using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 response body handling per RFC 1945 §7.
/// Verifies that the entity body is read until connection close (EOF).
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945 §7: Entity body delimited by connection close in HTTP/1.0.
/// </remarks>
public sealed class Http10DecoderBodySpec
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

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_readbodycorrectly()
    {
        var decoder = new Http10Decoder();
        const string body = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {body.Length}\r\nContent-Type: text/plain", body);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_readexactbytes()
    {
        var decoder = new Http10Decoder();
        const string body = "ABCDE";
        const string raw = $"HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var bytes = await response!.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, bytes.Length);
        Assert.Equal("ABC", Encoding.ASCII.GetString(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_returnemptybody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_readuntilendofdata()
    {
        var decoder = new Http10Decoder();
        const string body = "body without content-length";
        const string raw = $"HTTP/1.0 200 OK\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var actualBody = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_preservebinarycontent()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_setcontentlengthheader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "Hello");

        decoder.TryDecode(data, out var response);

        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_returnnullcontent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_throwdecoderexception()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: -1");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecoderError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_throwmultiplecontentlength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\nContent-Length: 5\r\n\r\nHello";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_acceptidenticalcontentlength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_haveemptybody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified", "Content-Length: 100");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(bodyBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_haveemptybody_2()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 304 Not Modified\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(bodyBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_haveemptybody_3()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 204 No Content\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(bodyBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_preservenullbytes()
    {
        var bodyBytes = new byte[] { 0x48, 0x00, 0x65, 0x00, 0x6C };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actual = await response!.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_decode2mbbody()
    {
        var bodyBytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bodyBytes);
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var actual = await response!.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyBytes.Length, actual.Length);
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_handlecorrectly()
    {
        var decoder = new Http10Decoder();
        var longValue = new string('A', 8000);
        var raw = $"HTTP/1.0 200 OK\r\nX-Big: {longValue}\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Big", out var values));
        Assert.Equal(longValue, values.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_treatchunkedasrawbody()
    {
        var decoder = new Http10Decoder();
        // HTTP/1.0 does not support chunked TE — body should be raw
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Transfer-Encoding: chunked\r\nContent-Length: {chunkedBody.Length}", chunkedBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(chunkedBody, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10DecoderBodySpec_should_readbodyviaeof()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\nEOF body data";

        // First TryDecode consumes headers + body (no CL, so all remaining is body)
        decoder.TryDecode(Bytes(raw), out var response);

        var body = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("EOF body data", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10DecoderBodySpec_should_returnfalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }
}
