using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2RequestEncoderFrameTests
{
    // ── Frame structure ───────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-8.1-001: GET request produces HEADERS frame with END_STREAM and END_HEADERS")]
    public void Encode_GetRequest_ProducesHeadersFrameWithEndStream()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var (streamId, frames) = encoder.Encode(request, 1);

        Assert.Equal(1, streamId);
        Assert.Single(frames);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, hf.StreamId);
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "9113-8.1-002: POST request produces HEADERS frame (no END_STREAM) followed by DATA")]
    public void Encode_PostRequest_ProducesHeadersThenData()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("hello world"),
        };

        var (streamId, frames) = encoder.Encode(request, 1);

        Assert.Equal(1, streamId);
        Assert.Equal(2, frames.Count);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndStream);
        Assert.True(hf.EndHeaders);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(1, df.StreamId);
        Assert.True(df.EndStream);
        Assert.NotEmpty(df.Data.ToArray());
    }

    // ── Pseudo-headers ────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-8.3.1-001: Encoded header block contains required HTTP/2 pseudo-headers")]
    public void Encode_GetRequest_HeaderBlockContainsPseudoHeaders()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?q=1");

        var (_, frames) = encoder.Encode(request, 1);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);

        // Decode the header block to verify pseudo-headers
        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(hf.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/v1/data?q=1" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "api.example.com" });
    }

    [Fact(DisplayName = "9113-8.3.1-002: Path includes query string in :path pseudo-header")]
    public void Encode_RequestWithQuery_PathIncludesQuery()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?term=foo&page=2");

        var (_, frames) = encoder.Encode(request, 1);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(hf.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/search?term=foo&page=2" });
    }

    // ── Forbidden headers ─────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-8.2.2-001: Connection-specific headers are stripped from encoded output")]
    public void Encode_ConnectionHeaders_AreStripped()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("x-custom", "value");

        var (_, frames) = encoder.Encode(request, 1);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(hf.HeaderBlockFragment.Span);

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h.Name == "x-custom");
    }

    // ── Large header block (CONTINUATION) ─────────────────────────────────────

    [Fact(DisplayName = "9113-6.10-002: Header block larger than max frame size uses CONTINUATION frames")]
    public void Encode_LargeHeaderBlock_UsesContinuationFrames()
    {
        // Use a tiny maxFrameSize to force continuation
        var encoder = new Http2RequestEncoder(useHuffman: false, maxFrameSize: 30);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        request.Headers.TryAddWithoutValidation("x-long-header", new string('a', 100));

        var (streamId, frames) = encoder.Encode(request, 1);

        Assert.True(frames.Count >= 2, "Expected at least HEADERS + CONTINUATION");

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndHeaders, "First HEADERS frame should not have END_HEADERS");

        // Last frame must be CONTINUATION with END_HEADERS=true or a HEADERS with END_HEADERS=true
        var lastFrame = frames[^1];

        if (lastFrame is ContinuationFrame cf)
        {
            Assert.Equal(streamId, cf.StreamId);
            Assert.True(cf.EndHeaders, "Last CONTINUATION frame must have END_HEADERS");
        }
        else
        {
            Assert.IsType<HeadersFrame>(lastFrame);
        }
    }

    // ── Stream ID ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-5.1.1-001: All frames for a request share the same stream ID")]
    public void Encode_PostRequest_AllFramesHaveSameStreamId()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent([1, 2, 3, 4]),
        };

        var (streamId, frames) = encoder.Encode(request, 1);

        Assert.All(frames, f => Assert.Equal(streamId, f.StreamId));
    }
}