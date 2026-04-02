using System.Text;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Tests.Http10;

/// <summary>
/// Round-trip tests for HTTP/1.0 entity bodies per RFC 1945 §7.
/// Verifies that body content survives encode-then-decode with correct byte boundaries.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http10Encoder"/>, <see cref="Http10Decoder"/>.
/// RFC 1945 §7: Entity body.
/// </remarks>
public sealed class Http10RoundTripBodySpec
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body)
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildBinaryResponse(
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
    public async Task Http10RoundTripBodySpec_should_preservetextbody()
    {
        var decoder = new Http10Decoder();
        var bodyText = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyText.Length}", bodyText);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10RoundTripBodySpec_should_preservebinarybody()
    {
        var decoder = new Http10Decoder();
        var binaryBody = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var data = BuildBinaryResponse("HTTP/1.0 200 OK",
            $"Content-Length: {binaryBody.Length}", binaryBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(binaryBody, content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10RoundTripBodySpec_should_preserveutf8body()
    {
        var decoder = new Http10Decoder();
        var bodyText = "Hello, ??! ??????!";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
        var data = BuildBinaryResponse("HTTP/1.0 200 OK",
            $"Content-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}",
            bodyBytes);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10RoundTripBodySpec_should_decodeemptybody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content",
            "Content-Length: 0", "");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10RoundTripBodySpec_should_preservelargebody()
    {
        var decoder = new Http10Decoder();
        var largeBody = new string('X', 1048576); // 1 MB
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {largeBody.Length}", largeBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1048576, content.Length);
        Assert.True(content.All(c => c == 'X'));
    }
}
