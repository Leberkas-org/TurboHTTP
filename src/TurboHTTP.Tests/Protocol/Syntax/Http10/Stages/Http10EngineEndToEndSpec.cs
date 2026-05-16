using System.Net;
using System.Text;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Stages;

public sealed class Http10EngineEndToEndSpec : EngineTestBase
{
    private static Http10Engine Engine => new(new TurboClientOptions());

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Http10EngineEndToEnd_should_return_body_with_version_10_when_get_returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version10
        };

        const string responseBody = "Hello, World!";
        var raw = $"HTTP/1.0 200 OK\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}";

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10EngineEndToEnd_should_include_body_in_wire_and_decode_response_when_post_with_body()
    {
        const string payload = "field=value&other=42";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        const string responseBody = "{\"ok\":true}";
        var raw =
            $"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}";

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        // Wire must contain the POST body
        Assert.Contains(payload, rawRequest);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}", rawRequest);

        // Response must carry body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, respBody);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10EngineEndToEnd_should_set_correct_status_code_and_reason_phrase_when_404_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        {
            Version = HttpVersion.Version10
        };

        const string raw = "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n";

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.2")]
    public async Task Http10EngineEndToEnd_should_include_custom_header_in_wire_when_custom_request_header_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Add("X-Correlation-Id", "abc-123");

        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n";

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        // Custom header must appear in the wire bytes sent to the server
        Assert.Contains("X-Correlation-Id: abc-123", rawRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Http10EngineEndToEnd_should_set_request_message_to_original_request_when_response_received()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/corr")
        {
            Version = HttpVersion.Version10
        };

        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n";

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        Assert.NotNull(response.RequestMessage);
        Assert.Same(request, response.RequestMessage);
    }
}