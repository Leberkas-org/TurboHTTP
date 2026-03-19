using System.Text;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2EncoderBaselineTests
{
    [Fact]
    public void BuildConnectionPreface_StartsWithMagic()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        Assert.True(preface.Length > magic.Length);
        Assert.Equal(magic, preface[..magic.Length]);
    }

    [Fact]
    public void BuildConnectionPreface_ContainsSettingsFrame()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        Assert.Equal((byte)FrameType.Settings, preface[27]);
    }

    [Fact]
    public void EncodeRequest_Get_ProducesHeadersFrame()
    {
        var request = CreateGetRequest("example.com", "/index.html");

        var frames = EncodeToFrames(request);

        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact]
    public void EncodeRequest_Get_HeadersFrame_HasEndStream()
    {
        var request = CreateGetRequest("example.com", "/");

        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact]
    public void EncodeRequest_Get_NoBannedHeaders()
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

    [Fact]
    public void EncodeRequest_Get_ContainsPseudoHeaders()
    {
        var request = CreateGetRequest("example.com", "/v1/data", 443, isHttps: true);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("GET", dict[":method"]);
        Assert.Equal("/v1/data", dict[":path"]);
        Assert.Equal("https", dict[":scheme"]);
        Assert.Equal("example.com", dict[":authority"]);
    }

    [Fact]
    public void EncodeRequest_Get_NonStandardPort_AuthorityIncludesPort()
    {
        var request = CreateGetRequest("example.com", "/", 8080);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("example.com:8080", dict[":authority"]);
    }

    [Fact]
    public void EncodeRequest_Post_HasDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"key\":\"value\"}");

        var frames = EncodeToFrames(request);

        Assert.Equal(2, frames.Count);
        Assert.IsType<HeadersFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    [Fact]
    public void EncodeRequest_Post_HeadersFrame_NoEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{}");

        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact]
    public void EncodeRequest_Post_DataFrame_HasEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"x\":1}");

        var frames = EncodeToFrames(request);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.True(df.EndStream);
    }

    [Fact]
    public void EncodeRequest_Post_ContentHeadersPresent()
    {
        const string json = "{\"name\":\"test\"}";
        var request = CreatePostRequest("example.com", "/users", json);

        var headers = DecodeHeaders(request);
        var dict = headers.ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("application/json; charset=utf-8", dict["content-type"]);
    }

    [Fact]
    public void EncodeRequest_Post_EmptyBody_ProducesEmptyDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "");

        var frames = EncodeToFrames(request);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(0, df.Data.Length);
        Assert.True(df.EndStream);
    }

    [Fact]
    public void EncodeSettingsAck_ProducesAckFrame()
    {
        var ack = Http2FrameUtils.EncodeSettingsAck();

        Assert.Equal((byte)FrameType.Settings, ack[3]);
        Assert.Equal((byte)Settings.Ack, ack[4]);
    }

    [Fact]
    public void EncodeSettings_ProducesSettingsFrame()
    {
        var frame = Http2FrameUtils.EncodeSettings(
        [
            (SettingsParameter.MaxFrameSize, 32768u),
        ]);

        Assert.Equal((byte)FrameType.Settings, frame[3]);
        Assert.Equal(0, frame[4]);
    }

    [Fact]
    public void EncodePing_ProducesPingFrame()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = Http2FrameUtils.EncodePing(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal(0, frame[4]);
    }

    [Fact]
    public void EncodePingAck_ProducesPingAckFrame()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = Http2FrameUtils.EncodePingAck(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal((byte)PingFlags.Ack, frame[4]);
    }

    [Fact]
    public void EncodeWindowUpdate_ProducesWindowUpdateFrame()
    {
        var frame = Http2FrameUtils.EncodeWindowUpdate(streamId: 1, increment: 65535);

        Assert.Equal((byte)FrameType.WindowUpdate, frame[3]);
        var increment = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9)) & 0x7FFFFFFF;
        Assert.Equal(65535u, increment);
    }

    [Fact]
    public void EncodeRstStream_ProducesRstStreamFrame()
    {
        var frame = Http2FrameUtils.EncodeRstStream(streamId: 3, Http2ErrorCode.Cancel);

        Assert.Equal((byte)FrameType.RstStream, frame[3]);
        var errorCode = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9));
        Assert.Equal((uint)Http2ErrorCode.Cancel, errorCode);
    }

    [Fact]
    public void EncodeGoAway_WithDebugMessage_ProducesGoAwayFrame()
    {
        var frame = Http2FrameUtils.EncodeGoAway(5, Http2ErrorCode.NoError, "shutdown");

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        var debug = Encoding.UTF8.GetString(frame[17..]);
        Assert.Equal("shutdown", debug);
    }

    [Fact]
    public void EncodeGoAway_WithoutDebugMessage_ProducesGoAwayFrame()
    {
        var frame = Http2FrameUtils.EncodeGoAway(0, Http2ErrorCode.NoError);

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        Assert.Equal(9 + 8, frame.Length);
    }

    [Fact]
    public void ApplyServerSettings_MaxFrameSize_UpdatesEncoder()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 32768u)]);

        var request = CreateGetRequest("example.com", "/");
        var frames = EncodeToFrames(request);
        Assert.NotEmpty(frames);
    }

    [Fact]
    public void ApplyServerSettings_OtherParameter_IsIgnored()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.InitialWindowSize, 65535u)]);

        var request = CreateGetRequest("example.com", "/");
        var frames = EncodeToFrames(request);
        Assert.NotEmpty(frames);
    }

    [Fact]
    public void EncodeRequest_LargeHeaders_ProducesContinuationFrames()
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
