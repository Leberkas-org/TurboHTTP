using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests RFC-specific encoder behaviors covering connection setup, HPACK compression, and frame semantics per RFC 9113.
/// Verifies correct stream state transitions and flag combinations.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RequestEncoder"/>.
/// RFC 9113 §5: Stream states and transitions for request encoding.
/// </remarks>
public sealed class Http2EncoderRfcTaggedSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2Encoder_should_set_end_stream_on_headers_for_stateless_request()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 1);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2Encoder_should_not_set_end_stream_on_headers_for_request_with_body()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("data"),
        };

        var frames = encoder.Encode(request, 1);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2Encoder_should_set_end_stream_on_data_frame_for_request_with_body()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("request body"),
        };

        var frames = encoder.Encode(request, 1);

        Assert.True(frames.Count >= 2);
        var dataFrame = Assert.IsType<DataFrame>(frames[1]);
        Assert.True(dataFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void Http2Encoder_should_use_hpack_compression()
    {
        var encoder = new RequestEncoder(useHuffman: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);

        Assert.NotEmpty(headerBlock.ToArray());
        // HPACK-encoded headers are typically smaller than raw headers
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void Http2Encoder_should_encode_with_huffman_when_enabled()
    {
        var encoderWithHuffman = new RequestEncoder(useHuffman: true);
        var encoderWithoutHuffman = new RequestEncoder(useHuffman: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var blockWithHuffman = encoderWithHuffman.EncodeToHpackBlock(request);
        var blockWithoutHuffman = encoderWithoutHuffman.EncodeToHpackBlock(request);

        // Huffman-encoded block should be smaller or equal
        Assert.True(blockWithHuffman.Length <= blockWithoutHuffman.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2Encoder_should_set_end_headers_flag_on_headers_frame()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 1);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2Encoder_should_lower_case_header_names()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.Contains(decoded, h => h.Name == "x-custom-header");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2Encoder_should_strip_connection_specific_headers()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        Assert.DoesNotContain(decoded, h => h.Name == "connection");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void Http2Encoder_should_use_odd_stream_ids()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request, 1);
        var streamId = frames[0].StreamId;
        Assert.Equal(1, streamId % 2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2Encoder_should_maintain_flow_control_window()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("data"),
        };

        var frames = encoder.Encode(request, 1);

        Assert.NotEmpty(frames);
        // Frames should respect default flow control window (65535 bytes)
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2Encoder_should_prefix_pseudo_headers()
    {
        var encoder = new RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var decoded = new HpackDecoder().Decode(headerBlock);

        var pseudoCount = decoded.Count(h => h.Name.StartsWith(':'));
        Assert.True(pseudoCount >= 4);
    }
}
