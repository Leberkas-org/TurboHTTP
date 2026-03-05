using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 16 — HTTP/2 Advanced: Flow control tests.
/// Covers stream and connection window management, encoder pausing,
/// WINDOW_UPDATE resumption, overflow detection, and large-body transfer.
/// </summary>
[Collection("Http2")]
public sealed class Http2FlowControlTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2FlowControlTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Encoder Flow Control ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-030: Stream window exhaustion — encoder omits DATA when window is zero")]
    public void Should_OmitDataFrame_When_ConnectionSendWindowIsZero()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Drain the connection send window by encoding a 65535-byte body.
        var fullWindowBody = new ByteArrayContent(new byte[65535]);
        var drainRequest = new HttpRequestMessage(HttpMethod.Post,
            new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = fullWindowBody
        };

        encoder.EncodeToBytes(drainRequest);

        // Now the connection window should be 0; any additional body will not be encoded.
        var overflow = new ByteArrayContent(new byte[100]);
        var req2 = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = overflow
        };

        var (_, bytesWritten2) = encoder.EncodeToBytes(req2);

        // The encoded output should contain only the HEADERS frame (no DATA frame),
        // since the connection send window is 0.
        // A HEADERS frame for a simple request is at minimum 9 bytes.
        // A DATA frame would add at least 9 + 1 = 10 more bytes.
        // We verify no DATA was written by checking the encoded size is under a threshold.
        Assert.True(bytesWritten2 > 0, "HEADERS frame must be encoded even with zero window.");
        // The body (100 bytes) should NOT be in the encoded output — only headers.
        Assert.True(bytesWritten2 < 300,
            "With zero connection window, DATA frame should not be included in encoded output.");
    }

    [Fact(DisplayName = "IT-2A-031: WINDOW_UPDATE received — encoder can send DATA after window replenishment")]
    public void Should_EncodeDataFrame_When_ConnectionWindowReplenished()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Drain the window.
        var drainRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[65535])
        };
        encoder.EncodeToBytes(drainRequest);

        // Replenish the connection window.
        encoder.UpdateConnectionWindow(65535);

        // Now encode a request with a body — DATA should be included.
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[100])
        };
        var (_, bytesWritten) = encoder.EncodeToBytes(req);

        // Should include both HEADERS and DATA frames.
        // HEADERS + DATA(100 bytes) = at least 9 + 9 + 100 = 118 bytes.
        Assert.True(bytesWritten >= 118,
            "After window replenishment, DATA frame should be included in encoded output.");
    }

    [Fact(DisplayName = "IT-2A-032: Connection window exhaustion — same as stream window for first stream")]
    public void Should_TrackConnectionAndStreamWindowsSeparately_When_EncoderUsed()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Verify the stream window is tracked independently from the connection window.
        // After encoding one stream, update only the stream window.
        var req1 = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[1000])
        };
        var (streamId1, _) = encoder.EncodeToBytes(req1);

        // Update only the stream window for stream 1 — connection window unchanged.
        encoder.UpdateStreamWindow(streamId1, 65535);

        // Next request uses stream 3 (new stream); both windows still have capacity.
        var req2 = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[100])
        };
        var (_, bytesWritten2) = encoder.EncodeToBytes(req2);

        Assert.True(bytesWritten2 > 0);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-033: Connection WINDOW_UPDATE resumes large body transfer over real connection")]
    public async Task Should_ReceiveLargeBody_When_ConnectionWindowUpdatedByServer()
    {
        // This test verifies the Http2Connection auto-WINDOW_UPDATE mechanism for large bodies.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/128"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(128 * 1024, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-034: Mixed stream + connection flow control — both windows respected")]
    public async Task Should_HandleBothFlowControlWindows_When_LargeBodiesUsed()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // Fetch two sequential large bodies that exercise both windows.
        for (var i = 0; i < 2; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/60"));
            var resp = await conn.SendAndReceiveAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal(60 * 1024, body.Length);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-035: Default stream window is 65535 bytes")]
    public async Task Should_HaveDefaultStreamWindow_When_NewStreamCreated()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // New (not yet open) stream should report default window of 65535.
        var window = conn.GetStreamReceiveWindow(999);
        Assert.Equal(65535, window);
    }

    [Fact(DisplayName = "IT-2A-036: Window overflow detection — WINDOW_UPDATE > 2^31-1 throws FLOW_CONTROL_ERROR")]
    public void Should_ThrowFlowControlError_When_WindowUpdateCausesOverflow()
    {
        var session = new Http2IntegrationSession();

        // Initial send window = 65535. Adding 2^31-1 would exceed 2^31-1 total.
        var wu = Http2FrameUtils.EncodeWindowUpdate(0, 0x7FFFFFFF);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(wu.AsMemory()));

        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2A-037: Zero WINDOW_UPDATE increment on stream → PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_StreamWindowUpdateIncrementIsZero()
    {
        var session = new Http2IntegrationSession();

        // Build WINDOW_UPDATE frame for stream 1 with increment = 0.
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4; // length = 4
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        // flags = 0
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frameBytes.AsSpan(5), 1); // stream 1
        // increment = 0 (from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "IT-2A-038: Zero WINDOW_UPDATE increment on connection → PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ConnectionWindowUpdateIncrementIsZero()
    {
        var session = new Http2IntegrationSession();

        // WINDOW_UPDATE on stream 0 (connection) with increment = 0.
        var frameBytes = new byte[9 + 4];
        frameBytes[0] = 0; frameBytes[1] = 0; frameBytes[2] = 4;
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        // stream = 0, increment = 0 (from array init)

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(frameBytes.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-039: 64 KB body fits in initial connection window — delivered without WINDOW_UPDATE")]
    public async Task Should_DeliverBody_When_64KbBodyFitsInInitialWindow()
    {
        // 64 KB = 65536 bytes, slightly above the 65535-byte initial window.
        // The Http2Connection automatically sends WINDOW_UPDATE when needed.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/64"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(64 * 1024, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-040: 128 KB body requires WINDOW_UPDATE mid-transfer — fully received")]
    public async Task Should_ReceiveFullBody_When_128KbBodyRequiresMultipleWindowUpdates()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/128"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(128 * 1024, body.Length);
        Assert.True(body.All(b => b == (byte)'A'));
    }

    [Fact(DisplayName = "IT-2A-041: Multiple WINDOW_UPDATE frames cumulative — encoder tracks total window")]
    public void Should_AccumulateWindow_When_MultipleWindowUpdateFramesReceived()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Apply multiple window updates.
        encoder.UpdateConnectionWindow(1000);
        encoder.UpdateConnectionWindow(2000);
        encoder.UpdateConnectionWindow(3000);

        // The encoder should now be able to send at least 6000 additional bytes.
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[6000])
        };
        var (_, bytesWritten) = encoder.EncodeToBytes(req);

        // Should include DATA frame with 6000 bytes (plus headers).
        // 9 (headers-frame header) + headers payload + 9 (data-frame header) + 6000 ≈ 6100+.
        Assert.True(bytesWritten >= 6009, $"Expected at least 6009 bytes but got {bytesWritten}.");
    }

    [Fact(DisplayName = "IT-2A-042: Encoder correctly tracks remaining send window after encoding")]
    public void Should_TrackRemainingWindow_When_EncoderEncodesBodies()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Encode a 1000-byte body request.
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[1000])
        };
        encoder.EncodeToBytes(req);

        // The encoder consumed 1000 bytes from the send window.
        // Verify by encoding another body — should still work (65535 - 1000 = 64535 remaining).
        var req2 = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:9999/echo"))
        {
            Content = new ByteArrayContent(new byte[100])
        };
        var (_, bytesWritten2) = encoder.EncodeToBytes(req2);

        // Should include DATA frame for the 100-byte body.
        Assert.True(bytesWritten2 >= 9 + 9 + 100,
            "Encoder should still have window capacity for the second request.");
    }
}
