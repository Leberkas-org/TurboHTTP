using System.Net;
using System.Text;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class RequestFormatSpec : AcceptanceTestBase
{
    private static byte[] BuildResponse(string body) =>
        Encoding.Latin1.GetBytes(
            $"HTTP/1.0 200 OK\r\nContent-Length: {Encoding.Latin1.GetByteCount(body)}\r\n\r\n{body}");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Request_should_contain_correct_request_line_for_get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version10
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("GET /hello HTTP/1.0\r\n", rawRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Request_should_contain_correct_request_line_for_post()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("POST /submit HTTP/1.0\r\n", rawRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Request_should_omit_host_header_per_http10_spec()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version10
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.DoesNotContain("Host:", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public async Task Post_request_should_include_content_length()
    {
        var body = "test body content";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/upload")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("Content-Length:", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Request_should_not_contain_transfer_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/data")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("some data", Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.DoesNotContain("Transfer-Encoding", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Request_should_include_body_after_headers()
    {
        var body = "exact payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        var headerEnd = rawRequest.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "Request must have header/body separator");
        var rawBody = rawRequest[(headerEnd + 4)..];
        Assert.Contains(body, rawBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Get_request_should_not_contain_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/no-body")
        {
            Version = HttpVersion.Version10
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp10Engine(), request, (_, _) => BuildResponse("ok"));

        var headerEnd = rawRequest.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "Request must have header/body separator");
        var rawBody = rawRequest[(headerEnd + 4)..];
        Assert.Empty(rawBody);
    }
}