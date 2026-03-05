using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 13 — HTTP/1.1 Integration Tests: Status code and error handling scenarios.
/// Verifies that Http11Decoder correctly parses all 2xx, 3xx, 4xx, and 5xx
/// status codes returned by a real Kestrel server.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11StatusAndErrorTests
{
    private readonly KestrelFixture _fixture;

    public Http11StatusAndErrorTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── 2xx Success ───────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-090: GET /status/200 returns 200 OK")]
    public async Task Get_Status200_Returns200OK()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-091: GET /status/201 returns 201 Created")]
    public async Task Get_Status201_Returns201Created()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/201");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-092: GET /status/202 returns 202 Accepted")]
    public async Task Get_Status202_Returns202Accepted()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/202");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-093: GET /status/204 returns 204 No Content with empty body")]
    public async Task Get_Status204_Returns204NoContent_EmptyBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/204");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-094: GET /status/206 returns 206 Partial Content")]
    public async Task Get_Status206_Returns206PartialContent()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/206");
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
    }

    // ── 3xx Redirection ───────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-095: GET /status/301 returns 301 Moved Permanently")]
    public async Task Get_Status301_ReturnsMovedPermanently()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/301");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-096: GET /status/302 returns 302 Found")]
    public async Task Get_Status302_ReturnsFound()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/302");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-097: GET /status/303 returns 303 See Other")]
    public async Task Get_Status303_ReturnsSeeOther()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/303");
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-098: GET /status/307 returns 307 Temporary Redirect")]
    public async Task Get_Status307_ReturnsTemporaryRedirect()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/307");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-099: GET /status/308 returns 308 Permanent Redirect")]
    public async Task Get_Status308_ReturnsPermanentRedirect()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/308");
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }

    // ── 304 No Body ───────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-100: GET /status/304 returns 304 Not Modified with empty body")]
    public async Task Get_Status304_ReturnsNotModified_EmptyBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/304");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── 4xx Client Errors ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-101: GET /status/400 returns 400 Bad Request")]
    public async Task Get_Status400_ReturnsBadRequest()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/400");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-102: GET /status/401 returns 401 Unauthorized")]
    public async Task Get_Status401_ReturnsUnauthorized()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/401");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-103: GET /status/403 returns 403 Forbidden")]
    public async Task Get_Status403_ReturnsForbidden()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/403");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-104: GET /status/404 returns 404 Not Found")]
    public async Task Get_Status404_ReturnsNotFound()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/404");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-105: GET /status/405 returns 405 Method Not Allowed")]
    public async Task Get_Status405_ReturnsMethodNotAllowed()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/405");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-106: GET /status/408 returns 408 Request Timeout")]
    public async Task Get_Status408_ReturnsRequestTimeout()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/408");
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-107: GET /status/409 returns 409 Conflict")]
    public async Task Get_Status409_ReturnsConflict()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/409");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-108: GET /status/410 returns 410 Gone")]
    public async Task Get_Status410_ReturnsGone()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/410");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-109: GET /status/413 returns 413 Content Too Large")]
    public async Task Get_Status413_ReturnsContentTooLarge()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/413");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-110: GET /status/429 returns 429 Too Many Requests")]
    public async Task Get_Status429_ReturnsTooManyRequests()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/429");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // ── 5xx Server Errors ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-111: GET /status/500 returns 500 Internal Server Error")]
    public async Task Get_Status500_ReturnsInternalServerError()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/500");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-112: GET /status/501 returns 501 Not Implemented")]
    public async Task Get_Status501_ReturnsNotImplemented()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/501");
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-113: GET /status/502 returns 502 Bad Gateway")]
    public async Task Get_Status502_ReturnsBadGateway()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/502");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-114: GET /status/503 returns 503 Service Unavailable")]
    public async Task Get_Status503_ReturnsServiceUnavailable()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/503");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11-115: GET /status/504 returns 504 Gateway Timeout")]
    public async Task Get_Status504_ReturnsGatewayTimeout()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/504");
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    // ── Theory: all 2xx codes have bodies (except 204) ───────────────────────

    [Theory(Timeout = 10_000, DisplayName = "IT-11-116: 2xx status codes (except 204) have non-empty body")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(206)]
    public async Task TwoXx_ExceptNoContent_HaveBody(int code)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, $"/status/{code}");

        Assert.Equal(code, (int)response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        // All these status codes have "ok" body from the /status route
        Assert.NotEmpty(body);
    }

    // ── Theory: 3xx redirect codes have small body ────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "IT-11-117: 3xx status codes are decoded without error")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task ThreeXx_DecodedWithoutError(int code)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, $"/status/{code}");

        Assert.Equal(code, (int)response.StatusCode);
        // No exception = decoded successfully
    }

    // ── Theory: 5xx status codes decoded without error ────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "IT-11-118: 5xx status codes are decoded without error")]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task FiveXx_DecodedWithoutError(int code)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, $"/status/{code}");

        Assert.Equal(code, (int)response.StatusCode);
    }

    // ── Sequential 4xx responses on keep-alive connection ─────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11-119: Sequential 4xx responses on keep-alive connection all decoded")]
    public async Task Sequential_4xxResponses_KeepAlive_AllDecoded()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        foreach (var code in new[] { 400, 401, 403, 404, 500 })
        {
            var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                Http11Helper.BuildUri(_fixture.Port, $"/status/{code}")));
            Assert.Equal(code, (int)r.StatusCode);
        }
    }
}
