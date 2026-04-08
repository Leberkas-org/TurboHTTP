using System.Text;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Round-trip tests for HTTP/1.0 parsing under TCP fragmentation per RFC 1945.
/// Verifies that split delivery of bytes does not corrupt round-trip decode output.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http10Encoder"/>, <see cref="Http10Decoder"/>.
/// RFC 1945: Decoder must handle partial byte delivery across TryDecode calls.
/// </remarks>
public sealed class Http10RoundTripFragmentationSpec
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
    public async Task Http10RoundTripFragmentationSpec_should_handlefragmentationatstatusline()
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
        Assert.Equal("Hello", await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public void Http10RoundTripFragmentationSpec_should_handlefragmentationatheaderboundary()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10RoundTripFragmentationSpec_should_handlefragmentationatheaderendboundary()
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
        Assert.Equal("FooBar", await response2!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10RoundTripFragmentationSpec_should_handlebodyfragmentation()
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
        Assert.Equal(bodyText, await response3!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
