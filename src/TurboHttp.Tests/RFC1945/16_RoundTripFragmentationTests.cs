using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Fragmentation Tests
/// Verifies that fragmented messages decode correctly after encode
/// </summary>
public sealed class Http10RoundTripFragmentationTests
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

    [Fact(DisplayName = "RFC1945-RT-F01: Fragmented at status line")]
    public async Task Should_HandleFragmentationAtStatusLine_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var fullResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Bytes(fullResponse);

        // Fragment at status line boundary (first 10 bytes)
        var fragment1 = bytes[..10];
        var result1 = decoder.TryDecode(fragment1, out var response1);

        Assert.False(result1); // Should need more data
        Assert.Null(response1);

        // Send remaining
        var fragment2 = bytes[10..];
        var result2 = decoder.TryDecode(fragment2, out var response2);

        Assert.True(result2);
        Assert.NotNull(response2);
        Assert.Equal("Hello", await response2.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC1945-RT-F02: Fragmented at header boundary")]
    public void Should_HandleFragmentationAtHeaderBoundary_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var fullResponse = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 4\r\n\r\nTest";
        var bytes = Bytes(fullResponse);

        // Fragment at header boundary (after first header line)
        var fragment1 = bytes[..(fullResponse.IndexOf("\r\n") + 2)];
        var result1 = decoder.TryDecode(fragment1, out var response1);

        Assert.False(result1); // Should need more data
        Assert.Null(response1);

        // Send remaining
        var fragment2 = bytes[fragment1.Length..];
        var result2 = decoder.TryDecode(fragment2, out var response2);

        Assert.True(result2);
        Assert.NotNull(response2);
    }

    [Fact(DisplayName = "RFC1945-RT-F03: Fragmented at CRLF CRLF boundary")]
    public async Task Should_HandleFragmentationAtHeaderEndBoundary_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var fullResponse = "HTTP/1.0 200 OK\r\nContent-Length: 6\r\n\r\nFooBar";
        var bytes = Bytes(fullResponse);

        // Fragment at header-body separator
        var separatorIndex = fullResponse.IndexOf("\r\n\r\n");
        var fragment1 = bytes[..(separatorIndex + 2)]; // Include one \r\n
        var result1 = decoder.TryDecode(fragment1, out var response1);

        Assert.False(result1); // Should need more data

        // Send remaining
        var fragment2 = bytes[fragment1.Length..];
        var result2 = decoder.TryDecode(fragment2, out var response2);

        Assert.True(result2);
        Assert.Equal("FooBar", await response2!.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC1945-RT-F04: Fragmented body delivery")]
    public async Task Should_HandleBodyFragmentation_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var bodyText = "This is a fragmented body";
        var fullResponse = $"HTTP/1.0 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n{bodyText}";
        var bytes = Bytes(fullResponse);

        // Fragment after headers
        var headerEndIndex = fullResponse.IndexOf("\r\n\r\n") + 4;
        var fragment1 = bytes[..headerEndIndex]; // Headers only
        var result1 = decoder.TryDecode(fragment1, out var response1);

        Assert.False(result1); // Should need body data

        // Send first half of body (only new data, not cumulative)
        var midPoint = headerEndIndex + (bodyText.Length / 2);
        var fragment2 = bytes[headerEndIndex..midPoint];
        var result2 = decoder.TryDecode(fragment2, out var response2);

        Assert.False(result2); // Still incomplete

        // Send all remaining data
        var fragment3 = bytes[midPoint..];
        var result3 = decoder.TryDecode(fragment3, out var response3);

        Assert.True(result3);
        Assert.Equal(bodyText, await response3!.Content.ReadAsStringAsync());
    }
}
