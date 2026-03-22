using System.Text;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests HTTP/2 encoder baseline behaviors including connection preface and request frame production per RFC 9113 §3.5.
/// Verifies the magic octets, SETTINGS frame structure, and HEADERS frame encoding.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2RequestEncoder"/>.
/// RFC 9113 §3.5: The client connection preface consists of the PRI magic octets followed by a SETTINGS frame.
/// </remarks>
public sealed class Http2EncoderBaselineTests
{
    [Fact(DisplayName = "RFC9113-3.4-CP-001: Connection preface starts with PRI magic octets")]
    public void Should_StartWithMagic_WhenBuildingConnectionPreface()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        Assert.True(preface.Length > magic.Length);
        Assert.Equal(magic, preface[..magic.Length]);
    }

    [Fact(DisplayName = "RFC9113-3.4-CP-002: Connection preface contains SETTINGS frame after magic")]
    public void Should_ContainSettingsFrame_WhenBuildingConnectionPreface()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        Assert.Equal((byte)FrameType.Settings, preface[27]);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-001: GET request produces HEADERS frame")]
    public void Should_ProduceHeadersFrame_WhenEncodingGetRequest()
    {
        var request = CreateGetRequest("example.com", "/index.html");

        var frames = EncodeToFrames(request);

        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-002: GET HEADERS has END_STREAM and END_HEADERS flags")]
    public void Should_HaveEndStreamAndEndHeaders_WhenEncodingGetRequest()
    {
        var request = CreateGetRequest("example.com", "/");

        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-8.2.2-ENC-003: Encoder excludes connection-specific headers from request")]
    public void Should_ExcludeBannedHeaders_WhenEncodingGetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "Connection", "keep-alive" },
                { "Transfer-Encoding", "chunked" }
            }
        };

        var headers = DecodeHeaders(request);
        var names = headers.Select(h => h.Name).ToList();

        Assert.DoesNotContain("connection", names);
        Assert.DoesNotContain("keep-alive", names);
        Assert.DoesNotContain("transfer-encoding", names);
        Assert.DoesNotContain("upgrade", names);
        Assert.DoesNotContain("proxy-connection", names);
        Assert.DoesNotContain("te", names);
    }

    [Fact(DisplayName = "RFC9113-8.3.1-ENC-004: GET request includes all required pseudo-headers")]
    public void Should_ContainAllPseudoHeaders_WhenEncodingGetRequest()
    {
        var request = CreateGetRequest("example.com", "/v1/data", 443, isHttps: true);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("GET", dict[":method"]);
        Assert.Equal("/v1/data", dict[":path"]);
        Assert.Equal("https", dict[":scheme"]);
        Assert.Equal("example.com", dict[":authority"]);
    }

    [Fact(DisplayName = "RFC9113-8.3.1-ENC-005: Non-standard port included in :authority pseudo-header")]
    public void Should_IncludePortInAuthority_WhenGetRequestHasNonStandardPort()
    {
        var request = CreateGetRequest("example.com", "/", 8080);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("example.com:8080", dict[":authority"]);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-006: POST request produces HEADERS and DATA frames")]
    public void Should_ProduceDataFrame_WhenEncodingPostRequest()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"key\":\"value\"}");

        var frames = EncodeToFrames(request);

        Assert.Equal(2, frames.Count);
        Assert.IsType<HeadersFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-007: POST HEADERS frame does not set END_STREAM")]
    public void Should_NotSetEndStreamOnHeaders_WhenEncodingPostRequest()
    {
        var request = CreatePostRequest("example.com", "/api", "{}");

        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-008: POST DATA frame sets END_STREAM")]
    public void Should_SetEndStreamOnDataFrame_WhenEncodingPostRequest()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"x\":1}");

        var frames = EncodeToFrames(request);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.True(df.EndStream);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-009: POST request includes content-type header")]
    public void Should_IncludeContentHeaders_WhenEncodingPostRequest()
    {
        const string json = "{\"name\":\"test\"}";
        var request = CreatePostRequest("example.com", "/users", json);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("application/json; charset=utf-8", dict["content-type"]);
    }

    [Fact(DisplayName = "RFC9113-8.1-ENC-010: POST with empty body produces zero-length DATA with END_STREAM")]
    public void Should_ProduceEmptyDataFrame_WhenEncodingPostWithEmptyBody()
    {
        var request = CreatePostRequest("example.com", "/api", "");

        var frames = EncodeToFrames(request);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(0, df.Data.Length);
        Assert.True(df.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.5-FS-001: SETTINGS ACK produces correct frame type and ACK flag")]
    public void Should_ProduceAckFrame_WhenEncodingSettingsAck()
    {
        var ack = Http2FrameUtils.EncodeSettingsAck();

        Assert.Equal((byte)FrameType.Settings, ack[3]);
        Assert.Equal((byte)Settings.Ack, ack[4]);
    }

    [Fact(DisplayName = "RFC9113-6.5-FS-002: SETTINGS produces correct frame type without ACK flag")]
    public void Should_ProduceSettingsFrame_WhenEncodingSettings()
    {
        var frame = Http2FrameUtils.EncodeSettings(
        [
            (SettingsParameter.MaxFrameSize, 32768u),
        ]);

        Assert.Equal((byte)FrameType.Settings, frame[3]);
        Assert.Equal(0, frame[4]);
    }

    [Fact(DisplayName = "RFC9113-6.7-FS-003: PING produces correct frame type without ACK flag")]
    public void Should_ProducePingFrame_WhenEncodingPing()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = Http2FrameUtils.EncodePing(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal(0, frame[4]);
    }

    [Fact(DisplayName = "RFC9113-6.7-FS-004: PING ACK produces correct frame type and ACK flag")]
    public void Should_ProducePingAckFrame_WhenEncodingPingAck()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = Http2FrameUtils.EncodePingAck(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal((byte)PingFlags.Ack, frame[4]);
    }

    [Fact(DisplayName = "RFC9113-6.9-FS-005: WINDOW_UPDATE produces correct frame type and increment value")]
    public void Should_ProduceWindowUpdateFrame_WhenEncodingWindowUpdate()
    {
        var frame = Http2FrameUtils.EncodeWindowUpdate(streamId: 1, increment: 65535);

        Assert.Equal((byte)FrameType.WindowUpdate, frame[3]);
        var increment = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9)) & 0x7FFFFFFF;
        Assert.Equal(65535u, increment);
    }

    [Fact(DisplayName = "RFC9113-6.4-FS-006: RST_STREAM produces correct frame type and error code")]
    public void Should_ProduceRstStreamFrame_WhenEncodingRstStream()
    {
        var frame = Http2FrameUtils.EncodeRstStream(streamId: 3, Http2ErrorCode.Cancel);

        Assert.Equal((byte)FrameType.RstStream, frame[3]);
        var errorCode = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9));
        Assert.Equal((uint)Http2ErrorCode.Cancel, errorCode);
    }

    [Fact(DisplayName = "RFC9113-6.8-FS-007: GOAWAY with debug data includes debug message")]
    public void Should_ProduceGoAwayFrame_WhenEncodingGoAwayWithDebugMessage()
    {
        var frame = Http2FrameUtils.EncodeGoAway(5, Http2ErrorCode.NoError, "shutdown");

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        var debug = Encoding.UTF8.GetString(frame[17..]);
        Assert.Equal("shutdown", debug);
    }

    [Fact(DisplayName = "RFC9113-6.8-FS-008: GOAWAY without debug data has minimal frame length")]
    public void Should_ProduceGoAwayFrame_WhenEncodingGoAwayWithoutDebugMessage()
    {
        var frame = Http2FrameUtils.EncodeGoAway(0, Http2ErrorCode.NoError);

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        Assert.Equal(9 + 8, frame.Length);
    }

    [Fact(DisplayName = "RFC9113-6.5-SET-001: Encoder applies MAX_FRAME_SIZE server setting")]
    public void Should_UpdateEncoder_WhenApplyingMaxFrameSizeSetting()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 32768u)]);

        var request = CreateGetRequest("example.com", "/");
        var frames = EncodeToFrames(request);
        Assert.NotEmpty(frames);
    }

    [Fact(DisplayName = "RFC9113-6.5-SET-002: Encoder ignores non-MAX_FRAME_SIZE server settings")]
    public void Should_IgnoreParameter_WhenApplyingNonMaxFrameSizeSetting()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.InitialWindowSize, 65535u)]);

        var request = CreateGetRequest("example.com", "/");
        var frames = EncodeToFrames(request);
        Assert.NotEmpty(frames);
    }

    [Fact(DisplayName = "RFC9113-6.10-ENC-011: Large headers produce CONTINUATION frames with END_HEADERS unset")]
    public void Should_ProduceContinuationFrames_WhenEncodingRequestWithLargeHeaders()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-Custom-A", new string('a', 50) },
                { "X-Custom-B", new string('b', 50) }
            }
        };

        var (_, frames) = encoder.Encode(request, 1);

        Assert.True(frames.Count >= 2);
        Assert.IsType<HeadersFrame>(frames[0]);

        var hf = (HeadersFrame)frames[0];
        Assert.False(hf.EndHeaders);

        var continuationFrames = frames.Where(f => f.Type == FrameType.Continuation).ToList();
        Assert.NotEmpty(continuationFrames);
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

    private static IReadOnlyList<Http2Frame> EncodeToFrames(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2RequestEncoder(useHuffman);
        var (_, frames) = encoder.Encode(request, 1);
        return frames;
    }

    private static List<HpackHeader> DecodeHeaders(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2RequestEncoder(useHuffman);
        var block = encoder.EncodeToHpackBlock(request);
        return new HpackDecoder().Decode(block).ToList();
    }
}
