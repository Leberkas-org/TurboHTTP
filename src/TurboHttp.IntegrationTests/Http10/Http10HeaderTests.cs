using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Phase 12 — HTTP/1.0 Integration Tests: Header parsing and forwarding scenarios.
/// </summary>
[Collection("Http10Integration")]
public sealed class Http10HeaderTests
{
    private readonly KestrelFixture _fixture;

    public Http10HeaderTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── X-* header echo ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-040: GET /headers/echo with X-Test header — echoed in response")]
    public async Task Get_HeadersEcho_SingleXHeader_IsEchoedInResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{_fixture.Port}/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-Test", "my-value");

        var response = await Http10Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Test", out var vals));
        Assert.Contains("my-value", vals);
    }

    [Fact(DisplayName = "IT-10-041: GET /headers/echo with multiple X-* headers — all echoed")]
    public async Task Get_HeadersEcho_MultipleXHeaders_AllEchoedInResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{_fixture.Port}/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-First", "alpha");
        request.Headers.TryAddWithoutValidation("X-Second", "beta");
        request.Headers.TryAddWithoutValidation("X-Third", "gamma");

        var response = await Http10Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-First", out var v1));
        Assert.Contains("alpha", v1);
        Assert.True(response.Headers.TryGetValues("X-Second", out var v2));
        Assert.Contains("beta", v2);
        Assert.True(response.Headers.TryGetValues("X-Third", out var v3));
        Assert.Contains("gamma", v3);
    }

    [Fact(DisplayName = "IT-10-042: GET /headers/echo header value with ASCII printable chars is preserved")]
    public async Task Get_HeadersEcho_AsciiHeaderValue_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{_fixture.Port}/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-Custom", "cafe-au-lait");

        var response = await Http10Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom", out var vals));
        Assert.Contains("cafe-au-lait", vals);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-043: GET /auth without Authorization header returns 401")]
    public async Task Get_Auth_WithoutAuthorization_Returns401()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/auth");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "IT-10-044: GET /auth with valid Authorization header returns 200")]
    public async Task Get_Auth_WithAuthorization_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{_fixture.Port}/auth"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test-token");

        var response = await Http10Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Response header metadata ───────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-045: GET /hello response has Server header present")]
    public async Task Get_Hello_HasServerHeader()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        // Kestrel always emits a Server header
        Assert.True(response.Headers.Contains("Server"), "Server header should be present");
    }

    [Fact(DisplayName = "IT-10-046: GET /hello response has Date header with a parseable value")]
    public async Task Get_Hello_DateHeader_HasValidFormat()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        Assert.True(response.Headers.Contains("Date"), "Date header must be present");
        var dateValue = response.Headers.Date;
        Assert.NotNull(dateValue);
    }

    [Fact(DisplayName = "IT-10-047: GET /hello response Content-Type is text/plain")]
    public async Task Get_Hello_ContentType_IsTextPlain()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        var ct = response.Content.Headers.ContentType;
        Assert.NotNull(ct);
        Assert.Equal("text/plain", ct.MediaType);
    }

    // ── Custom response headers via /headers/set ──────────────────────────────

    [Fact(DisplayName = "IT-10-048: GET /headers/set?Foo=Bar sets Foo: Bar in response")]
    public async Task Get_HeadersSet_SetsCustomResponseHeader()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/headers/set?Foo=Bar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Foo", out var vals));
        Assert.Contains("Bar", vals);
    }

    [Fact(DisplayName = "IT-10-049: GET /headers/set?A=1&B=2 sets both A and B response headers")]
    public async Task Get_HeadersSet_SetsMultipleCustomResponseHeaders()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/headers/set?A=1&B=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("A", out var vA));
        Assert.Contains("1", vA);
        Assert.True(response.Headers.TryGetValues("B", out var vB));
        Assert.Contains("2", vB);
    }

    // ── Multiple values for same header name ──────────────────────────────────

    [Fact(DisplayName = "IT-10-050: GET /multiheader response has two X-Value entries, both accessible")]
    public async Task Get_MultiHeader_TwoXValueEntries_BothAccessible()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/multiheader");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Value", out var vals));
        var valueList = vals.ToList();
        Assert.Contains("alpha", valueList);
        Assert.Contains("beta", valueList);
    }

    // ── Header name case-insensitivity ────────────────────────────────────────

    [Fact(DisplayName = "IT-10-051: Response header Content-Length accessible regardless of case")]
    public async Task Get_Hello_ContentLength_CaseInsensitiveAccess()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        // .NET's HttpResponseMessage stores headers in a case-insensitive dictionary
        var contentLength = response.Content.Headers.ContentLength;
        Assert.NotNull(contentLength);
        Assert.Equal(11L, contentLength);
    }

    // ── Content-Length correctness vs actual body ─────────────────────────────

    [Fact(DisplayName = "IT-10-052: GET /hello Content-Length matches actual body bytes returned")]
    public async Task Get_Hello_ContentLength_MatchesActualBodyLength()
    {
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        var body = await response.Content.ReadAsByteArrayAsync();
        var reported = response.Content.Headers.ContentLength;

        Assert.Equal(body.Length, (int)(reported ?? 0));
    }
}
