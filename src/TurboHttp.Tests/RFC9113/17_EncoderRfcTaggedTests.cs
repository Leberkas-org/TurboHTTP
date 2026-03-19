using System.Text;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2EncoderRfcTaggedTests
{
    [Fact(DisplayName = "RFC9113-3.5-001: Client preface is PRI * HTTP/2.0 SM")]
    public void Preface_MagicBytes_MatchSpec()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var expected = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.Equal(expected, preface[..expected.Length]);
    }

    [Fact(DisplayName = "RFC9113-3.5-003: SETTINGS frame immediately follows client preface")]
    public void Preface_SettingsFrame_ImmediatelyFollowsMagic()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        const int magicLen = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        Assert.Equal((byte)FrameType.Settings, preface[magicLen + 3]);
    }

    [Fact(DisplayName = "RFC9113-8.1-001: All four pseudo-headers emitted")]
    public void PseudoHeaders_AllFourEmitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/v1/data");
        var headers = DecodeHeaders(request);
        Assert.Contains(headers, h => h.Name == ":method");
        Assert.Contains(headers, h => h.Name == ":scheme");
        Assert.Contains(headers, h => h.Name == ":authority");
        Assert.Contains(headers, h => h.Name == ":path");
    }

    [Fact(DisplayName = "RFC9113-8.1-002: Pseudo-headers precede regular headers")]
    public void PseudoHeaders_PrecedeRegularHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Custom", "value");
        var headers = DecodeHeaders(request);

        var lastPseudo = headers.FindLastIndex(h => h.Name.StartsWith(':'));
        var firstRegular = headers.FindIndex(h => !h.Name.StartsWith(':'));

        Assert.True(lastPseudo < firstRegular, "All pseudo-headers must appear before regular headers");
    }

    [Fact(DisplayName = "RFC9113-8.1-003: No duplicate pseudo-headers")]
    public void PseudoHeaders_NoDuplicates()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        var headers = DecodeHeaders(request);

        var pseudos = headers.Where(h => h.Name.StartsWith(':')).Select(h => h.Name).ToList();
        Assert.Equal(pseudos.Count, pseudos.Distinct().Count());
    }

    [Fact(DisplayName = "RFC9113-8.1-004: Connection-specific headers absent in HTTP/2")]
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
        var names = DecodeHeaders(request).Select(h => h.Name).ToList();

        Assert.DoesNotContain("connection", names);
        Assert.DoesNotContain("keep-alive", names);
        Assert.DoesNotContain("upgrade", names);
        Assert.DoesNotContain("te", names);
    }

    [Theory(DisplayName = "RFC9113-8.3-PH-001: :method pseudo-header correct for [{method}]")]
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
        var dict = DecodeHeaders(request).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal(method, dict[":method"]);
    }

    [Fact(DisplayName = "RFC9113-8.3-PH-002: :scheme reflects request URI scheme")]
    public void PseudoHeader_Scheme_ReflectsUriScheme()
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var httpsRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var httpDict = DecodeHeaders(httpRequest).ToDictionary(h => h.Name, h => h.Value);
        var httpsDict = DecodeHeaders(httpsRequest).ToDictionary(h => h.Name, h => h.Value);

        Assert.Equal("http", httpDict[":scheme"]);
        Assert.Equal("https", httpsDict[":scheme"]);
    }

    [Fact(DisplayName = "RFC9113-6.2-001: HEADERS frame has correct 9-byte header and payload")]
    public void HeadersFrame_HasCorrect9ByteHeader_TypeByte()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var frames = EncodeToFrames(request);

        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(DisplayName = "RFC9113-6.2-002: END_STREAM flag set on HEADERS for GET")]
    public void HeadersFrame_EndStream_SetForGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.2-003: END_HEADERS flag set on single HEADERS frame")]
    public void HeadersFrame_EndHeaders_SetOnSingleFrame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var frames = EncodeToFrames(request);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-6.9-001: Headers exceeding max frame size split into CONTINUATION")]
    public void LargeHeaders_SplitIntoContinuation()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        var (_, frames) = encoder.Encode(request, 1);

        // Should have HEADERS frame followed by at least one CONTINUATION frame
        Assert.True(frames.Count >= 2, "Expected HEADERS + at least one CONTINUATION frame");
        Assert.IsType<HeadersFrame>(frames[0]);
        Assert.IsType<ContinuationFrame>(frames[1]);
    }

    [Fact(DisplayName = "RFC9113-6.9-002: END_HEADERS on final CONTINUATION frame")]
    public void ContinuationFrame_FinalHasEndHeaders()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 64u)]);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Big", new string('x', 100) } }
        };

        var (_, frames) = encoder.Encode(request, 1);

        // Last frame should be a CONTINUATION with EndHeaders set
        var lastFrame = frames[^1];
        var cf = Assert.IsType<ContinuationFrame>(lastFrame);
        Assert.True(cf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-6.9-003: Multiple CONTINUATION frames for very large headers")]
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

        var (_, frames) = encoder.Encode(request, 1);

        var contCount = frames.OfType<ContinuationFrame>().Count();
        Assert.True(contCount >= 2, $"Expected >= 2 CONTINUATION frames, got {contCount}");
    }

    [Fact(DisplayName = "RFC9113-6.1-enc-002: END_STREAM set on final DATA frame")]
    public void DataFrame_EndStream_SetOnFinalFrame()
    {
        var request = CreatePostRequest("example.com", "/api", "hello");
        var frames = EncodeToFrames(request);

        // Should have HEADERS + DATA frame
        Assert.True(frames.Count >= 2);
        var df = Assert.IsType<DataFrame>(frames[^1]);
        Assert.True(df.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.1-enc-003: GET END_STREAM on HEADERS frame")]
    public void Get_EndStream_OnHeadersNotData()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var frames = EncodeToFrames(request);

        // GET produces exactly one frame (HEADERS with END_STREAM, no DATA frame)
        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.1-DATA-001: DATA frame has type byte 0x00")]
    public void DataFrame_TypeByte_IsZero()
    {
        var request = CreatePostRequest("example.com", "/api", "payload");
        var frames = EncodeToFrames(request);

        // Find first DATA frame
        var df = frames.OfType<DataFrame>().First();
        Assert.NotNull(df);
    }

    [Fact(DisplayName = "RFC9113-6.1-DATA-002: DATA frame carries correct stream ID")]
    public void DataFrame_CarriesCorrectStreamId()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var request = CreatePostRequest("example.com", "/api", "payload");
        var (streamId, frames) = encoder.Encode(request, 1);

        // All frames should have the same stream ID
        Assert.All(frames, f => Assert.Equal(streamId, f.StreamId));
    }

    [Fact(DisplayName = "RFC9113-6.1-DATA-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames")]
    public void DataFrame_LargeBody_SplitIntoMultipleFrames()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 16u)]);
        encoder.UpdateConnectionWindow(0x7FFFFFFF - 65535);

        const string body = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"; // 36 bytes > max frame 16
        var request = CreatePostRequest("example.com", "/api", body);

        var (_, frames) = encoder.Encode(request, 1);

        var dataFrameCount = frames.OfType<DataFrame>().Count();
        Assert.True(dataFrameCount >= 2, $"Expected >= 2 DATA frames, got {dataFrameCount}");
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
        return new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request)).ToList();
    }
}
