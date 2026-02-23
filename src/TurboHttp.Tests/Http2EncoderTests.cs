using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http2EncoderTests
{
    [Fact]
    public void BuildConnectionPreface_StartsWithMagic()
    {
        var preface = Http2Encoder.BuildConnectionPreface();
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        Assert.True(preface.Length > magic.Length);
        Assert.Equal(magic, preface[..magic.Length]);
    }

    [Fact]
    public void BuildConnectionPreface_ContainsSettingsFrame()
    {
        var preface = Http2Encoder.BuildConnectionPreface();
        Assert.Equal((byte)FrameType.Settings, preface[27]);
    }

    [Fact]
    public void EncodeRequest_IncrementsStreamId()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var req = CreateGetRequest("example.com", "/");

        using var owner = MemoryPool<byte>.Shared.Rent(4096);

        var buf1 = owner.Memory;
        var (id1, _) = encoder.Encode(req, ref buf1);

        var buf2 = owner.Memory;
        var (id2, _) = encoder.Encode(req, ref buf2);

        var buf3 = owner.Memory;
        var (id3, _) = encoder.Encode(req, ref buf3);

        Assert.Equal(1, id1);
        Assert.Equal(3, id2);
        Assert.Equal(5, id3);
    }

    [Fact]
    public void EncodeRequest_Get_ProducesHeadersFrame()
    {
        var request = CreateGetRequest("example.com", "/index.html");

        var (_, data) = Encode(request);

        Assert.True(data.Length > 9);
        Assert.Equal((byte)FrameType.Headers, data[3]);
    }

    [Fact]
    public void EncodeRequest_Get_HeadersFrame_HasEndStream()
    {
        var request = CreateGetRequest("example.com", "/");

        var (_, data) = Encode(request);

        var flags = (HeadersFlags)data[4];
        Assert.True(flags.HasFlag(HeadersFlags.EndStream));
        Assert.True(flags.HasFlag(HeadersFlags.EndHeaders));
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

        var (_, data) = Encode(request);

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

        var (_, data) = Encode(request);

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

        var (_, data) = Encode(request);

        var dict = new HpackDecoder().Decode(ExtractFirstHeaderBlock(data))
            .ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("example.com:8080", dict[":authority"]);
    }

    [Fact]
    public void EncodeRequest_Post_HasDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"key\":\"value\"}");

        var (_, data) = Encode(request);

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataFrameOffset = 9 + headersPayloadLen;

        Assert.True(data.Length > dataFrameOffset + 9);
        Assert.Equal((byte)FrameType.Data, data[dataFrameOffset + 3]);
    }

    [Fact]
    public void EncodeRequest_Post_HeadersFrame_NoEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{}");

        var (_, data) = Encode(request);

        var flags = (HeadersFlags)data[4];
        Assert.False(flags.HasFlag(HeadersFlags.EndStream));
        Assert.True(flags.HasFlag(HeadersFlags.EndHeaders));
    }

    [Fact]
    public void EncodeRequest_Post_DataFrame_HasEndStream()
    {
        var request = CreatePostRequest("example.com", "/api", "{\"x\":1}");

        var (_, data) = Encode(request);

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

        var (_, data) = Encode(request);

        var dict = new HpackDecoder().Decode(ExtractFirstHeaderBlock(data))
            .ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("application/json; charset=utf-8", dict["content-type"]);
    }

    [Fact]
    public void EncodeRequest_Post_EmptyBody_ProducesEmptyDataFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "");

        var (_, data) = Encode(request);

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
        var ack = Http2Encoder.EncodeSettingsAck();

        Assert.Equal((byte)FrameType.Settings, ack[3]);
        Assert.Equal((byte)SettingsFlags.Ack, ack[4]);
    }

    [Fact]
    public void EncodeSettings_ProducesSettingsFrame()
    {
        var frame = Http2Encoder.EncodeSettings(
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
        var frame = Http2Encoder.EncodePing(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal(0, frame[4]);
    }

    [Fact]
    public void EncodePingAck_ProducesPingAckFrame()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = Http2Encoder.EncodePingAck(data);

        Assert.Equal((byte)FrameType.Ping, frame[3]);
        Assert.Equal((byte)PingFlags.Ack, frame[4]);
    }

    [Fact]
    public void EncodeWindowUpdate_ProducesWindowUpdateFrame()
    {
        var frame = Http2Encoder.EncodeWindowUpdate(streamId: 1, increment: 65535);

        Assert.Equal((byte)FrameType.WindowUpdate, frame[3]);
        var increment = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9)) & 0x7FFFFFFF;
        Assert.Equal(65535u, increment);
    }

    [Fact]
    public void EncodeRstStream_ProducesRstStreamFrame()
    {
        var frame = Http2Encoder.EncodeRstStream(streamId: 3, Http2ErrorCode.Cancel);

        Assert.Equal((byte)FrameType.RstStream, frame[3]);
        var errorCode = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(9));
        Assert.Equal((uint)Http2ErrorCode.Cancel, errorCode);
    }

    [Fact]
    public void EncodeGoAway_WithDebugMessage_ProducesGoAwayFrame()
    {
        var frame = Http2Encoder.EncodeGoAway(5, Http2ErrorCode.NoError, "shutdown");

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        var debug = Encoding.UTF8.GetString(frame[17..]);
        Assert.Equal("shutdown", debug);
    }

    [Fact]
    public void EncodeGoAway_WithoutDebugMessage_ProducesGoAwayFrame()
    {
        var frame = Http2Encoder.EncodeGoAway(0, Http2ErrorCode.NoError);

        Assert.Equal((byte)FrameType.GoAway, frame[3]);
        Assert.Equal(9 + 8, frame.Length);
    }

    [Fact]
    public void ApplyServerSettings_MaxFrameSize_UpdatesEncoder()
    {
        var encoder = new Http2Encoder(useHuffman: false);
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
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.InitialWindowSize, 65535u)]);

        var request = CreateGetRequest("example.com", "/");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var (_, written) = encoder.Encode(request, ref buffer);
        Assert.True(written > 0);
    }

    [Fact]
    public void EncodeRequest_LargeHeaders_ProducesContinuationFrames()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-Custom-A", new string('a', 50) },
                { "X-Custom-B", new string('b', 50) }
            }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var buffer = owner.Memory;
        var (_, bytesWritten) = encoder.Encode(request, ref buffer);
        var data = buffer.Span[..bytesWritten];

        Assert.Equal((byte)FrameType.Headers, data[3]);
        var firstFlags = (HeadersFlags)data[4];
        Assert.False(firstFlags.HasFlag(HeadersFlags.EndHeaders));

        var firstPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var nextOffset = 9 + firstPayloadLen;
        Assert.Equal((byte)FrameType.Continuation, data[nextOffset + 3]);
    }

    // =========================================================================
    // Phase 5 RFC-tagged tests — HTTP/2 Client Encoder (RFC 7540)
    // =========================================================================

    // --- Connection Preface (RFC 7540 §3.5) ----------------------------------

    [Fact(DisplayName = "7540-3.5-001: Client preface is PRI * HTTP/2.0 SM")]
    public void Preface_MagicBytes_MatchSpec()
    {
        var preface = Http2Encoder.BuildConnectionPreface();
        var expected = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.Equal(expected, preface[..expected.Length]);
    }

    [Fact(DisplayName = "7540-3.5-003: SETTINGS frame immediately follows client preface")]
    public void Preface_SettingsFrame_ImmediatelyFollowsMagic()
    {
        var preface = Http2Encoder.BuildConnectionPreface();
        const int magicLen = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        Assert.Equal((byte)FrameType.Settings, preface[magicLen + 3]);
    }

    // --- Pseudo-Headers (RFC 7540 §8.1.2) ------------------------------------

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

    // --- SETTINGS Frame (RFC 7540 §6.5) --------------------------------------

    [Theory(DisplayName = "enc5-set-001: SETTINGS parameter {param} encoded correctly")]
    [InlineData(SettingsParameter.HeaderTableSize, 4096u)]
    [InlineData(SettingsParameter.EnablePush, 0u)]
    [InlineData(SettingsParameter.MaxConcurrentStreams, 100u)]
    [InlineData(SettingsParameter.InitialWindowSize, 65535u)]
    [InlineData(SettingsParameter.MaxFrameSize, 16384u)]
    [InlineData(SettingsParameter.MaxHeaderListSize, 8192u)]
    public void Settings_Parameter_EncodedCorrectly(SettingsParameter param, uint value)
    {
        var frame = Http2Encoder.EncodeSettings([(param, value)]);
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
        var ack = Http2Encoder.EncodeSettingsAck();
        Assert.Equal((byte)FrameType.Settings, ack[3]);         // type = 0x04
        Assert.Equal((byte)SettingsFlags.Ack, ack[4]);          // flags = 0x01
        var streamId = BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(5)) & 0x7FFFFFFFu;
        Assert.Equal(0u, streamId);                             // stream = 0
    }

    // --- Stream IDs (RFC 7540 §5.1) ------------------------------------------

    [Fact(DisplayName = "7540-5.1-001: First request uses stream ID 1")]
    public void StreamId_FirstRequest_IsOne()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buf = owner.Memory;
        var (id, _) = encoder.Encode(req, ref buf);
        Assert.Equal(1, id);
    }

    [Fact(DisplayName = "7540-5.1-002: Stream IDs increment (1,3,5,...)")]
    public void StreamId_Increments_ByTwo()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        using var owner = MemoryPool<byte>.Shared.Rent(4096);

        var b1 = owner.Memory;
        var (id1, _) = encoder.Encode(req, ref b1);

        var b2 = owner.Memory;
        var (id2, _) = encoder.Encode(req, ref b2);

        var b3 = owner.Memory;
        var (id3, _) = encoder.Encode(req, ref b3);

        Assert.Equal(1, id1);
        Assert.Equal(3, id2);
        Assert.Equal(5, id3);
    }

    [Fact(DisplayName = "enc5-sid-001: Client never produces even stream IDs")]
    public void StreamId_NeverEven()
    {
        var encoder = new Http2Encoder(useHuffman: false);
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
        var encoder = new Http2Encoder(useHuffman: false);

        // Set _nextStreamId to the last valid odd value (2^31 - 1 = 0x7FFFFFFF)
        var field = typeof(Http2Encoder).GetField("_nextStreamId",
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

    // --- HEADERS Frame (RFC 7540 §6.2) ----------------------------------------

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

        var flags = (HeadersFlags)data[4];
        Assert.True(flags.HasFlag(HeadersFlags.EndStream));
    }

    [Fact(DisplayName = "7540-6.2-003: END_HEADERS flag set on single HEADERS frame")]
    public void HeadersFrame_EndHeaders_SetOnSingleFrame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = Encode(request);

        var flags = (HeadersFlags)data[4];
        Assert.True(flags.HasFlag(HeadersFlags.EndHeaders));
    }

    // --- CONTINUATION Frames (RFC 7540 §6.9) ----------------------------------

    [Fact(DisplayName = "7540-6.9-001: Headers exceeding max frame size split into CONTINUATION")]
    public void LargeHeaders_SplitIntoContinuation()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = buf.Span[..n];

        var firstPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var nextOffset = 9 + firstPayloadLen;
        Assert.Equal((byte)FrameType.Continuation, data[nextOffset + 3]);
    }

    [Fact(DisplayName = "7540-6.9-002: END_HEADERS on final CONTINUATION frame")]
    public void ContinuationFrame_FinalHasEndHeaders()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = buf.Span[..n];

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
        var encoder = new Http2Encoder(useHuffman: false);
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
        var data = buf.Span[..n];

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

    // --- DATA Frames (RFC 7540 §6.1) ------------------------------------------

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

        var flags = (HeadersFlags)data[4];
        Assert.True(flags.HasFlag(HeadersFlags.EndStream));
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
        var encoder = new Http2Encoder(useHuffman: false);
        var request = CreatePostRequest("example.com", "/api", "payload");
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buf = owner.Memory;
        var (streamId, n) = encoder.Encode(request, ref buf);
        var data = buf.Span[..n];

        var headersPayloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var dataOffset = 9 + headersPayloadLen;
        var dataStreamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(data[(dataOffset + 5)..]) & 0x7FFFFFFF);

        Assert.Equal(streamId, dataStreamId);
    }

    [Fact(DisplayName = "enc5-data-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames")]
    public void DataFrame_LargeBody_SplitIntoMultipleFrames()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 16u)]);
        // Expand window so the full body fits
        encoder.UpdateConnectionWindow(0x7FFFFFFF - 65535);

        const string body = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"; // 36 bytes > max frame 16
        var request = CreatePostRequest("example.com", "/api", body);

        using var owner = MemoryPool<byte>.Shared.Rent(65536);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = buf.Span[..n];

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

    // --- Flow Control — Encoder Side (RFC 7540 §5.2) -------------------------

    [Fact(DisplayName = "7540-5.2-enc-001: Encoder does not exceed initial 65535-byte window")]
    public void FlowControl_InitialWindow_LimitsToDefault()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var body = new string('X', 65535); // exactly fills the default window
        var request = CreatePostRequest("example.com", "/api", body);

        using var owner = MemoryPool<byte>.Shared.Rent(1 << 20);
        var buf = owner.Memory;
        var (_, n) = encoder.Encode(request, ref buf);
        var data = buf.Span[..n];

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
        var encoder = new Http2Encoder(useHuffman: false);

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
        var data = buf.Span[..n];

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
        var encoder = new Http2Encoder(useHuffman: false);

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
        var data = buf.Span[..n];

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
        var encoder = new Http2Encoder(useHuffman: false);

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
        var data = buf.Span[..n];

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
        var encoder = new Http2Encoder(useHuffman: false);

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
        var data = buf.Span[..n];

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

    // =========================================================================
    // End Phase 5 tests
    // =========================================================================

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
        var encoder = new Http2Encoder(useHuffman);
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var (streamId, written) = encoder.Encode(request, ref buffer);
        return (streamId, buffer.Span[..written].ToArray());
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
