using System.Net;
using System.Text;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Tests.Http10;

/// <summary>
/// Tests HTTP/0.9 Simple-Response compatibility per RFC 1945 §3.1.
/// HTTP/1.0 clients must understand any valid response in the format of HTTP/0.9.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Decoder"/>.
/// RFC 1945 §3.1: Simple-Response has no status-line — just body until connection close.
/// </remarks>
public sealed class Http09SimpleResponseSpec
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Http09SimpleResponseSpec_should_parseashttp09()
    {
        var decoder = new Http10Decoder();
        var body = "<html>Hello</html>";
        var data = Bytes(body);

        // First call: detects HTTP/0.9, buffers data
        var result = decoder.TryDecode(data, out var response);
        Assert.False(result);
        Assert.Null(response);

        // EOF completes the response
        result = decoder.TryDecodeEof(out response);
        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(0, 9), response.Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Http09SimpleResponseSpec_should_haveemptyheaders()
    {
        var decoder = new Http10Decoder();
        var data = Bytes("some body content");

        decoder.TryDecode(data, out _);
        decoder.TryDecodeEof(out var response);

        Assert.NotNull(response);
        Assert.DoesNotContain(response.Headers, _ => true);
        Assert.DoesNotContain(response.Content.Headers,
            h => !h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task Http09SimpleResponseSpec_should_readbodyuntileof()
    {
        var decoder = new Http10Decoder();
        var chunk1 = Bytes("Hello ");
        var chunk2 = Bytes("World");

        // Feed data in multiple chunks
        decoder.TryDecode(chunk1, out _);
        decoder.TryDecode(chunk2, out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.True(result);
        Assert.NotNull(response);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", Encoding.GetEncoding("ISO-8859-1").GetString(body));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task Http09SimpleResponseSpec_should_parsenormally()
    {
        var decoder = new Http10Decoder();
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var data = Bytes(raw);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        Assert.Equal("OK", response.ReasonPhrase);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Http09SimpleResponseSpec_should_handleempty()
    {
        var decoder = new Http10Decoder();

        // Feed empty data then signal EOF
        decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out _);
        // No data at all — signal EOF directly
        var result = decoder.TryDecodeEof(out var response);

        // No data was ever received, so nothing to decode
        Assert.False(result);
    }
}
