using System.Text;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11DecoderEdgeCasesSpec
{
    private readonly Decoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_return_true_when_eof_with_complete_headers_and_no_body_framing()
    {
        // RFC 9112 §9.8: Response with no Content-Length/Transfer-Encoding header
        // has no body; TryDecode completes immediately with empty body
        const string raw = "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecode(bytes.AsMemory(), out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_return_false_when_eof_with_no_remainder()
    {
        var decoded = _decoder.TryDecodeEof(out var response);

        Assert.False(decoded);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_return_false_when_eof_with_incomplete_headers()
    {
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 10";
        var bytes = Encoding.ASCII.GetBytes(raw);

        _decoder.TryDecode(bytes, out _);

        var decoded = _decoder.TryDecodeEof(out _);

        Assert.False(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_return_false_when_eof_with_chunked_encoding_incomplete()
    {
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nHello\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        _decoder.TryDecode(bytes, out _);

        var decoded = _decoder.TryDecodeEof(out _);

        Assert.False(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_return_false_when_eof_with_content_length_not_satisfied()
    {
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nShort";
        var bytes = Encoding.ASCII.GetBytes(raw);

        _decoder.TryDecode(bytes, out _);

        var decoded = _decoder.TryDecodeEof(out _);

        Assert.False(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_skip_1xx_informational_responses()
    {
        const string raw = "HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecode(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112")]
    public void Http11Decoder_should_handle_multiple_1xx_responses_before_final()
    {
        const string raw = "HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 103 Early Hints\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecode(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Http11Decoder_should_handle_remainder_flushing()
    {
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHelloExtra";
        var bytes = Encoding.ASCII.GetBytes(raw);

        _decoder.TryDecode(bytes, out _);
        var remainder = _decoder.FlushRemainder();

        Assert.Equal("Extra"u8.ToArray(), remainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Http11Decoder_should_return_empty_remainder_when_nothing_buffered()
    {
        var remainder = _decoder.FlushRemainder();

        Assert.Empty(remainder);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reset_state_for_reuse()
    {
        const string raw1 = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nAB";
        const string raw2 = "HTTP/1.1 201 Created\r\nContent-Length: 2\r\n\r\nXY";

        _decoder.TryDecode(Encoding.ASCII.GetBytes(raw1), out var responses1);
        _decoder.Reset();
        _decoder.TryDecode(Encoding.ASCII.GetBytes(raw2), out var responses2);

        Assert.Single(responses1);
        Assert.Equal(200, (int)responses1[0].StatusCode);
        Assert.Single(responses2);
        Assert.Equal(201, (int)responses2[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_throw_when_used_after_disposed()
    {
        _decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _decoder.TryDecode("data"u8.ToArray().AsMemory(), out _));
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_throw_when_eof_after_disposed()
    {
        _decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _decoder.TryDecodeEof(out _));
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_be_idempotent_on_dispose()
    {
        _decoder.Dispose();
        _decoder.Dispose();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Http11Decoder_should_handle_head_request_with_content_length()
    {
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // HEAD response body is empty even if Content-Length header exists
        // The ContentLength property reflects what the server indicated
        Assert.Equal(100, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Http11Decoder_should_ignore_body_in_head_response()
    {
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // HEAD response parses Content-Length from headers
        Assert.Equal(5, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Decoder_should_handle_connect_2xx_with_content_length()
    {
        const string raw = "HTTP/1.1 200 Connection Established\r\nContent-Length: 100\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecodeConnect(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public void Http11Decoder_should_handle_connect_3xx_with_body()
    {
        const string raw = "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com\r\nContent-Length: 5\r\n\r\nProxy";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var decoded = _decoder.TryDecodeConnect(bytes, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(301, (int)responses[0].StatusCode);
    }
}
