using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2EncoderStreamSettingsTests
{
    [Theory(DisplayName = "enc5-set-001: SETTINGS parameter {param} encoded correctly")]
    [InlineData(SettingsParameter.HeaderTableSize, 4096u)]
    [InlineData(SettingsParameter.EnablePush, 0u)]
    [InlineData(SettingsParameter.MaxConcurrentStreams, 100u)]
    [InlineData(SettingsParameter.InitialWindowSize, 65535u)]
    [InlineData(SettingsParameter.MaxFrameSize, 16384u)]
    [InlineData(SettingsParameter.MaxHeaderListSize, 8192u)]
    public void Settings_Parameter_EncodedCorrectly(SettingsParameter param, uint value)
    {
        var frame = Http2FrameUtils.EncodeSettings([(param, value)]);
        Assert.Equal((byte)FrameType.Settings, frame[3]);
        Assert.Equal(0, frame[4]); // not ACK

        // payload: 6 bytes — 2-byte key + 4-byte value
        var key = (SettingsParameter)((frame[9] << 8) | frame[10]);
        var val = (uint)((frame[11] << 24) | (frame[12] << 16) | (frame[13] << 8) | frame[14]);
        Assert.Equal(param, key);
        Assert.Equal(value, val);
    }

    [Fact(DisplayName = "enc5-set-002: SETTINGS ACK frame has type=0x04 flags=0x01 stream=0")]
    public void SettingsAck_HasCorrectTypeAndFlags()
    {
        var ack = Http2FrameUtils.EncodeSettingsAck();
        Assert.Equal((byte)FrameType.Settings, ack[3]);         // type = 0x04
        Assert.Equal((byte)Settings.Ack, ack[4]);          // flags = 0x01
        var streamId = BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(5)) & 0x7FFFFFFFu;
        Assert.Equal(0u, streamId);                             // stream = 0
    }

    [Fact(DisplayName = "7540-5.1-001: First request uses stream ID 1")]
    public void StreamId_FirstRequest_IsOne()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buf = owner.Memory;
        var (id, _) = encoder.Encode(req, ref buf);
        Assert.Equal(1, id);
    }

    [Fact(DisplayName = "enc5-sid-001: Client never produces even stream IDs")]
    public void StreamId_NeverEven()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);

        for (var i = 0; i < 10; i++)
        {
            var buf = owner.Memory;
            var (id, _) = encoder.Encode(req, ref buf);
            Assert.Equal(1, id % 2); // all stream IDs must be odd
        }
    }

    [Fact(DisplayName = "enc5-sid-002: Stream ID approaching 2^31 handled gracefully")]
    public void StreamId_Near2Pow31_ThrowsGracefully()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Set _nextStreamId to the last valid odd value (2^31 - 1 = 0x7FFFFFFF)
        var field = typeof(Http2RequestEncoder).GetField("_nextStreamId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(encoder, 0x7FFFFFFF);

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);

        // The last valid stream ID should encode successfully
        var buf = owner.Memory;
        var (id, _) = encoder.Encode(req, ref buf);
        Assert.Equal(0x7FFFFFFF, id);

        // The next call must throw: stream ID space exhausted
        var buf2 = owner.Memory;
        Assert.Throws<Http2Exception>(() => encoder.Encode(req, ref buf2));
    }

    [Fact(DisplayName = "7540-5.2-enc-001: Encoder does not exceed initial 65535-byte window")]
    public void FlowControl_InitialWindow_LimitsToDefault()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var body = new string('X', 65535); // exactly fills the default window
        var request = CreatePostRequest("example.com", "/api", body);

        using var owner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        // Sum all DATA frame payload bytes
        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        long totalData = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data)
            {
                totalData += len;
            }

            offset += 9 + len;
        }

        Assert.Equal(65535L, totalData);
    }

    [Fact(DisplayName = "7540-5.2-enc-002: WINDOW_UPDATE allows more DATA to be sent")]
    public void FlowControl_WindowUpdate_AllowsMoreData()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Drain the initial connection window (65535) with stream 1
        using var drainOwner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var drainBuf = drainOwner.Memory;
        var drainReq = CreatePostRequest("example.com", "/drain", new string('X', 65535));
        encoder.Encode(drainReq, ref drainBuf); // connection window now = 0

        // Simulate server WINDOW_UPDATE: give 50000 more bytes on the connection
        encoder.UpdateConnectionWindow(50000);

        // Stream 3 uses default stream window (65535), connection window is now 50000
        // effective = min(50000, 65535) = 50000 → 50000 bytes can be sent
        var request = CreatePostRequest("example.com", "/api", new string('Y', 60000));

        using var owner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        long totalData = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data)
            {
                totalData += len;
            }

            offset += 9 + len;
        }

        // Without the WINDOW_UPDATE, 0 bytes could be sent; now 50000 should be sent
        Assert.Equal(50000L, totalData);
    }

    [Fact(DisplayName = "7540-5.2-enc-005: Encoder blocks when window is zero")]
    public void FlowControl_ZeroWindow_BlocksData()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Drain the full connection window with stream 1
        using var drainOwner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var drainBuf = drainOwner.Memory;
        var drainReq = CreatePostRequest("example.com", "/drain", new string('X', 65535));
        encoder.Encode(drainReq, ref drainBuf); // connection window = 0

        // With zero window, no DATA should be emitted for the next request
        var request = CreatePostRequest("example.com", "/api", "blocked body");
        using var owner = MemoryPool<byte>.Shared.Rent(65536);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        var hasNonEmptyData = false;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data && len > 0)
            {
                hasNonEmptyData = true;
            }

            offset += 9 + len;
        }

        Assert.False(hasNonEmptyData, "Encoder must not emit DATA when connection window is zero");
    }

    [Fact(DisplayName = "7540-5.2-enc-006: Connection-level window limits total DATA")]
    public void FlowControl_ConnectionWindow_LimitsTotalData()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Drain the full connection window with stream 1
        using var drainOwner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var drainBuf = drainOwner.Memory;
        var drainReq = CreatePostRequest("example.com", "/drain", new string('X', 65535));
        encoder.Encode(drainReq, ref drainBuf); // connection window = 0

        // Give exactly 100 bytes of connection window back
        encoder.UpdateConnectionWindow(100);

        // Stream 3 wants to send 500 bytes, but connection window = 100
        var request = CreatePostRequest("example.com", "/api", new string('Y', 500));
        using var owner = MemoryPool<byte>.Shared.Rent(65536);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        long totalData = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data)
            {
                totalData += len;
            }

            offset += 9 + len;
        }

        Assert.True(totalData <= 100, $"Connection window (100) exceeded: {totalData} bytes sent");
        Assert.Equal(100L, totalData); // exactly 100 bytes sent
    }

    [Fact(DisplayName = "7540-5.2-enc-007: Per-stream window limits DATA on that stream")]
    public void FlowControl_PerStreamWindow_LimitsStreamData()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);

        // Give a large connection window so it is not the limiting factor
        encoder.UpdateConnectionWindow(0x7FFFFFFF - 65535);

        // Pre-set stream 1's window to 50 bytes (before stream 1 is allocated).
        // UpdateStreamWindow adds to the current dict entry (0 for new streams),
        // so dict[1] = 50. When stream 1 encodes, effectiveWindow = min(huge, 50) = 50.
        encoder.UpdateStreamWindow(1, 50);

        var request = CreatePostRequest("example.com", "/api", new string('Y', 500));
        using var owner = MemoryPool<byte>.Shared.Rent(65536);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        long totalData = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data)
            {
                totalData += len;
            }

            offset += 9 + len;
        }

        Assert.True(totalData <= 50, $"Per-stream window (50) exceeded: {totalData} bytes sent");
        Assert.Equal(50L, totalData); // exactly 50 bytes sent
    }

    private static HttpRequestMessage CreateGetRequest(string host, string path, int port = 80, bool isHttps = false)
    {
        var uri = isHttps
            ? $"https://{host}{(port == 443 ? "" : $":{port}")}{path}"
            : $"http://{host}{(port == 80 ? "" : $":{port}")}{path}";
        return new HttpRequestMessage(HttpMethod.Get, uri);
    }

    private static HttpRequestMessage CreatePostRequest(string host, string path, string body)
    {
        var uri = $"https://{host}{path}";
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private static (int StreamId, byte[] Data) Encode(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2RequestEncoder(useHuffman);
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var (streamId, written) = encoder.Encode(request, ref buffer);
        return (streamId, owner.Memory.Span[..written].ToArray());
    }

    private static byte[] ExtractFirstHeaderBlock(ReadOnlySpan<byte> data)
    {
        var payloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        return data[9..(9 + payloadLen)].ToArray();
    }

    private static List<HpackHeader> DecodeHeaderList(byte[] data)
    {
        var payloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var headerBlock = data[9..(9 + payloadLen)];
        return new HpackDecoder().Decode(headerBlock).ToList();
    }
}
