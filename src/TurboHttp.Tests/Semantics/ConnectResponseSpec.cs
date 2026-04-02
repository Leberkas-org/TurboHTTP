using System.Net;
using System.Text;
using TurboHttp.Protocol.Http10;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Semantics;

/// <summary>
/// Tests CONNECT response body handling per RFC 9110 §9.3.6.
/// A successful (2xx) CONNECT response has no body — the connection transitions
/// to a tunnel. Content-Length and Transfer-Encoding MUST be ignored.
/// Non-2xx responses (e.g. 407) are decoded with normal body handling.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http11Decoder"/> and <see cref="Http10Decoder"/>.
/// </remarks>
public sealed class ConnectResponseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Should_IgnoreContentLength_When_Connect200()
    {
        using var decoder = new Http11Decoder();
        // Server sends 200 with Content-Length: 100 but no body bytes follow
        // (the tunnel is established; CL must be ignored)
        var raw = "HTTP/1.1 200 Connection Established\r\nContent-Length: 100\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecodeConnect(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Should_IgnoreTE_When_Connect200()
    {
        using var decoder = new Http11Decoder();
        // Server sends 200 with Transfer-Encoding: chunked but no body follows
        var raw = "HTTP/1.1 200 Connection Established\r\nTransfer-Encoding: chunked\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecodeConnect(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Should_ParseBody_When_Connect407()
    {
        using var decoder = new Http11Decoder();
        var bodyText = "Proxy Authentication Required";
        var raw = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 407 Proxy Authentication Required\r\n" +
            $"Content-Length: {bodyText.Length}\r\n" +
            $"Content-Type: text/plain\r\n\r\n" +
            bodyText);

        var decoded = decoder.TryDecodeConnect(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Should_RespectCL_When_NonConnect200()
    {
        // Verify that normal TryDecode still requires Content-Length body
        using var decoder = new Http11Decoder();
        var bodyText = "Hello World";
        var raw = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Length: {bodyText.Length}\r\n\r\n" +
            bodyText);

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Should_ReturnEmptyBody_When_Connect200WithTrailingData()
    {
        using var decoder = new Http11Decoder();
        // Even if tunnel data follows the 200, it should not be parsed as response body
        var raw = "HTTP/1.1 200 Connection Established\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();

        var decoded = decoder.TryDecodeConnect(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Http10_Should_IgnoreContentLength_When_Connect200()
    {
        var decoder = new Http10Decoder();
        var raw = "HTTP/1.0 200 Connection Established\r\nContent-Length: 100\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecodeConnect(raw, out var response);

        Assert.True(decoded);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task Http10_Should_ParseBody_When_Connect407()
    {
        var decoder = new Http10Decoder();
        var bodyText = "Auth Required";
        var raw = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 407 Proxy Authentication Required\r\n" +
            $"Content-Length: {bodyText.Length}\r\n\r\n" +
            bodyText);

        var decoded = decoder.TryDecodeConnect(raw, out var response);

        Assert.True(decoded);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyText, body);
    }
}
