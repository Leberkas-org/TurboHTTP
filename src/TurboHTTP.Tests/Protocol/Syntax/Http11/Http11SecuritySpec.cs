using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11SecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_accept_100_headers_when_at_default_limit()
    {
        // Default MaxHeaderCount = 100; 99 extra + Content-Length = 100 total
        var raw = BuildResponseWithNHeaders(99);
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome = decoder.Feed(raw.Span, false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_101_headers_when_above_default_limit()
    {
        // 100 extra + Content-Length = 101 total, exceeds default MaxHeaderCount = 100
        var raw = BuildResponseWithNHeaders(100);
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_at_custom_limit_when_header_count_exceeded()
    {
        // 5 extra + Content-Length = 6 total, exceeds custom MaxHeaderCount = 5
        var raw = BuildResponseWithNHeaders(5);
        var opts = new Http11ClientDecoderOptions { Shared = SharedHttpOptions.Default with { MaxHeaderCount = 5 } };
        var decoder = new Http11ClientDecoder(opts);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_accept_header_block_when_below_total_header_limit()
    {
        // Build a response with ~8KB of headers, well below the 32KB MaxHeaderBytes default
        var raw = BuildResponseWithLargeHeader(8191);
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome = decoder.Feed(raw.Span, false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_block_when_above_total_header_limit()
    {
        // Build a response with headers exceeding MaxHeaderBytes (32KB default)
        var raw = BuildResponseWithLargeHeader(33000);
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_single_header_when_value_exceeds_limit()
    {
        // 17000 bytes exceeds the default HeaderLineMaxLength (8KB)
        var raw = BuildResponseWithLargeHeaderValue(17000);
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_response_when_both_transfer_encoding_and_content_length_present()
    {
        var raw = BuildResponseWithTeAndCl();
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_when_crlf_injected_in_value()
    {
        var raw = BuildResponseWithBareCrInHeaderValue();
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_reject_header_when_nul_byte_in_value()
    {
        var raw = BuildResponseWithNulInHeaderValue();
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.Span, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_decode_cleanly_when_reset_after_partial_headers()
    {
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        // Feed incomplete headers (no CRLFCRLF yet)
        var incomplete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n"u8.ToArray();
        var outcome1 = decoder.Feed(incomplete.AsSpan(), false, out _);
        Assert.Equal(DecodeOutcome.NeedMore, outcome1);

        // Reset clears remainder
        decoder.Reset();

        // Feed a complete valid response — decoder must behave as if fresh
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();
        var outcome2 = decoder.Feed(complete.AsSpan(), false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-11")]
    public void Http11Security_should_decode_cleanly_when_reset_after_partial_body()
    {
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);

        // Feed headers + partial body (body says 10 bytes but we only send 5)
        var partial = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nHello"u8.ToArray();
        var outcome1 = decoder.Feed(partial.AsSpan(), false, out _);
        Assert.Equal(DecodeOutcome.NeedMore, outcome1);

        // Reset discards the partial state
        decoder.Reset();

        // Feed a complete valid response
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nWorld"u8.ToArray();
        var outcome2 = decoder.Feed(complete.AsSpan(), false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome2);
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

    private static ReadOnlyMemory<byte> BuildResponseWithLargeHeader(int totalHeaderBytes)
    {
        // Build a single large header value + Content-Length so the response is complete
        var fixedPart = "HTTP/1.1 200 OK\r\nX-Padding: ";
        var suffix = "\r\nContent-Length: 0\r\n\r\n";
        var paddingLength = totalHeaderBytes - fixedPart.Length - suffix.Length;
        if (paddingLength < 0)
        {
            paddingLength = 0;
        }

        var raw = fixedPart + new string('a', paddingLength) + suffix;
        return Encoding.ASCII.GetBytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildResponseWithLargeHeaderValue(int valueLength)
    {
        var value = new string('x', valueLength);
        var raw = $"HTTP/1.1 200 OK\r\nX-Big: {value}\r\n\r\n";
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
        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var bareCr = new byte[] { 0x0D };
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