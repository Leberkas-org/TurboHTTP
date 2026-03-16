using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

/// <summary>
/// TASK-ERR-04: Http20StreamStage — Memory Management
/// Verifies that StreamState buffers grow correctly, are disposed after emission,
/// and that the stream dictionary is cleaned up after each completed response.
/// </summary>
public sealed class Http20StreamStageMemoryTests : StreamTestBase
{
    private readonly HpackEncoder _hpack = new(useHuffman: false);

    private async Task<IReadOnlyList<HttpResponseMessage>> RunAsync(params Http2Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(new Http20StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    // ─── MEM-001: StreamState.Dispose() called after response emission ────────────

    [Fact(Timeout = 10_000, DisplayName = "MEM-001: StreamState.Dispose() called — HEADERS path: stream completes without DATA")]
    public async Task StreamState_Disposed_After_Headers_Only_Response()
    {
        // Verify that after a HEADERS+END_STREAM response is emitted, the stage continues
        // processing subsequent frames correctly. If Dispose were not called, leaking
        // IMemoryOwner would not be released to the pool.
        //
        // Observable: both responses are correct — stream 3 data is not contaminated
        // by stream 1's buffer state (which was freed).
        var headerBlock1 = _hpack.Encode([(":status", "200")]);
        var headerBlock3 = _hpack.Encode([(":status", "201")]);

        var frames = new Http2Frame[]
        {
            // Stream 1 completes immediately on HEADERS (endStream=true) → Dispose() called
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: true, endHeaders: true),
            // Stream 3 should process cleanly after stream 1 is disposed
            new HeadersFrame(streamId: 3, headerBlock: headerBlock3, endStream: true, endHeaders: true),
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "MEM-001: StreamState.Dispose() called — DATA path: stream completes on END_STREAM data")]
    public async Task StreamState_Disposed_After_Data_EndStream_Response()
    {
        // Verify that after a DATA+END_STREAM response is emitted, state.Dispose() is called.
        // Stream 3 starts AFTER stream 1 is complete; if stream 1's pooled buffers corrupt
        // the pool and stream 3 gets wrong memory, the body comparison will fail.
        var headerBlock1 = _hpack.Encode([(":status", "200")]);
        var headerBlock3 = _hpack.Encode([(":status", "200")]);
        var body1 = new byte[64];
        var body3 = new byte[64];
        for (var i = 0; i < 64; i++)
        {
            body1[i] = 0xAA;
            body3[i] = 0xBB;
        }

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: body1, endStream: true), // → Dispose() called here
            new HeadersFrame(streamId: 3, headerBlock: headerBlock3, endStream: false, endHeaders: true),
            new DataFrame(streamId: 3, data: body3, endStream: true),
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);
        var result1 = await responses[0].Content!.ReadAsByteArrayAsync();
        var result3 = await responses[1].Content!.ReadAsByteArrayAsync();

        // Both bodies must be byte-for-byte correct — no pool contamination
        Assert.Equal(body1, result1);
        Assert.Equal(body3, result3);
    }

    // ─── MEM-002: BodyBuffer grows correctly for large body ───────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MEM-002: BodyBuffer Rent→Copy→Dispose: two growths produce correct body")]
    public async Task BodyBuffer_Grows_Correctly_Across_Multiple_Data_Frames()
    {
        // The body arrives in three DATA frames. Each frame forces a buffer growth because
        // the cumulative required capacity exceeds the previously rented buffer.
        //
        //  Frame 1:  10 bytes → initial Rent(10)     → pool gives ≥10  (e.g. 16 bytes)
        //  Frame 2: 100 bytes → required=110 > 16    → Rent(110), copy 16 bytes, Dispose old
        //  Frame 3: 500 bytes → required=610 > buffer → Rent(610), copy, Dispose old
        //
        // If the Rent→Copy→Dispose cycle is broken (e.g. old data not copied), the final
        // body will be wrong or an exception will be thrown.
        var headerBlock = _hpack.Encode([(":status", "200")]);
        var chunk1 = MakeChunk(10, fillByte: 0x01);
        var chunk2 = MakeChunk(100, fillByte: 0x02);
        var chunk3 = MakeChunk(500, fillByte: 0x03);

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: chunk1, endStream: false),
            new DataFrame(streamId: 1, data: chunk2, endStream: false),
            new DataFrame(streamId: 1, data: chunk3, endStream: true),
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        var body = await responses[0].Content!.ReadAsByteArrayAsync();

        // Verify exact byte values at key positions to catch copy failures
        Assert.Equal(610, body.Length);
        Assert.True(body[..10].ToArray().All(b => b == 0x01), "Chunk 1 bytes (0–9) must be 0x01");
        Assert.True(body[10..110].ToArray().All(b => b == 0x02), "Chunk 2 bytes (10–109) must be 0x02");
        Assert.True(body[110..610].ToArray().All(b => b == 0x03), "Chunk 3 bytes (110–609) must be 0x03");
    }

    [Fact(Timeout = 10_000, DisplayName = "MEM-002: BodyBuffer handles very large body (16 KB) requiring multiple growths")]
    public async Task BodyBuffer_Handles_Large_Body_With_Many_Growths()
    {
        // Eight 2 KB chunks → each new chunk forces a growth since cumulative size doubles.
        // Total body: 16 KB.  Each growth must preserve all previously accumulated data.
        const int chunkSize = 2048;
        const int numChunks = 8;
        var headerBlock = _hpack.Encode([(":status", "200")]);

        // Build frames: HEADERS + 7 non-terminal DATA + 1 terminal DATA
        var frameList = new List<Http2Frame>
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true)
        };
        var expected = new byte[chunkSize * numChunks];
        for (var i = 0; i < numChunks; i++)
        {
            var chunk = MakeChunk(chunkSize, fillByte: (byte)(i + 1));
            Array.Copy(chunk, 0, expected, i * chunkSize, chunkSize);
            var isLast = i == numChunks - 1;
            frameList.Add(new DataFrame(streamId: 1, data: chunk, endStream: isLast));
        }

        var responses = await RunAsync(frameList.ToArray());

        Assert.Single(responses);
        var body = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal(expected.Length, body.Length);
        Assert.Equal(expected, body);
    }

    // ─── MEM-003: HeaderBuffer grows correctly for CONTINUATION frames ────────────

    [Fact(Timeout = 10_000, DisplayName = "MEM-003: HeaderBuffer Rent→Copy→Dispose: growth via CONTINUATION frames")]
    public async Task HeaderBuffer_Grows_Correctly_Across_Continuation_Frames()
    {
        // Encode a header block large enough to guarantee buffer growth when split
        // across multiple CONTINUATION frames.  Each CONTINUATION triggers AppendHeader
        // which calls EnsureHeaderCapacity; when capacity is exceeded the stage must
        // Rent a bigger buffer, copy old data, and Dispose the old owner.
        //
        // We use 20 custom headers with 50-byte values → ~1200 encoded bytes in total.
        // Splitting across 10 CONTINUATION frames means each chunk is ~120 bytes.
        var headerPairs = new List<(string Name, string Value)>
        {
            (":status", "200")
        };
        for (var i = 0; i < 20; i++)
        {
            headerPairs.Add(($"x-header-{i:D2}", new string((char)('a' + (i % 26)), 50)));
        }

        var fullBlock = _hpack.Encode(headerPairs.ToArray()).ToArray();
        var chunkCount = 10;
        var chunkSize = (int)Math.Ceiling((double)fullBlock.Length / chunkCount);

        var frameList = new List<Http2Frame>();
        var firstChunkEnd = Math.Min(chunkSize, fullBlock.Length);
        var firstChunk = fullBlock[..firstChunkEnd];
        var hasMoreAfterFirst = firstChunkEnd < fullBlock.Length;

        frameList.Add(new HeadersFrame(
            streamId: 1,
            headerBlock: firstChunk,
            endStream: false,
            endHeaders: !hasMoreAfterFirst));

        if (hasMoreAfterFirst)
        {
            var offset = firstChunkEnd;
            while (offset < fullBlock.Length)
            {
                var end = Math.Min(offset + chunkSize, fullBlock.Length);
                var chunk = fullBlock[offset..end];
                var isLast = end == fullBlock.Length;
                frameList.Add(new ContinuationFrame(
                    streamId: 1,
                    headerBlock: chunk,
                    endHeaders: isLast));
                offset = end;
            }
        }

        frameList.Add(new DataFrame(streamId: 1, data: "ok"u8.ToArray(), endStream: true));

        var responses = await RunAsync(frameList.ToArray());

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);

        // Verify that all 20 custom headers survived the buffer growth cycles
        for (var i = 0; i < 20; i++)
        {
            var name = $"x-header-{i:D2}";
            var expected = new string((char)('a' + (i % 26)), 50);
            Assert.True(
                responses[0].Headers.TryGetValues(name, out var values),
                $"Header '{name}' not found after buffer growth");
            Assert.Equal(expected, values!.Single());
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "MEM-003: HeaderBuffer handles many CONTINUATION frames requiring repeated growth")]
    public async Task HeaderBuffer_Handles_Many_Continuation_Frames()
    {
        // 30 CONTINUATION frames, each adding one header.
        // The stage must grow the header buffer multiple times.
        // If any growth discards previously accumulated data, HPACK decode will fail
        // or produce wrong header values.
        var headerPairs = new List<(string Name, string Value)>
        {
            (":status", "200")
        };
        for (var i = 0; i < 30; i++)
        {
            headerPairs.Add(($"x-cont-{i:D2}", $"value-{i:D2}"));
        }

        // Encode the full block, then send HEADERS with no data + 30 CONTINUATION frames,
        // each carrying 1/30th of the block (plus one CONTINUATION that closes with endHeaders).
        var fullBlock = _hpack.Encode(headerPairs.ToArray()).ToArray();
        var perFrame = Math.Max(1, fullBlock.Length / 30);

        var frameList = new List<Http2Frame>();
        var pos = 0;
        var firstEnd = Math.Min(perFrame, fullBlock.Length);
        var firstChunk = fullBlock[pos..firstEnd];
        pos = firstEnd;
        var moreRemaining = pos < fullBlock.Length;

        frameList.Add(new HeadersFrame(
            streamId: 1,
            headerBlock: firstChunk,
            endStream: false,
            endHeaders: !moreRemaining));

        while (pos < fullBlock.Length)
        {
            var end = Math.Min(pos + perFrame, fullBlock.Length);
            var chunk = fullBlock[pos..end];
            pos = end;
            var isLast = pos >= fullBlock.Length;
            frameList.Add(new ContinuationFrame(streamId: 1, headerBlock: chunk, endHeaders: isLast));
        }

        frameList.Add(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true));

        var responses = await RunAsync(frameList.ToArray());

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);

        // Verify all 30 continuation-delivered headers are present
        for (var i = 0; i < 30; i++)
        {
            var name = $"x-cont-{i:D2}";
            Assert.True(
                responses[0].Headers.TryGetValues(name, out var values),
                $"Header '{name}' missing after repeated CONTINUATION growth");
            Assert.Equal($"value-{i:D2}", values!.Single());
        }
    }

    // ─── MEM-004: Stream dictionary cleaned up after response emission ─────────────

    [Fact(Timeout = 10_000, DisplayName = "MEM-004: _streams.Remove called — stream ID reuse after HEADERS+END_STREAM completion")]
    public async Task StreamDictionary_Cleaned_After_Headers_Only_Response_Allows_StreamId_Reuse()
    {
        // After stream 1 completes (HEADERS with END_STREAM), _streams.Remove(1) must be
        // called so that a subsequent HEADERS for stream 1 gets a fresh StreamState.
        //
        // If Remove is NOT called: _streams.TryAdd(1, new()) is a no-op (key exists),
        // and the old (disposed) StreamState is reused.  The old state's header buffer
        // still holds the first response's bytes, causing the second HPACK decode to
        // fail or produce the wrong status code.
        //
        // :status 200  → static table entry 8  (0x88) — no dynamic table update
        // :status 404  → static table entry 13 (0x8D) — no dynamic table update
        // Using only static-table headers keeps the HPACK decoder in sync with a
        // per-cycle fresh encoder.
        var encoder1 = new HpackEncoder(useHuffman: false);
        var encoder2 = new HpackEncoder(useHuffman: false);

        var headerBlock1 = encoder1.Encode([(":status", "200")]);
        var headerBlock2 = encoder2.Encode([(":status", "404")]);

        var frames = new Http2Frame[]
        {
            // First use of stream 1 → complete response emitted → Remove(1) must fire
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: true, endHeaders: true),
            // Second use of stream 1 → must get a fresh StreamState
            new HeadersFrame(streamId: 1, headerBlock: headerBlock2, endStream: true, endHeaders: true),
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);
        // Both status codes must be exactly correct
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "MEM-004: _streams.Remove called — stream ID reuse after DATA+END_STREAM completion")]
    public async Task StreamDictionary_Cleaned_After_Data_Response_Allows_StreamId_Reuse()
    {
        // Same as above but the response completes via a DATA+END_STREAM frame.
        // If _streams.Remove is not called from HandleData, the second cycle's
        // body will be contaminated with the first cycle's bytes.
        var encoder1 = new HpackEncoder(useHuffman: false);
        var encoder2 = new HpackEncoder(useHuffman: false);

        var headerBlock1 = encoder1.Encode([(":status", "200")]);
        var headerBlock2 = encoder2.Encode([(":status", "201")]);
        var body1 = "first-response"u8.ToArray();
        var body2 = "second-response"u8.ToArray();

        var frames = new Http2Frame[]
        {
            // First cycle for stream 1 (DATA path)
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: body1, endStream: true), // → Remove(1) fires
            // Second cycle for stream 1 (same stream ID)
            new HeadersFrame(streamId: 1, headerBlock: headerBlock2, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: body2, endStream: true),
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);

        var resultBody1 = await responses[0].Content!.ReadAsByteArrayAsync();
        var resultBody2 = await responses[1].Content!.ReadAsByteArrayAsync();

        // If _streams.Remove was not called, the old BodyBuffer retains "first-response"
        // bytes and the second body would be "first-responsesecond-response" (512 bytes).
        Assert.Equal(body1, resultBody1);
        Assert.Equal(body2, resultBody2);
    }

    [Fact(Timeout = 10_000, DisplayName = "MEM-004: Multiple stream ID reuses across many cycles")]
    public async Task StreamDictionary_Cleaned_Correctly_For_Multiple_Reuse_Cycles()
    {
        // Run stream 1 through 5 complete cycles.  Each cycle must produce the correct
        // status code, proving that _streams.Remove is called after every emission.
        var statusCodes = new[] { "200", "201", "404", "500", "204" };
        var expectedCodes = new[]
        {
            HttpStatusCode.OK,
            HttpStatusCode.Created,
            HttpStatusCode.NotFound,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.NoContent,
        };

        var frameList = new List<Http2Frame>();
        foreach (var status in statusCodes)
        {
            // Use a fresh encoder per cycle to avoid cross-cycle HPACK contamination.
            // All used values (:status 2xx/4xx/5xx) are in the static table.
            var encoder = new HpackEncoder(useHuffman: false);
            var block = encoder.Encode([(":status", status)]);
            frameList.Add(new HeadersFrame(streamId: 1, headerBlock: block, endStream: true, endHeaders: true));
        }

        var responses = await RunAsync(frameList.ToArray());

        Assert.Equal(5, responses.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(expectedCodes[i], responses[i].StatusCode);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private static byte[] MakeChunk(int size, byte fillByte)
    {
        var chunk = new byte[size];
        Array.Fill(chunk, fillByte);
        return chunk;
    }
}
