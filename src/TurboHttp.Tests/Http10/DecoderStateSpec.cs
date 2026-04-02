using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 decoder state lifecycle per RFC 1945.
/// Verifies Reset() clears internal state and the decoder is reusable across connections.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945: Decoder must be resettable between sequential connections.
/// </remarks>
public sealed class Http10DecoderStateSpec
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_returntrue()
    {
        // HTTP/0.9 response: no headers — entire buffer is body, delimited by EOF (RFC 1945 §2.1)
        var decoder = new Http10Decoder();
        var body = Bytes("<html>response body</html>");
        decoder.TryDecode(body, out _);

        var result = decoder.TryDecodeEof(out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_returnfalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_returnfalse_2()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200");
        decoder.TryDecode(incomplete, out _);

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_clearremainder()
    {
        // HTTP/0.9 response: first TryDecodeEof clears buffered body; second call returns false
        var decoder = new Http10Decoder();
        var body = Bytes("<html>some body</html>");
        decoder.TryDecode(body, out _);

        decoder.TryDecodeEof(out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_throw()
    {
        // RFC 1945 §7.2.2: if Content-Length is declared, EOF before all bytes is an error
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder.TryDecode(partial, out _);

        Assert.Throws<HttpDecoderException>(() => decoder.TryDecodeEof(out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_clearbuffereddata()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_decodenewresponse()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var fresh = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.Equal(System.Net.HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_notthrow()
    {
        var decoder = new Http10Decoder();

        var ex = Record.Exception(() =>
        {
            decoder.Reset();
            decoder.Reset();
            decoder.Reset();
        });

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_returnfalse_3()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_preservestate()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nX-Header: value\r\nContent-Length: 5\r\n\r\nHello");

        // First decode with partial data
        var chunk1 = full[..20];
        var result1 = decoder.TryDecode(chunk1, out _);
        Assert.False(result1);

        // Second decode with remaining data
        var chunk2 = full[20..];
        var result2 = decoder.TryDecode(chunk2, out var response);

        Assert.True(result2);
        Assert.NotNull(response);
        Assert.True(response.Headers.TryGetValues("X-Header", out var values));
        Assert.Contains("value", values);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_bereusable()
    {
        var decoder = new Http10Decoder();

        var data1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(data1, out var response1);
        Assert.NotNull(response1);

        decoder.Reset();

        var data2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");
        decoder.TryDecode(data2, out var response2);

        Assert.NotNull(response2);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response2.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_beidempotent()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200");
        decoder.TryDecode(partial, out _);

        decoder.Reset();
        decoder.Reset();

        // Should still be able to decode new data
        var fresh = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_maintainstate()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello");

        // Feed one byte at a time for first part, then flush
        for (var i = 0; i < full.Length - 1; i++)
        {
            var chunk = full.Slice(i, 1);
            var result = decoder.TryDecode(chunk, out var response);
            if (result)
            {
                Assert.NotNull(response);
                return;
            }
        }

        // Last chunk
        Assert.True(decoder.TryDecode(full.Slice(full.Length - 1, 1), out var finalResponse));
        Assert.NotNull(finalResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10DecoderStateSpec_should_handleeof()
    {
        var decoder = new Http10Decoder();
        var complete = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(complete, out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }
}
