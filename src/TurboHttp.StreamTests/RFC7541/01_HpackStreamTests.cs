using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC7541;

/// <summary>
/// Tests HPACK header compression through the HTTP/2 request pipeline per RFC 7541.
/// Verifies dynamic table indexing, Huffman encoding, and header eviction across sequential requests.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="HpackEncoder"/> and <see cref="HpackDecoder"/> via <see cref="Request2FrameStage"/>.
/// RFC 7541 §2–§6: HPACK header compression, dynamic table management, and Huffman encoding.
/// </remarks>
public sealed class HpackStreamTests : StreamTestBase
{
    /// <summary>
    /// Runs requests through StreamIdAllocatorStage → Request2FrameStage using a shared encoder,
    /// preserving HPACK dynamic table state across requests.
    /// </summary>
    private async Task<IReadOnlyList<Http2Frame>> RunAsync(Http2RequestEncoder encoder,
        params HttpRequestMessage[] requests)
    {
        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocatorStage()))
            .Via(Flow.FromGraph(new Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    /// <summary>
    /// Extracts the raw HPACK header block bytes from a HEADERS frame.
    /// </summary>
    private static ReadOnlyMemory<byte> GetHeaderBlock(Http2Frame frame)
        => Assert.IsType<HeadersFrame>(frame).HeaderBlockFragment;

    /// <summary>
    /// Decodes HPACK header block into header fields.
    /// </summary>
    private static List<HpackHeader> DecodeHeaders(HeadersFrame frame)
        => new HpackDecoder().Decode(frame.HeaderBlockFragment.Span);

    private static HttpRequestMessage GetRequest(string uri = "http://example.com/")
        => new(HttpMethod.Get, uri);

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-001: Static table :method GET transmitted as indexed byte 0x82")]
    public async Task Should_TransmitGetAsIndexedByte_When_UsingStaticTable()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await RunAsync(encoder, GetRequest());

        var headerBlock = GetHeaderBlock(frames[0]);
        var bytes = headerBlock.Span;

        // RFC 7541 §6.1: Indexed Header Field starts with bit pattern 1xxxxxxx.
        // Static table index 2 = ":method GET" → encoded as single byte 0x82.
        Assert.True(bytes.Length > 0, "Header block must not be empty");
        Assert.Equal(0x82, bytes[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-001: Static table :method GET uses single-byte indexed representation")]
    public async Task Should_UseSingleByteEncoding_When_GetMethodHasStaticTableEntry()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await RunAsync(encoder, GetRequest());

        var headerBlock = GetHeaderBlock(frames[0]);
        var bytes = headerBlock.Span;

        // The first byte is 0x82 (indexed :method GET). The second byte must NOT be
        // a continuation of the integer (it would need the 0x80 continuation bit).
        // This confirms the static index fits in a single byte.
        Assert.True(bytes.Length >= 2, "Header block must have at least 2 bytes");
        Assert.Equal(0x82, bytes[0]);

        // Verify decoded headers contain :method = GET
        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var method = Assert.Single(headers, h => h.Name == ":method");
        Assert.Equal("GET", method.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-002: Repeated custom header produces smaller block on 2nd request")]
    public async Task Should_ProduceSmallerBlock_When_CustomHeaderRepeatedAcrossRequests()
    {
        // Use the same encoder so the dynamic table persists across requests.
        var encoder = new Http2RequestEncoder();

        var req1 = GetRequest();
        req1.Headers.TryAddWithoutValidation("x-custom-token", "some-long-value-that-should-be-indexed");

        var req2 = GetRequest();
        req2.Headers.TryAddWithoutValidation("x-custom-token", "some-long-value-that-should-be-indexed");

        var frames = await RunAsync(encoder, req1, req2);

        // Find the two HEADERS frames (one per request)
        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(2, headersFrames.Count);

        var block1Size = headersFrames[0].HeaderBlockFragment.Length;
        var block2Size = headersFrames[1].HeaderBlockFragment.Length;

        // The 2nd block must be smaller because x-custom-token is now in the dynamic table
        // and can be referenced by index instead of literal name+value.
        Assert.True(block2Size < block1Size,
            $"2nd header block ({block2Size} bytes) should be smaller than 1st ({block1Size} bytes) due to dynamic table indexing");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-002: 2nd request custom header is indexed from dynamic table")]
    public async Task Should_DecodeCorrectly_When_CustomHeaderIndexedFromDynamicTable()
    {
        var encoder = new Http2RequestEncoder();

        var req1 = GetRequest();
        req1.Headers.TryAddWithoutValidation("x-repeat", "value123");

        var req2 = GetRequest();
        req2.Headers.TryAddWithoutValidation("x-repeat", "value123");

        var frames = await RunAsync(encoder, req1, req2);

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(2, headersFrames.Count);

        // Both requests decode to the same custom header despite different encodings
        var decoder = new HpackDecoder();
        var headers1 = decoder.Decode(headersFrames[0].HeaderBlockFragment.Span);
        var headers2 = decoder.Decode(headersFrames[1].HeaderBlockFragment.Span);

        var custom1 = Assert.Single(headers1, h => h.Name == "x-repeat");
        var custom2 = Assert.Single(headers2, h => h.Name == "x-repeat");

        Assert.Equal("value123", custom1.Value);
        Assert.Equal("value123", custom2.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-003: 3 requests with same host show progressive compression")]
    public async Task Should_ShowProgressiveCompression_When_ThreeRequestsUseSameHost()
    {
        var encoder = new Http2RequestEncoder();

        // 3 identical GET requests to the same host — pseudo-headers (:authority, :scheme, :path, :method)
        // and any custom headers get progressively indexed in the dynamic table.
        var req1 = GetRequest("http://example.com/api/data");
        req1.Headers.TryAddWithoutValidation("x-session", "abc123");

        var req2 = GetRequest("http://example.com/api/data");
        req2.Headers.TryAddWithoutValidation("x-session", "abc123");

        var req3 = GetRequest("http://example.com/api/data");
        req3.Headers.TryAddWithoutValidation("x-session", "abc123");

        var frames = await RunAsync(encoder, req1, req2, req3);

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(3, headersFrames.Count);

        var size1 = headersFrames[0].HeaderBlockFragment.Length;
        var size2 = headersFrames[1].HeaderBlockFragment.Length;
        var size3 = headersFrames[2].HeaderBlockFragment.Length;

        // 2nd request should be smaller than 1st (dynamic table populated)
        Assert.True(size2 < size1,
            $"2nd block ({size2} bytes) should be smaller than 1st ({size1} bytes)");

        // 3rd request should be same size or smaller than 2nd (fully indexed)
        Assert.True(size3 <= size2,
            $"3rd block ({size3} bytes) should be <= 2nd ({size2} bytes)");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-003: Progressive compression - all 3 responses decode correctly")]
    public async Task Should_DecodeAllFramesCorrectly_When_ProgressiveCompressionApplied()
    {
        var encoder = new Http2RequestEncoder();

        var requests = Enumerable.Range(0, 3)
            .Select(_ =>
            {
                var r = GetRequest("http://example.com/path");
                r.Headers.TryAddWithoutValidation("x-trace", "trace-val");
                return r;
            })
            .ToArray();

        var frames = await RunAsync(encoder, requests);

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(3, headersFrames.Count);

        // Use a single decoder to maintain dynamic table state in sync
        var decoder = new HpackDecoder();
        foreach (var hf in headersFrames)
        {
            var headers = decoder.Decode(hf.HeaderBlockFragment.Span);
            var authority = Assert.Single(headers, h => h.Name == ":authority");
            Assert.Equal("example.com", authority.Value);

            var trace = Assert.Single(headers, h => h.Name == "x-trace");
            Assert.Equal("trace-val", trace.Value);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-004: Huffman encoding produces smaller header block than without")]
    public async Task Should_ProduceSmallerHeaderBlock_When_HuffmanEncodingIsUsed()
    {
        // Encoder WITHOUT Huffman
        var encoderNoHuff = new Http2RequestEncoder(useHuffman: false);
        // Encoder WITH Huffman
        var encoderHuff = new Http2RequestEncoder(useHuffman: true);

        var reqNoHuff = GetRequest("http://example.com/api/long-path-for-huffman-test");
        reqNoHuff.Headers.TryAddWithoutValidation("x-custom-header", "a-moderately-long-header-value");

        var reqHuff = GetRequest("http://example.com/api/long-path-for-huffman-test");
        reqHuff.Headers.TryAddWithoutValidation("x-custom-header", "a-moderately-long-header-value");

        var framesNoHuff = await RunAsync(encoderNoHuff, reqNoHuff);
        var framesHuff = await RunAsync(encoderHuff, reqHuff);

        var blockNoHuff = Assert.IsType<HeadersFrame>(framesNoHuff[0]).HeaderBlockFragment;
        var blockHuff = Assert.IsType<HeadersFrame>(framesHuff[0]).HeaderBlockFragment;

        Assert.True(blockHuff.Length < blockNoHuff.Length,
            $"Huffman block ({blockHuff.Length} bytes) should be smaller than raw block ({blockNoHuff.Length} bytes)");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC7541-2-H2HP-004: Huffman-encoded block decodes to same headers as non-Huffman")]
    public async Task Should_DecodeToSameHeaders_When_HuffmanAndRawEncodingsCompared()
    {
        var encoderNoHuff = new Http2RequestEncoder(useHuffman: false);
        var encoderHuff = new Http2RequestEncoder(useHuffman: true);

        var reqNoHuff = GetRequest("http://example.com/test");
        reqNoHuff.Headers.TryAddWithoutValidation("x-data", "hello-world");

        var reqHuff = GetRequest("http://example.com/test");
        reqHuff.Headers.TryAddWithoutValidation("x-data", "hello-world");

        var framesNoHuff = await RunAsync(encoderNoHuff, reqNoHuff);
        var framesHuff = await RunAsync(encoderHuff, reqHuff);

        var headersNoHuff = DecodeHeaders(Assert.IsType<HeadersFrame>(framesNoHuff[0]));
        var headersHuff = DecodeHeaders(Assert.IsType<HeadersFrame>(framesHuff[0]));

        // Same number of headers
        Assert.Equal(headersNoHuff.Count, headersHuff.Count);

        // Each header name/value pair matches
        for (var i = 0; i < headersNoHuff.Count; i++)
        {
            Assert.Equal(headersNoHuff[i].Name, headersHuff[i].Name);
            Assert.Equal(headersNoHuff[i].Value, headersHuff[i].Value);
        }
    }
}
