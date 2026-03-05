using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Phase 12 — HTTP/1.0 Integration Tests: Status code parsing.
/// Verifies that <see cref="TurboHttp.Protocol.Http10Decoder"/> correctly parses all 15
/// status codes listed in the plan.
/// </summary>
[Collection("Http10Integration")]
public sealed class Http10StatusCodeTests
{
    private readonly KestrelFixture _fixture;

    public Http10StatusCodeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── 2xx ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-060: GET /status/200 decoded status is 200 OK")]
    public async Task Status200_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-061: GET /status/201 decoded status is 201 Created")]
    public async Task Status201_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/201");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-062: GET /status/204 decoded status is 204 No Content with empty body")]
    public async Task Status204_IsDecodedCorrectly_EmptyBody()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/204");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-063: GET /status/206 decoded status is 206 Partial Content")]
    public async Task Status206_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/206");
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
    }

    // ── 3xx ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-064: GET /status/301 decoded status is 301 Moved Permanently")]
    public async Task Status301_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/301");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-065: GET /status/302 decoded status is 302 Found")]
    public async Task Status302_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/302");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    // ── 4xx ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-066: GET /status/400 decoded status is 400 Bad Request")]
    public async Task Status400_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/400");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-067: GET /status/401 decoded status is 401 Unauthorized")]
    public async Task Status401_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/401");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-068: GET /status/403 decoded status is 403 Forbidden")]
    public async Task Status403_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/403");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-069: GET /status/404 decoded status is 404 Not Found")]
    public async Task Status404_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/404");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-070: GET /status/405 decoded status is 405 Method Not Allowed")]
    public async Task Status405_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/405");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-071: GET /status/408 decoded status is 408 Request Timeout")]
    public async Task Status408_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/408");
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    // ── 5xx ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-072: GET /status/500 decoded status is 500 Internal Server Error")]
    public async Task Status500_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/500");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-073: GET /status/502 decoded status is 502 Bad Gateway")]
    public async Task Status502_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/502");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-074: GET /status/503 decoded status is 503 Service Unavailable")]
    public async Task Status503_IsDecodedCorrectly()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/503");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
