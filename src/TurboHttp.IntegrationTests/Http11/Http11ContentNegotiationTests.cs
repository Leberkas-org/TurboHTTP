using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 14 — HTTP/1.1 Integration Tests: Content negotiation.
/// Tests cover Accept, Accept-Charset, Accept-Language, Content-Type variants,
/// Content-Encoding metadata, and Vary header handling.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11ContentNegotiationTests
{
    private readonly KestrelFixture _fixture;

    public Http11ContentNegotiationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Accept: application/json ──────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-001: Accept: application/json — server returns Content-Type application/json")]
    public async Task Accept_ApplicationJson_ServerReturnsJsonContentType()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/negotiate"));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", ct);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);
    }

    // ── Accept: text/html ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-002: Accept: text/html — server returns Content-Type text/html")]
    public async Task Accept_TextHtml_ServerReturnsHtmlContentType()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/negotiate"));
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/html", ct);
    }

    // ── Accept: */* ───────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-003: Accept: */* — server returns default Content-Type")]
    public async Task Accept_Wildcard_ServerReturnsDefaultContentType()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/negotiate"));
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Server default is text/plain when no specific match
        Assert.NotNull(response.Content.Headers.ContentType);
    }

    // ── Accept with quality values ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-004: Accept with q-values (text/html;q=0.9,application/json;q=1.0) — highest q matched")]
    public async Task Accept_WithQValues_HighestQMatched()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/negotiate"));
        request.Headers.TryAddWithoutValidation("Accept", "text/html;q=0.9,application/json;q=1.0");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Our server does simple contains check — application/json should win
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", ct);
    }

    // ── Accept-Charset header ─────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-005: Accept-Charset: utf-8 header sent in request without error")]
    public async Task AcceptCharset_Utf8_SentWithoutError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("Accept-Charset", "utf-8");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-006: Accept-Charset: iso-8859-1,utf-8 multi-value sent without error")]
    public async Task AcceptCharset_MultiValue_SentWithoutError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("Accept-Charset", "iso-8859-1, utf-8;q=0.9");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Accept-Language header ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-007: Accept-Language: en-US sent in request without error")]
    public async Task AcceptLanguage_EnUS_SentWithoutError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-008: Accept-Language: fr,en;q=0.8 multi-value sent without error")]
    public async Task AcceptLanguage_MultiValue_SentWithoutError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("Accept-Language", "fr, en;q=0.8");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Content-Type: multipart/form-data ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-009: Content-Type: multipart/form-data — server parses body successfully")]
    public async Task ContentType_MultipartFormData_ServerParsesBody()
    {
        const string boundary = "----WebKitFormBoundary7MA4YWxkTrZu0gW";
        var multipartBody =
            $"------WebKitFormBoundary7MA4YWxkTrZu0gW\r\n" +
            $"Content-Disposition: form-data; name=\"field1\"\r\n\r\n" +
            $"value1\r\n" +
            $"------WebKitFormBoundary7MA4YWxkTrZu0gW--\r\n";

        var bodyBytes = Encoding.UTF8.GetBytes(multipartBody);
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/form/multipart"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("received:", responseBody);
    }

    // ── Content-Type: application/x-www-form-urlencoded ──────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-010: Content-Type: application/x-www-form-urlencoded — server parses body")]
    public async Task ContentType_UrlEncoded_ServerParsesBody()
    {
        var bodyBytes = Encoding.UTF8.GetBytes("field1=value1&field2=value2");
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/form/urlencoded"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("received:", responseBody);
    }

    // ── Content-Encoding: identity (default) ─────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-011: Default response has no Content-Encoding or Content-Encoding: identity")]
    public async Task DefaultResponse_NoContentEncoding_OrIdentity()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Default responses from Kestrel have no Content-Encoding or identity (not gzip/deflate)
        var ce = response.Content.Headers.ContentEncoding;
        Assert.True(ce.Count == 0 || ce.Contains("identity"),
            $"Expected no content encoding or identity, got: {string.Join(",", ce)}");
    }

    // ── Request with Content-Encoding header ─────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-012: Request with Content-Encoding: identity header accepted by server")]
    public async Task Request_ContentEncodingIdentity_AcceptedByServer()
    {
        var bodyBytes = Encoding.UTF8.GetBytes("identity-encoded-body");
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation("Content-Encoding", "identity");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        // Server accepts request; response is 200 echoing body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Response Content-Encoding: metadata only ─────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-013: GET /gzip-meta — response Content-Encoding header present in decoded response")]
    public async Task Get_GzipMeta_ContentEncodingHeaderPresent()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/gzip-meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.ContentEncoding.Count > 0,
            "Content-Encoding header should be present");
    }

    // ── Vary: Accept header ───────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-014: GET /negotiate/vary — response Vary header contains Accept")]
    public async Task Get_NegotiateVary_VaryHeaderContainsAccept()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/negotiate/vary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Vary.Contains("Accept"),
            $"Expected Vary header to contain 'Accept', got: {string.Join(",", response.Headers.Vary)}");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-015: Accept and Accept-Language both in same request — server returns 200")]
    public async Task AcceptAndAcceptLanguage_BothInRequest_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/negotiate"));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,fr;q=0.8");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", ct);
    }
}
