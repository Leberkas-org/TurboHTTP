using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC 1945 — Http10Engine end-to-end round-trip tests.
/// Each test drives a full request → encoder → fake-TCP → decoder → correlation cycle
/// using <see cref="EngineTestBase.SendAsync"/>.
/// </summary>
public sealed class Http10EngineRfcRoundTripTests : EngineTestBase
{
    private static Http10Engine Engine => new();

    // ── 10ENG-001: GET → 200 with body — version 1.0 in response ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-4.1-10EN-001: GET → 200 with body — version 1.0 in response")]
    public async Task ENG_001_Get_Returns_200_With_Body_And_Version10()
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

    // ── 10ENG-002: POST with body → request body in wire, 200 response with body ─

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10EN-002: POST with body → request body in wire, 200 response with body")]
    public async Task ENG_002_Post_Body_In_Wire_And_Response_Body()
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

    // ── 10ENG-003: 404 response → StatusCode correct, ReasonPhrase present ───────

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-6.1-10EN-003: 404 response → StatusCode correct, ReasonPhrase present")]
    public async Task ENG_003_404_Response_StatusCode_And_ReasonPhrase()
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

    // ── 10ENG-004: Custom request header → in wire and response carries header ───

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.2-10EN-004: Custom request header → present in wire bytes")]
    public async Task ENG_004_Custom_Request_Header_In_Wire()
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

    // ── 10ENG-005: Response correlation — response.RequestMessage == sent request ─

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-4.1-10EN-005: response.RequestMessage is the original sent request")]
    public async Task ENG_005_Response_RequestMessage_Is_Original_Request()
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
