using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11SecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_accept_100_headers_when_at_default_limit()
    {
        var decoder = new Decoder(); // default maxHeaderCount = 100
        var raw = BuildResponseWithNHeaders(99); // 99 + Content-Length = 100
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_101_headers_when_above_default_limit()
    {
        var decoder = new Decoder();
        var raw = BuildResponseWithNHeaders(100); // 100 + Content-Length = 101

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_at_custom_limit_when_header_count_exceeded()
    {
        var decoder = new Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(5); // 5 + Content-Length = 6

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_accept_header_block_when_below_total_header_limit()
    {
        var decoder = new Decoder();
        // Well below the 64 KB total header limit
        var raw = BuildResponseWithHeaderBlockPosition(8191);
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_block_when_above_64kb_total_limit()
    {
        var decoder = new Decoder();
        // 65537 bytes (64 KB + 1) before the CRLFCRLF terminator — exceeds 64 KB total header limit
        var raw = BuildResponseWithHeaderBlockPosition(65537);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_single_header_when_value_exceeds_limit()
    {
        var decoder = new Decoder();
        // 17000 bytes exceeds the 16 KB (16384) single header limit
        var raw = BuildResponseWithLargeHeaderValue(17000);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_accept_body_when_at_configurable_limit()
    {
        var decoder = new Decoder(maxBodySize: 1024);
        var raw = BuildResponseWithBodySize(1024);
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_body_when_exceeding_limit()
    {
        var decoder = new Decoder(maxBodySize: 1024);
        var raw = BuildResponseWithContentLengthOnly(1025);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_body_when_zero_body_limit()
    {
        var decoder = new Decoder(maxBodySize: 0);
        var raw = BuildResponseWithContentLengthOnly(1);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_response_when_both_transfer_encoding_and_content_length_present()
    {
        var decoder = new Decoder();
        var raw = BuildResponseWithTeAndCl();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_when_crlf_injected_in_value()
    {
        var decoder = new Decoder();
        var raw = BuildResponseWithBareCrInHeaderValue();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldValue, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_when_nul_byte_in_value()
    {
        var decoder = new Decoder();
        var raw = BuildResponseWithNulInHeaderValue();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldValue, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_decode_cleanly_when_reset_after_partial_headers()
    {
        var decoder = new Decoder();

        // Feed incomplete headers (no CRLFCRLF yet)
        var incomplete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n"u8.ToArray();
        var gotResponse = decoder.TryDecode(incomplete, out _);
        Assert.False(gotResponse);

        // Reset clears remainder
        decoder.Reset();

        // Feed a complete valid response — decoder must behave as if fresh
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();
        var decoded = decoder.TryDecode(complete, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_decode_cleanly_when_reset_after_partial_body()
    {
        var decoder = new Decoder();

        // Feed headers + partial body (body says 10 bytes but we only send 5)
        var partial = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nHello"u8.ToArray();
        var gotResponse = decoder.TryDecode(partial, out _);
        Assert.False(gotResponse);

        // Reset discards the partial state
        decoder.Reset();

        // Feed a complete valid response
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nWorld"u8.ToArray();
        var decoded = decoder.TryDecode(complete, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
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

    private static ReadOnlyMemory<byte> BuildResponseWithHeaderBlockPosition(int headerEnd)
    {
        var paddingLength = headerEnd - 28;
        var padding = new string('a', paddingLength);
        var raw = $"HTTP/1.1 200 OK\r\nX-Padding: {padding}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithLargeHeaderValue(int valueLength)
    {
        var value = new string('x', valueLength);
        var raw = $"HTTP/1.1 200 OK\r\nX-Big: {value}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithBodySize(int bodySize)
    {
        var body = new string('B', bodySize);
        var raw = $"HTTP/1.1 200 OK\r\nContent-Length: {bodySize}\r\n\r\n{body}";
        return Encoding.ASCII.GetBytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithContentLengthOnly(int contentLength)
    {
        var raw = $"HTTP/1.1 200 OK\r\nContent-Length: {contentLength}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithTeAndCl()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        return Encoding.ASCII.GetBytes(response);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithBareCrInHeaderValue()
    {
        // Manually build bytes to embed a bare \r inside a header value.
        // Bytes: "HTTP/1.1 200 OK\r\n" + "X-Foo: hello\rworld\r\n" + "Content-Length: 0\r\n" + "\r\n"
        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var bareCr = new byte[] { 0x0D }; // bare CR (not followed by LF)
        var suffix = "world\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + bareCr.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        bareCr.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + bareCr.Length);
        return bytes;
    }

    private static ReadOnlyMemory<byte> BuildResponseWithNulInHeaderValue()
    {
        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var nul = new byte[] { 0x00 };
        var suffix = "world\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + nul.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        nul.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + nul.Length);
        return bytes;
    }
}
