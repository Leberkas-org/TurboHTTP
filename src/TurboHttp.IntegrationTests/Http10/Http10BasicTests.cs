using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Phase 12 — HTTP/1.0 Integration Tests: Basic GET/HEAD scenarios.
/// Each test opens a fresh TCP connection, encodes the request with Http10Encoder,
/// and decodes the response with Http10Decoder.
/// </summary>
[Collection("Http10Integration")]
public sealed class Http10BasicTests
{
    private readonly KestrelFixture _fixture;

    public Http10BasicTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── GET /hello ─────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-001: GET /hello returns 200 with body 'Hello World'")]
    public async Task Get_Hello_Returns200_WithBodyHelloWorld()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-002: GET /hello response has Date header and correct Content-Length")]
    public async Task Get_Hello_HasDateHeader_AndCorrectContentLength()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        Assert.True(response.Headers.Contains("Date"), "Date header should be present");
        var contentLength = response.Content.Headers.ContentLength;
        Assert.Equal(11L, contentLength); // "Hello World" = 11 bytes
    }

    // ── GET /large ─────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-003: GET /large/1 returns 200 with 1 KB body")]
    public async Task Get_Large_1KB_Returns200_With1KbBody()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/large/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-004: GET /large/64 returns 200 with 64 KB body")]
    public async Task Get_Large_64KB_Returns200_With64KbBody()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/large/64");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(64 * 1024, body.Length);
    }

    // ── GET /status/* ──────────────────────────────────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "IT-10-005: GET /status/{code} returns the expected status code")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Get_Status_ReturnsExpectedStatusCode(int code)
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, $"/status/{code}");

        Assert.Equal(code, (int)response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-006: GET /status/204 returns 204 with empty body")]
    public async Task Get_Status204_ReturnsNoContent()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/204");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-007: GET /status/301 returns 301 redirect status")]
    public async Task Get_Status301_ReturnsMovedPermanently()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/status/301");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    // ── GET /ping ─────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-008: GET /ping returns 200 with body 'pong'")]
    public async Task Get_Ping_Returns200_WithBodyPong()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("pong", body);
    }

    // ── GET /content/* ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-009: GET /content/text/html response has Content-Type text/html")]
    public async Task Get_Content_TextHtml_HasCorrectContentType()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/content/text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/html", ct);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-10-010: GET /content/application/json response has Content-Type application/json")]
    public async Task Get_Content_ApplicationJson_HasCorrectContentType()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/content/application/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", ct);
    }

    // ── HEAD /hello ───────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-011: HEAD /hello returns 200 with no body and Content-Length present")]
    public async Task Head_Hello_Returns200_NoBody_WithContentLength()
    {
        var response = await Http10Helper.HeadAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);

        // Content-Length must be present in the response headers (it represents the GET body size)
        Assert.NotNull(response.Content.Headers.ContentLength);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    // ── GET /methods ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-012: GET /methods returns body equal to 'GET'")]
    public async Task Get_Methods_Returns_GET()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/methods");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("GET", body);
    }

    // ── Repeated requests (each needs a new TCP connection) ──────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-10-013: Two sequential GET /ping requests each succeed independently")]
    public async Task TwoSequential_Get_Ping_BothSucceed()
    {
        var r1 = await Http10Helper.GetAsync(_fixture.Port, "/ping");
        var r2 = await Http10Helper.GetAsync(_fixture.Port, "/ping");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var b1 = await r1.Content.ReadAsStringAsync();
        var b2 = await r2.Content.ReadAsStringAsync();
        Assert.Equal("pong", b1);
        Assert.Equal("pong", b2);
    }

    // ── Content-Length accuracy ───────────────────────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "IT-10-014: GET /large/{kb} Content-Length header matches actual body byte count")]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public async Task Get_Large_ContentLength_MatchesActualBodyLength(int kb)
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, $"/large/{kb}");

        var body = await response.Content.ReadAsByteArrayAsync();
        var reportedLength = response.Content.Headers.ContentLength;

        Assert.Equal(kb * 1024, body.Length);
        Assert.Equal(kb * 1024, (int)(reportedLength ?? 0));
    }
}
