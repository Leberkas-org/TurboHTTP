using System.Net;
using System.Text;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class RequestFormatSpec : AcceptanceTestBase
{
    private static byte[] BuildResponse(string body) =>
        Encoding.Latin1.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.Latin1.GetByteCount(body)}\r\n\r\n{body}");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Request_should_contain_http11_request_line_for_get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("GET /hello HTTP/1.1\r\n", rawRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Request_should_contain_http11_request_line_for_post()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/submit")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("POST /submit HTTP/1.1\r\n", rawRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.2")]
    public async Task Request_should_include_mandatory_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version11
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("Host: example.com\r\n", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public async Task Post_request_should_include_content_length()
    {
        var body = "test body content";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/upload")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("Content-Length:", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Request_should_include_body_after_headers()
    {
        var body = "exact payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        var headerEnd = rawRequest.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "Request must have header/body separator");
        var rawBody = rawRequest[(headerEnd + 4)..];
        Assert.Contains(body, rawBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Get_request_should_not_contain_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/no-body")
        {
            Version = HttpVersion.Version11
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        var headerEnd = rawRequest.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "Request must have header/body separator");
        var rawBody = rawRequest[(headerEnd + 4)..];
        Assert.Empty(rawBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.2.1")]
    public async Task Request_should_preserve_custom_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("X-Custom: test-value\r\n", rawRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.2.1")]
    public async Task Request_should_include_content_type_for_post()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/typed")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("data", Encoding.UTF8, "application/json")
        };

        var (_, rawRequest) = await SendScriptedWithCaptureAsync(
            CreateHttp11Engine(), request, (_, _) => BuildResponse("ok"));

        Assert.Contains("Content-Type:", rawRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("application/json", rawRequest, StringComparison.OrdinalIgnoreCase);
    }
}