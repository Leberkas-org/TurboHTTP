using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 decoder header size limits (security: DoS protection).
/// Verifies that oversized individual headers and total header blocks are rejected.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// Security: Prevents memory exhaustion via oversized headers.
/// </remarks>
public sealed class Http10DecoderHeaderLimitsSpec
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static string BuildRawResponse(string statusLine, string headers, string body = "")
        => $"{statusLine}\r\n{headers}\r\n\r\n{body}";


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_usedefaultmaxheadersize()
    {
        var decoder = new Http10Decoder();
        // A header just under 16KB should be accepted
        var value = new string('A', 16 * 1024 - 20); // name + ": " + value < 16KB
        var raw = BuildRawResponse("HTTP/1.0 200 OK", $"X-Big: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_usedefaultmaxtotalheadersize()
    {
        var decoder = new Http10Decoder();
        // Build headers totalling just under 64KB
        var sb = new StringBuilder();
        var headerValue = new string('B', 1000);
        for (var i = 0; i < 60; i++)
        {
            sb.Append($"X-Hdr-{i:D3}: {headerValue}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.0 200 OK", sb.ToString());

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwheadertoolarge()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 100);
        var bigValue = new string('X', 200);
        var raw = BuildRawResponse("HTTP/1.0 200 OK", $"X-Big: {bigValue}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
        Assert.Contains("X-Big", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_accept()
    {
        // name="X" (1 byte) + ": " (2 bytes) + value = maxHeaderSize
        const int limit = 50;
        var value = new string('V', limit - 1 - 2); // "X" + ": " + value = 50
        var decoder = new Http10Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.0 200 OK", $"X: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwheadertoolarge_2()
    {
        const int limit = 50;
        var value = new string('V', limit - 1 - 2 + 1); // one byte over
        var decoder = new Http10Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.0 200 OK", $"X: {value}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_accept_2()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 100);
        var raw = BuildRawResponse("HTTP/1.0 200 OK",
            "X-A: short\r\nX-B: also-short\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwtotalheaderstoolarge()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 200);
        var sb = new StringBuilder();
        // Each header is ~20 bytes; 15 headers = ~300 bytes > 200 limit
        for (var i = 0; i < 15; i++)
        {
            sb.Append($"X-Hdr-{i:D2}: value-{i:D2}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.0 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
        Assert.Contains("200", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_accept_3()
    {
        // "X: V" = 1 + 2 + 1 = 4 bytes per header
        var decoder = new Http10Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 8);
        var raw = BuildRawResponse("HTTP/1.0 200 OK", "X: V\r\nY: W");

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwtotalheaderstoolarge_2()
    {
        // "X: V" = 4 bytes, "Y: WW" = 5 bytes, total = 9 > 8
        var decoder = new Http10Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 8);
        var raw = BuildRawResponse("HTTP/1.0 200 OK", "X: V\r\nY: WW");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_rejectatcustomlimit()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 20);
        var raw = BuildRawResponse("HTTP/1.0 200 OK",
            "X-TooLong: this-value-is-way-too-long-for-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_rejectatcustomtotallimit()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 500, maxTotalHeaderSize: 50);
        var sb = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            sb.Append($"X-H{i}: value-padding-{i:D4}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.0 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwheadertoolarge_3()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 30);
        // "X-Folded: part1" = 15 bytes, after fold "X-Folded: part1 continued-text" > 30
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: part1\r\n continued-text-that-is-long\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
        Assert.Contains("X-Folded", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwtotalheaderstoolarge_3()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 500, maxTotalHeaderSize: 40);
        // "X-A: val" = 8 bytes; "X-Folded: part1" = 15 bytes + fold adds more
        const string raw = "HTTP/1.0 200 OK\r\nX-A: value-a\r\nX-Folded: part1\r\n continuation-that-pushes-total-over\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.True(
            ex.DecodeError is HttpDecoderError.TotalHeadersTooLarge or HttpDecoderError.HeaderTooLarge,
            $"Expected HeaderTooLarge or TotalHeadersTooLarge, got {ex.DecodeError}");
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_includeheadername()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Offending: this-value-exceeds-the-configured-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Contains("X-Offending", ex.Message);
        Assert.Contains("30", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_includetotalsize()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.0 200 OK",
            "X-A: aaaaaaaaaa\r\nX-B: bbbbbbbbbb\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Contains("30", ex.Message);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwheadertoolarge_4()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 20);
        var bigValue = new string('Z', 50);
        // No Content-Length — body delimited by EOF, so TryDecodeEof must process headers
        var raw = $"HTTP/1.0 200 OK\r\nX-EofBig: {bigValue}\r\n\r\nbody";

        // Feed all data — TryDecode returns the response with body since headers end is found
        // But the header validation happens during header parsing in TryDecode
        var ex = Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_throwheadertoolarge_5()
    {
        var decoder = new Http10Decoder(maxHeaderSize: 20);
        var bigValue = new string('C', 50);
        var raw = $"HTTP/1.0 200 OK\r\nX-Connect: {bigValue}\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecodeConnect(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10DecoderHeaderLimitsSpec_should_workwithdefaults()
    {
        var decoder = new Http10Decoder();
        var raw = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }
}
