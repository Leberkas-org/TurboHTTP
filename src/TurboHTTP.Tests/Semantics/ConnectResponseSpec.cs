using System.Net;
using System.Text;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Semantics;

public sealed class ConnectResponseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.6")]
    public async Task ConnectResponse_should_ignore_content_length_when_200()
    {
        using var decoder = new Decoder();
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
    public async Task ConnectResponse_should_ignore_transfer_encoding_when_200()
    {
        using var decoder = new Decoder();
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
    public async Task ConnectResponse_should_parse_body_when_407()
    {
        using var decoder = new Decoder();
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
    public async Task ConnectResponse_should_respect_content_length_when_non_connect_200()
    {
        // Verify that normal TryDecode still requires Content-Length body
        using var decoder = new Decoder();
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
    public async Task ConnectResponse_should_return_empty_body_when_200_with_trailing_data()
    {
        using var decoder = new Decoder();
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
    public async Task Http10_should_ignore_content_length_when_connect_200()
    {
        var decoder = new TurboHTTP.Protocol.Http10.Decoder();
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
    public async Task Http10_should_parse_body_when_connect_407()
    {
        var decoder = new TurboHTTP.Protocol.Http10.Decoder();
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