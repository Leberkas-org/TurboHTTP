using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Decoding;

/// <summary>
/// Tests HTTP/1.1 decoder header size limits (security: DoS protection).
/// Verifies that oversized individual headers, total header blocks, and header counts are rejected.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §5: Header field limits prevent memory exhaustion from oversized or excessive headers.
/// </remarks>
public sealed class Http11DecoderHeaderLimitsSpec
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.ASCII.GetBytes(s);

    private static string BuildRawResponse(string statusLine, string headers, string body = "")
        => $"{statusLine}\r\n{headers}\r\n\r\n{body}";

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_use_default_max_header_size_when_no_config_provided()
    {
        var decoder = new Http11Decoder();
        var value = new string('A', 16 * 1024 - 20); // name + ": " + value < 16KB
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X-Big: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_use_default_max_total_header_size_when_no_config_provided()
    {
        var decoder = new Http11Decoder();
        var sb = new StringBuilder();
        var headerValue = new string('B', 1000);
        for (var i = 0; i < 60; i++)
        {
            sb.Append($"X-Hdr-{i:D3}: {headerValue}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_use_default_max_header_count_when_no_config_provided()
    {
        var decoder = new Http11Decoder();
        // 99 extra + Content-Length = 100 total, at the limit
        var raw = BuildResponseWithNHeaders(99);

        var result = decoder.TryDecode(raw, out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_header_too_large_when_single_header_exceeds_limit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 100);
        var bigValue = new string('X', 200);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X-Big: {bigValue}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
        Assert.Contains("X-Big", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_when_single_header_exactly_at_limit()
    {
        const int limit = 50;
        var value = new string('V', limit - 1 - 2); // "X" + ": " + value = 50
        var decoder = new Http11Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_header_too_large_when_one_byte_over_limit()
    {
        const int limit = 50;
        var value = new string('V', limit - 1 - 2 + 1); // one byte over
        var decoder = new Http11Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X: {value}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_when_multiple_small_headers_within_limit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 100);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-A: short\r\nX-B: also-short\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_total_headers_too_large_when_total_exceeds_limit()
    {
        // Early check fires when raw header section size exceeds maxTotalHeaderSize
        var decoder = new Http11Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 200);
        var sb = new StringBuilder();
        for (var i = 0; i < 15; i++)
        {
            sb.Append($"X-Hdr-{i:D2}: value-{i:D2}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_when_total_headers_exactly_at_limit()
    {
        // Raw header section: "HTTP/1.1 200 OK\r\nX: V\r\n" = 15+2+4+2 = 23 bytes
        // headerEnd = 23, so maxTotalHeaderSize = 23 → passes early check (23 > 23 is false)
        var decoder = new Http11Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 23);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", "X: V");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_total_headers_too_large_when_one_byte_over_total()
    {
        // "X: V" = 4 bytes, "Y: WW" = 5 bytes, total = 9 > 8
        var decoder = new Http11Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 8);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", "X: V\r\nY: WW");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_too_many_headers_when_count_exceeds_limit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(5); // 5 + Content-Length = 6 > 5

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_when_header_count_exactly_at_limit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(4); // 4 + Content-Length = 5

        var result = decoder.TryDecode(raw, out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_too_many_headers_when_one_over_count_limit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 10);
        var raw = BuildResponseWithNHeaders(10); // 10 + Content-Length = 11 > 10

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
        Assert.Contains("10", ex.Message); // limit value in message
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_reject_at_custom_limit_when_max_header_size_overridden()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 20);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-TooLong: this-value-is-way-too-long-for-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_reject_at_custom_total_limit_when_max_total_header_size_overridden()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 500, maxTotalHeaderSize: 50);
        var sb = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            sb.Append($"X-H{i}: value-padding-{i:D4}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_reject_at_custom_count_limit_when_max_header_count_overridden()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 3);
        var raw = BuildResponseWithNHeaders(3); // 3 + Content-Length = 4 > 3

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_obsolete_folding_when_folded_header_detected()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 500);
        const string raw = "HTTP/1.1 200 OK\r\nX-Folded: part1\r\n continued-text\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.ObsoleteFoldingDetected, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_obsolete_folding_when_tab_folded_header_detected()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nX-Folded: part1\r\n\tcontinued-with-tab\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.ObsoleteFoldingDetected, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_chunked_body_when_body_larger_than_header_limit()
    {
        // MaxHeaderSize is tiny but chunked body is large — body must not trigger header limits
        var decoder = new Http11Decoder(maxHeaderSize: 50, maxTotalHeaderSize: 200);
        var bodyChunk = new string('D', 500);
        var chunkLen = bodyChunk.Length.ToString("X");
        var raw = $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{chunkLen}\r\n{bodyChunk}\r\n0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_content_length_body_when_body_larger_than_header_limit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 50, maxTotalHeaderSize: 200);
        var body = new string('E', 500);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"Content-Length: {body.Length}", body);

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_include_header_name_when_single_header_too_large()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-Offending: this-value-exceeds-the-configured-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Contains("X-Offending", ex.Message);
        Assert.Contains("30", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_have_descriptive_message_when_total_headers_too_large()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-A: aaaaaaaaaa\r\nX-B: bbbbbbbbbb\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        // Error message references the security concern
        Assert.Contains("Total header size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_include_count_when_too_many_headers()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 3);
        var raw = BuildResponseWithNHeaders(3); // 3 + Content-Length = 4 > 3

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));

        Assert.Contains("3", ex.Message); // limit value
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_work_with_defaults_when_no_parameters_provided()
    {
        var decoder = new Http11Decoder();
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_header_too_large_when_connect_response_header_exceeds_limit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 20);
        var bigValue = new string('C', 50);
        var raw = $"HTTP/1.1 200 OK\r\nX-Connect: {bigValue}\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecodeConnect(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithNHeaders(int extraCount)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Length: 0\r\n");
        for (var i = 0; i < extraCount; i++)
        {
            sb.Append($"X-Header-{i:D3}: value\r\n");
        }
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
