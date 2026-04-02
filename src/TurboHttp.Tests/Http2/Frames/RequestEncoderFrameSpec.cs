using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.Frames;

/// <summary>
/// Tests HTTP request serialization to HTTP/2 frames per RFC 9113 §8.1.
/// Verifies frame types, flags, stream IDs, and HPACK-encoded pseudo-headers.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2RequestEncoder"/>.
/// RFC 9113 §8.1: GET requests produce a HEADERS frame with END_STREAM; POST requests with a body produce HEADERS + DATA.
/// </remarks>
public sealed class Http2RequestEncoderFrameSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_frame_with_end_stream_when_encoding_get_request()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_then_data_when_encoding_post_request()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void Http2RequestEncoder_should_contain_pseudo_headers_when_encoding_get_request_header_block()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?q=1");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/v1/data?q=1" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "api.example.com" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void Http2RequestEncoder_should_include_query_in_path_when_encoding_request_with_query()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?term=foo&page=2");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/search?term=foo&page=2" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2RequestEncoder_should_strip_connection_headers_when_encoding()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("x-custom", "value");

        var headers = new HpackDecoder().Decode(encoder.EncodeToHpackBlock(request));

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h.Name == "x-custom");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2RequestEncoder_should_use_continuation_frames_when_header_block_larger_than_max_frame_size()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void Http2RequestEncoder_should_have_same_stream_id_on_all_frames_when_encoding_post_request()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new ByteArrayContent([1, 2, 3, 4]),
        };

        var (streamId, frames) = encoder.Encode(request, 1);

        Assert.All(frames, f => Assert.Equal(streamId, f.StreamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_produce_headers_frame_with_end_headers()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, frames) = encoder.Encode(request, 1);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2RequestEncoder_should_encode_multiple_requests_with_increasing_stream_ids()
    {
        var encoder = new Http2RequestEncoder();
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");

        var (streamId1, _) = encoder.Encode(request1, 1);
        var (streamId2, _) = encoder.Encode(request2, 3);

        Assert.Equal(1, streamId1);
        Assert.Equal(3, streamId2);
    }
}
