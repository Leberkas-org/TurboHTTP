using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests HTTP/2 encoder baseline behaviors per RFC 9113 §3.
/// Verifies connection preface, frame production, and basic request encoding.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RequestEncoder"/>.
/// RFC 9113 §3: HTTP/2 connection preface consists of a client preface string and SETTINGS frame.
/// </remarks>
public sealed class Http2EncoderBaselineSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3")]
    public void Http2Encoder_should_encode_get_request_to_headers_frame()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var frames = encoder.Encode(request, 1);

        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3")]
    public void Http2Encoder_should_assign_stream_id_to_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 5);

        Assert.All(frames, f => Assert.Equal(5, f.StreamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2Encoder_should_include_pseudo_headers_in_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/resource");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h.Name == ":method");
        Assert.Contains(decoded, h => h.Name == ":path");
        Assert.Contains(decoded, h => h.Name == ":scheme");
        Assert.Contains(decoded, h => h.Name == ":authority");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_method_to_get_for_get_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":method", Value: "GET" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_method_to_post_for_post_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":method", Value: "POST" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_path_from_uri()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/resource");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":path", Value: "/api/resource" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_scheme_to_http()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":scheme", Value: "http" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_scheme_to_https()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":scheme", Value: "https" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Http2Encoder_should_set_authority_from_uri()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.example.com:8080/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: ":authority", Value: "api.example.com:8080" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2Encoder_should_encode_regular_headers()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Add("User-Agent", "TestClient/1.0");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h is { Name: "user-agent", Value: "TestClient/1.0" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Http2Encoder_should_produce_headers_frame_with_end_stream_for_get()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 1);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2Encoder_should_produce_headers_and_data_for_post()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("body"),
        };

        var frames = encoder.Encode(request, 1);

        Assert.True(frames.Count >= 2);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream);
    }
}
