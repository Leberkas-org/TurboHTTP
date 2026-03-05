using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http10;

public sealed class Http10EngineTests : EngineTestBase
{
    private static readonly Func<byte[]> Http10OkEmpty =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static readonly Func<byte[]> Http10OkWithBody =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    private static Http10Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§6.1: ST-10-ENG-001: Simple GET returns 200 with HTTP/1.0 version")]
    public async Task ST_10_ENG_001_SimpleGet_Returns200_WithVersion10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8.3: ST-10-ENG-002: POST with body returns 200")]
    public async Task ST_10_ENG_002_Post_WithBody_Returns200()
    {
        var body = "hello=world"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent(body)
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.2: ST-10-ENG-003: Response body readable via Content.ReadAsByteArrayAsync")]
    public async Task ST_10_ENG_003_ResponseBody_ReadableViaContent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10OkWithBody);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(5, body.Length);
        Assert.Equal("hello", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§6.1: ST-10-ENG-004: 404 response decoded to HttpStatusCode.NotFound")]
    public async Task ST_10_ENG_004_NotFound_DecodedCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§5: ST-10-ENG-005: Three sequential requests each return 200")]
    public async Task ST_10_ENG_005_ThreeSequentialRequests_AllReturn200()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3") { Version = HttpVersion.Version10 },
        };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Http10OkEmpty, 3);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpVersion.Version10, r.Version));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.1: ST-10-ENG-006: Request with custom header passes through to wire")]
    public async Task ST_10_ENG_006_CustomHeader_PassesThroughToWire()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.Contains("X-Custom: value", raw);
    }
}
