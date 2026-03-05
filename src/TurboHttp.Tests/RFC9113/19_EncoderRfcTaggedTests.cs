using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2EncoderRfcTaggedTests
{
    [Fact(DisplayName = "7540-3.5-001: Client preface is PRI * HTTP/2.0 SM")]
    public void Preface_MagicBytes_MatchSpec()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var expected = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.Equal(expected, preface[..expected.Length]);
    }

    [Fact(DisplayName = "7540-3.5-003: SETTINGS frame immediately follows client preface")]
    public void Preface_SettingsFrame_ImmediatelyFollowsMagic()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        const int magicLen = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        Assert.Equal((byte)FrameType.Settings, preface[magicLen + 3]);
    }

    [Fact(DisplayName = "7540-8.1-001: All four pseudo-headers emitted")]
    public void PseudoHeaders_AllFourEmitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/v1/data");
        var (_, data) = Encode(request);
        var headers = DecodeHeaderList(data);
        Assert.Contains(headers, h => h.Name == ":method");
        Assert.Contains(headers, h => h.Name == ":scheme");
        Assert.Contains(headers, h => h.Name == ":authority");
        Assert.Contains(headers, h => h.Name == ":path");
    }

    [Fact(DisplayName = "7540-8.1-002: Pseudo-headers precede regular headers")]
    public void PseudoHeaders_PrecedeRegularHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Custom", "value");
        var (_, data) = Encode(request);
        var headers = DecodeHeaderList(data);

        var lastPseudo = headers.FindLastIndex(h => h.Name.StartsWith(':'));
        var firstRegular = headers.FindIndex(h => !h.Name.StartsWith(':'));

        Assert.True(lastPseudo < firstRegular, "All pseudo-headers must appear before regular headers");
    }

    [Fact(DisplayName = "7540-8.1-003: No duplicate pseudo-headers")]
    public void PseudoHeaders_NoDuplicates()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        var (_, data) = Encode(request);
        var headers = DecodeHeaderList(data);

        var pseudos = headers.Where(h => h.Name.StartsWith(':')).Select(h => h.Name).ToList();
        Assert.Equal(pseudos.Count, pseudos.Distinct().Count());
    }

    [Fact(DisplayName = "7540-8.1-004: Connection-specific headers absent in HTTP/2")]
    public void Http2_ConnectionSpecificHeaders_Absent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "Connection", "keep-alive" },
                { "Keep-Alive", "timeout=5" },
                { "Upgrade", "websocket" },
                { "TE", "trailers" },
            }
        };
        var (_, data) = Encode(request);
        var names = DecodeHeaderList(data).Select(h => h.Name).ToList();

        Assert.DoesNotContain("connection", names);
        Assert.DoesNotContain("keep-alive", names);
        Assert.DoesNotContain("upgrade", names);
        Assert.DoesNotContain("te", names);
    }

    [Theory(DisplayName = "enc5-ph-001: :method pseudo-header correct for [{method}]")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void PseudoHeader_Method_CorrectForAllMethods(string method)
    {
        var uri = "https://example.com/test";
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        var (_, data) = Encode(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal(method, dict[":method"]);
    }

    [Fact(DisplayName = "enc5-ph-002: :scheme reflects request URI scheme")]
    public void PseudoHeader_Scheme_ReflectsUriScheme()
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var httpsRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var (_, httpData) = Encode(httpRequest);
        var (_, httpsData) = Encode(httpsRequest);

        var httpDict = DecodeHeaderList(httpData).ToDictionary(h => h.Name, h => h.Value);
        var httpsDict = DecodeHeaderList(httpsData).ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("http", httpDict[":scheme"]);
        Assert.Equal("https", httpsDict[":scheme"]);
    }

    [Fact(DisplayName = "7540-6.2-001: HEADERS frame has correct 9-byte header and payload")]
    public void HeadersFrame_HasCorrect9ByteHeader_TypeByte()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = Encode(request);

        Assert.True(data.Length >= 9);
        Assert.Equal((byte)FrameType.Headers, data[3]); // type = 0x01
    }

    [Fact(DisplayName = "7540-6.2-002: END_STREAM flag set on HEADERS for GET")]
    public void HeadersFrame_EndStream_SetForGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = Encode(request);

        var flags = (Headers)data[4];
        Assert.True(flags.HasFlag(Headers.EndStream));
    }

    [Fact(DisplayName = "7540-6.2-003: END_HEADERS flag set on single HEADERS frame")]
    public void HeadersFrame_EndHeaders_SetOnSingleFrame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = Encode(request);

        var flags = (Headers)data[4];
        Assert.True(flags.HasFlag(Headers.EndHeaders));
    }

    [Fact(DisplayName = "7540-6.9-001: Headers exceeding max frame size split into CONTINUATION")]
    public void LargeHeaders_SplitIntoContinuation()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var firstPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var nextOffset = 9 + firstPayloadLen;
        Assert.Equal((byte)FrameType.Continuation, data[nextOffset + 3]);
    }

    [Fact(DisplayName = "7540-6.9-002: END_HEADERS on final CONTINUATION frame")]
    public void ContinuationFrame_FinalHasEndHeaders()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        // Walk all frames and record flags of last CONTINUATION frame
        var offset = 0;
        byte lastContFlags = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Continuation)
            {
                lastContFlags = data[offset + 4];
            }

            offset += 9 + len;
        }

        Assert.NotEqual(0, lastContFlags & (byte)ContinuationFlags.EndHeaders);
    }

    [Fact(DisplayName = "7540-6.9-003: Multiple CONTINUATION frames for very large headers")]
    public void VeryLargeHeaders_MultipleContinuationFrames()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 32u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-A", new string('a', 60) },
                { "X-B", new string('b', 60) },
                { "X-C", new string('c', 60) },
            }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(16384);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var contCount = 0;
        var offset = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Continuation)
            {
                contCount++;
            }

            offset += 9 + len;
        }

        Assert.True(contCount >= 2, $"Expected >= 2 CONTINUATION frames, got {contCount}");
    }

    [Fact(DisplayName = "7540-6.1-enc-002: END_STREAM set on final DATA frame")]
    public void DataFrame_EndStream_SetOnFinalFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "hello");
        var (_, data) = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataOffset = 9 + headersPayloadLen;
        var dataFlags = (DataFlags)data[dataOffset + 4];

        Assert.True(dataFlags.HasFlag(DataFlags.EndStream));
    }

    [Fact(DisplayName = "7540-6.1-enc-003: GET END_STREAM on HEADERS frame")]
    public void Get_EndStream_OnHeadersNotData()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = Encode(request);

        // GET produces exactly one frame (HEADERS with END_STREAM, no DATA frame)
        var payloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        Assert.Equal(data.Length, 9 + payloadLen);

        var flags = (Headers)data[4];
        Assert.True(flags.HasFlag(Headers.EndStream));
    }

    [Fact(DisplayName = "enc5-data-001: DATA frame has type byte 0x00")]
    public void DataFrame_TypeByte_IsZero()
    {
        var request = CreatePostRequest("example.com", "/api", "payload");
        var (_, data) = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataOffset = 9 + headersPayloadLen;

        Assert.Equal((byte)FrameType.Data, data[dataOffset + 3]); // 0x00
    }

    [Fact(DisplayName = "enc5-data-002: DATA frame carries correct stream ID")]
    public void DataFrame_CarriesCorrectStreamId()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var request = CreatePostRequest("example.com", "/api", "payload");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buf = owner.Memory;
        var (streamId, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataOffset = 9 + headersPayloadLen;
        var dataStreamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(data[(dataOffset + 5)..]) & 0x7FFFFFFF);

        Assert.Equal(streamId, dataStreamId);
    }

    [Fact(DisplayName = "enc5-data-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames")]
    public void DataFrame_LargeBody_SplitIntoMultipleFrames()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 16u)]);
        // Expand window so the full body fits
        encoder.UpdateConnectionWindow(0x7FFFFFFF - 65535);

        const string body = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"; // 36 bytes > max frame 16
        var request = CreatePostRequest("example.com", "/api", body);

        using var owner = MemoryPool<byte>.Shared.Rent(65536);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = owner.Memory.Span[..n];

        // Skip HEADERS frame, count DATA frames
        var headersLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var offset = 9 + headersLen;
        var dataFrameCount = 0;
        while (offset < data.Length)
        {
            var len = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            if (data[offset + 3] == (byte)FrameType.Data)
            {
                dataFrameCount++;
            }

            offset += 9 + len;
        }

        Assert.True(dataFrameCount >= 2, $"Expected >= 2 DATA frames, got {dataFrameCount}");
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
        var original = owner.Memory;
        var buffer = original;
        var (streamId, written) = encoder.Encode(request, ref buffer);
        return (streamId, original.Span[..written].ToArray());
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
