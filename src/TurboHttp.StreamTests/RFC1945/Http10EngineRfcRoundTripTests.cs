using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// RFC-tagged round-trip tests for the HTTP/1.0 engine per RFC 1945.
/// Verifies end-to-end request encoding and response decoding through the full HTTP/1.0 protocol flow.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10Engine"/>.
/// RFC 1945 §4.1: HTTP/1.0 full request/response message exchange.
/// </remarks>
public sealed class Http10EngineRfcRoundTripTests : EngineTestBase
{
    private static Http10Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-4.1-10EN-001: GET → 200 with body — version 1.0 in response")]
    public async Task Should_ReturnBodyWithVersion10_When_GetReturns200()
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
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10EN-002: POST with body → request body in wire, 200 response with body")]
    public async Task Should_IncludeBodyInWireAndDecodeResponse_When_PostWithBody()
    {
        const string payload = "field=value&other=42";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        const string responseBody = "{\"ok\":true}";
        var raw = $"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}";

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Encoding.Latin1.GetBytes(raw));

        // Wire must contain the POST body
        Assert.Contains(payload, rawRequest);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}", rawRequest);

        // Response must carry body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, respBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10EN-003: 404 response → StatusCode correct, ReasonPhrase present")]
    public async Task Should_SetCorrectStatusCodeAndReasonPhrase_When_404Response()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.2-10EN-004: Custom request header → present in wire bytes")]
    public async Task Should_IncludeCustomHeaderInWire_When_CustomRequestHeaderSet()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-4.1-10EN-005: response.RequestMessage is the original sent request")]
    public async Task Should_SetRequestMessageToOriginalRequest_When_ResponseReceived()
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
