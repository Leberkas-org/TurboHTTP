using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Body Tests
/// Verifies that body content is preserved through encode→decode cycles
/// </summary>
public sealed class Http10RoundTripBodyTests
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

    [Fact(DisplayName = "RFC1945-RT-B01: Content-Length with text body round-trip")]
    public async Task Should_PreserveTextBody_When_ContentLengthRoundTrip()
    {
        var decoder = new Http10Decoder();
        var bodyText = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyText.Length}", bodyText);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, content);
    }

    [Fact(DisplayName = "RFC1945-RT-B02: Binary body with correct Content-Length")]
    public async Task Should_PreserveBinaryBody_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var binaryBody = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var data = BuildBinaryResponse("HTTP/1.0 200 OK",
            $"Content-Length: {binaryBody.Length}", binaryBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryBody, content);
    }

    [Fact(DisplayName = "RFC1945-RT-B03: UTF-8 encoded body round-trip")]
    public async Task Should_PreserveUtf8Body_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var bodyText = "Hello, 世界! Привет!";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
        var data = BuildBinaryResponse("HTTP/1.0 200 OK",
            $"Content-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}",
            bodyBytes);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, content);
    }

    [Fact(DisplayName = "RFC1945-RT-B04: Empty body with Content-Length 0")]
    public async Task Should_DecodeEmptyBody_When_ContentLengthZero()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content",
            "Content-Length: 0", "");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync();
        Assert.Empty(content);
    }

    [Fact(DisplayName = "RFC1945-RT-B05: Large body (1MB) round-trip")]
    public async Task Should_PreserveLargeBody_When_1MbRoundTrip()
    {
        var decoder = new Http10Decoder();
        var largeBody = new string('X', 1048576); // 1 MB
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {largeBody.Length}", largeBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var content = await response!.Content.ReadAsStringAsync();
        Assert.Equal(1048576, content.Length);
        Assert.True(content.All(c => c == 'X'));
    }
}
