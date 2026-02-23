using System.Buffers.Binary;
using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 16 — HTTP/2 Advanced: Edge case and corner case tests.
/// Covers immediately-closed streams, SETTINGS with multiple parameters,
/// unknown flags, GOAWAY mid-connection, PRIORITY frames, long URIs,
/// and :authority with explicit port numbers.
/// </summary>
[Collection("Http2Advanced")]
public sealed class Http2EdgeCaseTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2EdgeCaseTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Immediately Closed Stream ─────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-060: Immediately closed stream (HEADERS + END_STREAM, no DATA) — decoded correctly")]
    public async Task Should_ReturnEmptyBodyResponse_When_StreamImmediatelyClosed()
    {
        // GET /status/204 returns HEADERS with END_STREAM set and no DATA frame.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/204"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    [Fact(DisplayName = "IT-2A-061: Immediately closed stream via decoder — HEADERS with END_STREAM returns response")]
    public void Should_ReturnResponse_When_HeadersFrameHasEndStream()
    {
        var decoder = new Http2Decoder();
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "204")]);
        var frame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(frame, streamId: 1, headerBlock.Span, endStream: true, endHeaders: true);

        var decoded = decoder.TryDecode(frame.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        Assert.Equal(1, result.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.NoContent, result.Responses[0].Response.StatusCode);
    }

    // ── SETTINGS with Multiple Parameters ────────────────────────────────────

    [Fact(DisplayName = "IT-2A-062: SETTINGS with multiple parameters in one frame — all applied correctly")]
    public async Task Should_ApplyAllSettings_When_SettingsFrameHasMultipleParameters()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // Send SETTINGS with multiple parameters at once.
        await conn.SendSettingsAsync([
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.EnablePush, 0u),
            (SettingsParameter.InitialWindowSize, 65535u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);

        await Task.Delay(50);

        // Connection remains functional after multi-parameter SETTINGS.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "IT-2A-063: SETTINGS decoder parses multiple parameters in one frame")]
    public void Should_ParseAllParameters_When_SettingsFrameContainsMultipleEntries()
    {
        var decoder = new Http2Decoder();
        var settings = Http2Encoder.EncodeSettings([
            (SettingsParameter.HeaderTableSize, 8192u),
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 32768u),
        ]);

        var decoded = decoder.TryDecode(settings.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.ReceivedSettings);
        Assert.Equal(3, result.ReceivedSettings[0].Count);

        Assert.Contains(result.ReceivedSettings[0],
            s => s.Item1 == SettingsParameter.HeaderTableSize && s.Item2 == 8192u);
        Assert.Contains(result.ReceivedSettings[0],
            s => s.Item1 == SettingsParameter.MaxConcurrentStreams && s.Item2 == 100u);
        Assert.Contains(result.ReceivedSettings[0],
            s => s.Item1 == SettingsParameter.InitialWindowSize && s.Item2 == 32768u);
    }

    // ── PING Round-Trip ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-064: PING with 8-byte opaque data round-trip — ACK data matches")]
    public async Task Should_EchoOpaqueData_When_PingAckReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x23, 0x45, 0x67 };
        var ack = await conn.PingAsync(data);
        Assert.Equal(data, ack);
    }

    // ── Unknown Frame Handling ────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-065: Unknown frame type 0xFE — decoder ignores silently (RFC 7540 §4.1)")]
    public void Should_IgnoreUnknownFrameType_When_Frame0xFeReceived()
    {
        var decoder = new Http2Decoder();
        var frameBytes = new byte[9 + 8];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 8;
        frameBytes[3] = 0xFE; // unknown type
        BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1); // stream 1

        // Must not throw.
        var decoded = decoder.TryDecode(frameBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Empty(result.Responses);
        Assert.Null(result.GoAway);
    }

    [Fact(DisplayName = "IT-2A-066: Unknown flags on HEADERS frame — decoder processes frame normally")]
    public void Should_ProcessNormally_When_HeadersFrameHasUnknownFlags()
    {
        var decoder = new Http2Decoder();
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]);

        var frame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(frame, streamId: 1, headerBlock.Span, endStream: true, endHeaders: true);

        // Set an unknown flag (bit 6 = 0x40, not defined for HEADERS).
        frame[4] |= 0x40;

        // Should not throw — unknown flags on known frame types are ignored per RFC 7540 §4.1.
        var decoded = decoder.TryDecode(frame.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
    }

    // ── GOAWAY Mid-Connection ─────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-067: GOAWAY received mid-connection — decoder returns both response and GOAWAY")]
    public void Should_ReturnResponseAndGoAway_When_BothDecodedInSameBatch()
    {
        var decoder = new Http2Decoder();

        // Build HEADERS response for stream 1 with END_STREAM
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]);
        var headersFrame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(headersFrame, streamId: 1, headerBlock.Span, endStream: true, endHeaders: true);

        // Build GOAWAY frame (lastStreamId=1, NO_ERROR)
        var goAwayFrame = Http2Encoder.EncodeGoAway(1, Http2ErrorCode.NoError);

        // Feed both in a single call to TryDecode.
        var batch = new byte[headersFrame.Length + goAwayFrame.Length];
        headersFrame.CopyTo(batch, 0);
        goAwayFrame.CopyTo(batch, headersFrame.Length);

        var decoded = decoder.TryDecode(batch.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.NotNull(result.GoAway);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway!.ErrorCode);
    }

    [Fact(DisplayName = "IT-2A-068: Connection reuse after SETTINGS_MAX_CONCURRENT_STREAMS update")]
    public async Task Should_RemainFunctional_When_MaxConcurrentStreamsIncreased()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // Use the connection for a first request.
        var req1 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var resp1 = await conn.SendAndReceiveAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Server-side, send client SETTINGS increasing MAX_CONCURRENT_STREAMS.
        await conn.SendSettingsAsync([(SettingsParameter.MaxConcurrentStreams, 100u)]);
        await Task.Delay(50);

        // Connection still usable after SETTINGS update.
        var req2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var resp2 = await conn.SendAndReceiveAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("Hello World", await resp2.Content.ReadAsStringAsync());
    }

    // ── Priority Frames ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-069: PRIORITY frame decoded without error — ignored per RFC 9113")]
    public void Should_IgnorePriorityFrame_When_Received()
    {
        var decoder = new Http2Decoder();

        // Build a PRIORITY frame (type 0x2): 5-byte payload on stream 1.
        var frameBytes = new byte[9 + 5];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 5; // length = 5
        frameBytes[3] = (byte)FrameType.Priority;
        // flags = 0
        BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1); // stream 1
        // stream dependency = 0, exclusive = 0, weight = 15 (payload already zero)
        frameBytes[13] = 15; // weight (actual = value + 1 = 16)

        // Must not throw; result should have no responses.
        var decoded = decoder.TryDecode(frameBytes.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Empty(result.Responses);
        Assert.Null(result.GoAway);
    }

    // ── Long URI ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-070: Very long :path value (4 KB URI) — server responds with 200")]
    public async Task Should_Return200_When_PathIs4KbLong()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // Build a URI with a ~4 KB query string.
        var padding = new string('x', 4000);
        var uri = Http2Helper.BuildUri(_fixture.Port, $"/h2/echo-path?q={padding}");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("q=", body);
    }

    // ── :authority with Port ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-071: :authority with explicit port number — request succeeds")]
    public async Task Should_Return200_When_AuthorityIncludesExplicitPort()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // The URI already includes the port number, so :authority = "127.0.0.1:{port}".
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{_fixture.Port}/ping"));

        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
    }
}
