using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using TurboHttp.Protocol;

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

        var data = Encode(request);

        Assert.True(data.Length > 9);
        Assert.Equal((byte)FrameType.Headers, data[3]);
    }

    [Fact]
    public void EncodeRequest_Get_HeadersFrame_HasEndStream()
    {
        var request = CreateGetRequest("example.com", "/");

        var data = Encode(request);

        var flags = (Headers)data[4];
        Assert.True(flags.HasFlag(Headers.EndStream));
        Assert.True(flags.HasFlag(Headers.EndHeaders));
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

        var data = Encode(request);

        var headerBlock = ExtractFirstHeaderBlock(data);
        var decoded = new HpackDecoder().Decode(headerBlock);
        var names = decoded.Select(h => h.Name).ToList();

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

        var data = Encode(request);

        var headerBlock = ExtractFirstHeaderBlock(data);
        var dict = new HpackDecoder().Decode(headerBlock)
            .ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("GET", dict[":method"]);
        Assert.Equal("/v1/data", dict[":path"]);
        Assert.Equal("https", dict[":scheme"]);
        Assert.Equal("example.com", dict[":authority"]);
    }

    [Fact]
    public void EncodeRequest_Get_NonStandardPort_AuthorityIncludesPort()
    {
        var request = CreateGetRequest("example.com", "/", 8080);

        var data = Encode(request);

        var dict = new HpackDecoder().Decode(ExtractFirstHeaderBlock(data))
            .ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("example.com:8080", dict[":authority"]);
    }

    [Fact]
    public void EncodeRequest_Post_HasDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"key\":\"value\"}");

        var data = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataFrameOffset = 9 + headersPayloadLen;

        Assert.True(data.Length > dataFrameOffset + 9);
        Assert.Equal((byte)FrameType.Data, data[dataFrameOffset + 3]);
    }

    [Fact]
    public void EncodeRequest_Post_HeadersFrame_NoEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{}");

        var data = Encode(request);

        var flags = (Headers)data[4];
        Assert.False(flags.HasFlag(Headers.EndStream));
        Assert.True(flags.HasFlag(Headers.EndHeaders));
    }

    [Fact]
    public void EncodeRequest_Post_DataFrame_HasEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"x\":1}");

        var data = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataFrameOffset = 9 + headersPayloadLen;
        var dataFlags = (DataFlags)data[dataFrameOffset + 4];

        Assert.True(dataFlags.HasFlag(DataFlags.EndStream));
    }

    [Fact]
    public void EncodeRequest_Post_ContentHeadersPresent()
    {
        const string json = "{\"name\":\"test\"}";
        var request = CreatePostRequest("example.com", "/users", json);

        var data = Encode(request);

        var dict = new HpackDecoder().Decode(ExtractFirstHeaderBlock(data))
            .ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("application/json; charset=utf-8", dict["content-type"]);
    }

    [Fact]
    public void EncodeRequest_Post_EmptyBody_ProducesEmptyDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "");

        var data = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataFrameOffset = 9 + headersPayloadLen;

        Assert.Equal((byte)FrameType.Data, data[dataFrameOffset + 3]);
        var dataPayloadLen = (data[dataFrameOffset] << 16) | (data[dataFrameOffset + 1] << 8) |
                             data[dataFrameOffset + 2];
        Assert.Equal(0, dataPayloadLen);
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
        var increment = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9)) & 0x7FFFFFFF;
        Assert.Equal(65535u, increment);
    }

    [Fact]
    public void EncodeRstStream_ProducesRstStreamFrame()
    {
        var frame = Http2FrameUtils.EncodeRstStream(streamId: 3, Http2ErrorCode.Cancel);

        Assert.Equal((byte)FrameType.RstStream, frame[3]);
        var errorCode = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9));
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
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var (_, written) = encoder.Encode(request, ref buffer);
        Assert.True(written > 0);
    }

    [Fact]
    public void ApplyServerSettings_OtherParameter_IsIgnored()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.InitialWindowSize, 65535u)]);

        var request = CreateGetRequest("example.com", "/");
        var (_, frames) = encoder.Encode(request, 1);
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

        // Serialize frames to check structure
        var totalSize = frames.Sum(f => f.SerializedSize);
        var buffer = new byte[totalSize];
        var offset = 0;

        foreach (var frame in frames)
        {
            var frameBytes = frame.Serialize();
            frameBytes.CopyTo(buffer, offset);
            offset += frameBytes.Length;
        }

        var data = (ReadOnlySpan<byte>)buffer;
        Assert.Equal((byte)FrameType.Headers, data[3]);
        var firstFlags = (Headers)data[4];
        Assert.False(firstFlags.HasFlag(Headers.EndHeaders));

        var firstPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var nextOffset = 9 + firstPayloadLen;
        Assert.Equal((byte)FrameType.Continuation, data[nextOffset + 3]);
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

    private static byte[] Encode(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2RequestEncoder(useHuffman);
        var (streamId, frames) = encoder.Encode(request, 1);

        // Serialize all frames to bytes
        var totalSize = frames.Sum(f => f.SerializedSize);
        var buffer = new byte[totalSize];
        var offset = 0;

        foreach (var frame in frames)
        {
            var frameBytes = frame.Serialize();
            frameBytes.CopyTo(buffer, offset);
            offset += frameBytes.Length;
        }

        return buffer;
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