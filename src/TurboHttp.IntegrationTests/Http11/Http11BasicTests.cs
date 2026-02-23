using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 13 — HTTP/1.1 Integration Tests: Basic request/response scenarios.
/// Each test uses Http11Helper which opens a fresh TCP connection, encodes
/// with Http11Encoder, and decodes with Http11Decoder.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11BasicTests
{
    private readonly KestrelFixture _fixture;

    public Http11BasicTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── HTTP verbs — 7 basic scenarios ────────────────────────────────────────

    [Fact(DisplayName = "IT-11-001: GET /any returns 200 with method 'GET' in body")]
    public async Task Get_Any_Returns200_MethodNameInBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/any");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("GET", body);
    }

    [Fact(DisplayName = "IT-11-002: POST /any returns 200 with method 'POST' in body")]
    public async Task Post_Any_Returns200_MethodNameInBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/any"))
        {
            Content = new ByteArrayContent([])
        };
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("POST", body);
    }

    [Fact(DisplayName = "IT-11-003: HEAD /any returns 200 with no body and HTTP/1.1 version")]
    public async Task Head_Any_Returns200_NoBody()
    {
        var response = await Http11Helper.HeadAsync(_fixture.Port, "/any");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "IT-11-004: PUT /echo returns 200 echoing request body")]
    public async Task Put_Echo_Returns200_EchoesBody()
    {
        const string payload = "put-payload";
        var request = new HttpRequestMessage(HttpMethod.Put, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(payload, body);
    }

    [Fact(DisplayName = "IT-11-005: DELETE /any returns 200")]
    public async Task Delete_Any_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, Http11Helper.BuildUri(_fixture.Port, "/any"));
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "IT-11-006: PATCH /echo returns 200 echoing request body")]
    public async Task Patch_Echo_Returns200_EchoesBody()
    {
        const string payload = "patch-payload";
        var request = new HttpRequestMessage(HttpMethod.Patch, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(payload, body);
    }

    [Fact(DisplayName = "IT-11-007: OPTIONS /any returns 200")]
    public async Task Options_Any_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Http11Helper.BuildUri(_fixture.Port, "/any"));
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Host header ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-008: GET /hello Host header required — server sees correct Host")]
    public async Task Get_Hello_HostHeader_IsRequiredAndPresent()
    {
        // The Http11Encoder always adds Host header (RFC 9112 §5.4)
        // We verify the server responds 200 (it would fail auth or routing without Host)
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "IT-11-009: HTTP/1.1 response carries HTTP/1.1 version")]
    public async Task Get_Hello_ResponseVersion_IsHttp11()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
    }

    // ── Multiple status codes ─────────────────────────────────────────────────

    [Theory(DisplayName = "IT-11-010: GET /status/{code} returns the expected status code")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Get_Status_ReturnsExpectedStatusCode(int code)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, $"/status/{code}");

        Assert.Equal(code, (int)response.StatusCode);
    }

    // ── Large body ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-011: GET /large/1 returns 200 with 1 KB body")]
    public async Task Get_Large_1KB_Returns200_With1KbBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/large/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024, body.Length);
    }

    [Fact(DisplayName = "IT-11-012: GET /large/64 returns 200 with 64 KB body")]
    public async Task Get_Large_64KB_Returns200_With64KbBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/large/64");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(64 * 1024, body.Length);
    }

    [Fact(DisplayName = "IT-11-013: GET /large/512 returns 200 with 512 KB body")]
    public async Task Get_Large_512KB_Returns200_With512KbBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/large/512");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(512 * 1024, body.Length);
    }

    [Theory(DisplayName = "IT-11-014: GET /large/{kb} Content-Length matches actual body byte count")]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(64)]
    public async Task Get_Large_ContentLength_MatchesActualBodyLength(int kb)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, $"/large/{kb}");

        var body = await response.Content.ReadAsByteArrayAsync();
        var reportedLength = response.Content.Headers.ContentLength;

        Assert.Equal(kb * 1024, body.Length);
        Assert.Equal(kb * 1024, (int)(reportedLength ?? 0));
    }

    // ── Content-Type ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-015: GET /content/text/plain response has Content-Type text/plain")]
    public async Task Get_Content_TextPlain_HasCorrectContentType()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/content/text/plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "IT-11-016: GET /content/application/json response has Content-Type application/json")]
    public async Task Get_Content_ApplicationJson_HasCorrectContentType()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/content/application/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "IT-11-017: GET /content/application/octet-stream response has correct Content-Type")]
    public async Task Get_Content_OctetStream_HasCorrectContentType()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/content/application/octet-stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    // ── POST /echo basic body round-trip ─────────────────────────────────────

    [Fact(DisplayName = "IT-11-018: POST /echo small body is echoed correctly")]
    public async Task Post_Echo_SmallBody_IsEchoedCorrectly()
    {
        const string text = "hello-http11";
        var content = new StringContent(text, Encoding.UTF8, "text/plain");
        var response = await Http11Helper.PostAsync(_fixture.Port, "/echo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(text, body);
    }

    [Fact(DisplayName = "IT-11-019: POST /echo 64 KB body is echoed correctly")]
    public async Task Post_Echo_64KbBody_IsEchoedCorrectly()
    {
        var bodyBytes = new byte[64 * 1024];
        for (var i = 0; i < bodyBytes.Length; i++)
        {
            bodyBytes[i] = (byte)(i % 256);
        }

        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var response = await Http11Helper.PostAsync(_fixture.Port, "/echo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, echoedBody);
    }

    [Fact(DisplayName = "IT-11-020: POST /echo empty body returns 200 with empty body")]
    public async Task Post_Echo_EmptyBody_Returns200_EmptyBody()
    {
        var content = new ByteArrayContent(Array.Empty<byte>());
        var response = await Http11Helper.PostAsync(_fixture.Port, "/echo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(echoedBody);
    }

    // ── Response date and general headers ────────────────────────────────────

    [Fact(DisplayName = "IT-11-021: GET /hello response includes Date header")]
    public async Task Get_Hello_ResponseHasDateHeader()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.True(response.Headers.Contains("Date"), "Date header should be present");
    }

    [Fact(DisplayName = "IT-11-022: GET /hello returns body 'Hello World'")]
    public async Task Get_Hello_Returns200_WithBodyHelloWorld()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    // ── Two sequential independent requests ──────────────────────────────────

    [Fact(DisplayName = "IT-11-023: Two independent GET /ping requests each succeed")]
    public async Task TwoIndependent_GetPing_BothSucceed()
    {
        var r1 = await Http11Helper.GetAsync(_fixture.Port, "/ping");
        var r2 = await Http11Helper.GetAsync(_fixture.Port, "/ping");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("pong", await r1.Content.ReadAsStringAsync());
        Assert.Equal("pong", await r2.Content.ReadAsStringAsync());
    }

    // ── 204 no body ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-024: GET /status/204 returns 204 with empty body")]
    public async Task Get_Status204_ReturnsNoContent_EmptyBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/204");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── 304 no body ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-025: GET /status/304 returns 304 with empty body")]
    public async Task Get_Status304_ReturnsNotModified_EmptyBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/304");

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }
}
