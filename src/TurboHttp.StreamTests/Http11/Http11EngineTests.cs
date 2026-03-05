using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

public sealed class Http11EngineTests : EngineTestBase
{
    private static readonly Func<byte[]> Http11Response =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static readonly Func<byte[]> Http11ResponseWithBody =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§4: ST-11-001: Simple GET returns 200 with HTTP/1.1 version")]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§3.1: ST-11-002: Simple GET encodes request line")]
    public async Task Simple_GET_Encodes_Request_Line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo?bar=1")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.StartsWith("GET /foo?bar=1 HTTP/1.1\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§7.2: ST-11-003: GET contains mandatory Host header")]
    public async Task GET_Contains_Host_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Contains("Host: example.com", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6.1: ST-11-004: POST with body uses chunked or Content-Length framing")]
    public async Task POST_With_Body_Uses_Chunked_Or_Content_Length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("hello=world", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(raw.Contains("Content-Length:") || raw.Contains("Transfer-Encoding: chunked"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6.1: ST-11-005: Response with Content-Length body is decoded")]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http11ResponseWithBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§5: ST-11-006: Custom request header is forwarded verbatim")]
    public async Task Custom_Header_Is_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Contains("X-Custom: test-value", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3: ST-11-007: Multiple pipelined requests all return 200")]
    public async Task Multiple_Pipelined_Requests_All_Return_200()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3") { Version = HttpVersion.Version11 },
        };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Http11Response, 3);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpVersion.Version11, r.Version));
    }
}
