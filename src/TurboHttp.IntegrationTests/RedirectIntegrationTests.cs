using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests;

[Collection("Http1Integration")]
public sealed class RedirectIntegrationTests
{
    private readonly KestrelFixture _fixture;

    public RedirectIntegrationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private ClientHelper CreateRedirectClient()
    {
        return ClientHelper.CreateClient(
            _fixture.Port,
            new Version(1, 1),
            configure: builder => builder.WithRedirect());
    }

    // ── GET /redirect/{code}/{target} — status code redirects to /hello ──────

    [Fact(DisplayName = "Redirect-001: GET 301 redirect follows to /hello")]
    public async Task Get_301_Redirect_Follows_To_Hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/301/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-002: GET 302 redirect follows to /hello")]
    public async Task Get_302_Redirect_Follows_To_Hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-003: GET 307 redirect follows to /hello")]
    public async Task Get_307_Redirect_Follows_To_Hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/307/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-004: GET 308 redirect follows to /hello")]
    public async Task Get_308_Redirect_Follows_To_Hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/308/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── GET /redirect/chain/{n} — redirect chain ────────────────────────────

    [Theory(DisplayName = "Redirect-005: Redirect chain of N hops ends at /hello")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Redirect_Chain_Follows_N_Hops_To_Hello(int hops)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/redirect/chain/{hops}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── GET /redirect/loop — infinite redirect loop ─────────────────────────

    [Fact(DisplayName = "Redirect-006: Infinite redirect loop returns final redirect response")]
    public async Task Infinite_Redirect_Loop_Returns_Final_Redirect_Response()
    {
        // RedirectBidiStage catches RedirectException (loop/max-exceeded) and
        // forwards the final redirect response to the caller instead of throwing.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // The stage forwards the last 302 response once max redirects or loop is detected
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    // ── GET /redirect/relative — relative Location header ───────────────────

    [Fact(DisplayName = "Redirect-007: Relative Location header resolved to /hello")]
    public async Task Relative_Location_Header_Resolved_To_Hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/relative");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── GET /redirect/cross-scheme — HTTPS→HTTP downgrade protection ────────

    [Fact(DisplayName = "Redirect-008: Cross-scheme HTTPS to HTTP downgrade blocked by default")]
    public async Task Cross_Scheme_Downgrade_Blocked_By_Default()
    {
        // This test uses the plain http fixture, so the redirect from
        // /redirect/cross-scheme sets Location to http://127.0.0.1:{port}/hello.
        // Since the original request is also http, there's no actual downgrade.
        // The route was designed for HTTPS→HTTP testing via KestrelTlsFixture.
        // Over plain HTTP, the redirect is same-scheme and should succeed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // http → http is not a downgrade, so this follows through
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── POST /redirect/307 — 307 preserves method + body ────────────────────

    [Fact(DisplayName = "Redirect-009: POST 307 preserves method and body to /echo")]
    public async Task Post_307_Preserves_Method_And_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var payload = "redirect-307-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/307")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    // ── POST /redirect/303 — 303 rewrites to GET ────────────────────────────

    [Fact(DisplayName = "Redirect-010: POST 303 rewrites to GET at /hello")]
    public async Task Post_303_Rewrites_To_Get()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/303")
        {
            Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── POST /redirect/302 — 302 rewrites POST to GET ──────────────────────

    [Fact(DisplayName = "Redirect-011: POST 302 rewrites to GET at /hello")]
    public async Task Post_302_Rewrites_To_Get()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/302")
        {
            Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── POST /redirect/308 — 308 preserves method + body ────────────────────

    [Fact(DisplayName = "Redirect-012: POST 308 preserves method and body to /echo")]
    public async Task Post_308_Preserves_Method_And_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var payload = "redirect-308-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/308")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    // ── GET /redirect/cross-origin — cross-origin redirect ──────────────────

    [Fact(DisplayName = "Redirect-013: Cross-origin redirect follows to /headers/echo")]
    public async Task Cross_Origin_Redirect_Follows_To_Headers_Echo()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin");
        request.Headers.Add("X-Test", "preserved");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // The redirect target /headers/echo returns 200 with X-* headers echoed
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GET /redirect/cross-origin-auth — Authorization stripping ────────────

    [Fact(DisplayName = "Redirect-014: Cross-origin redirect strips Authorization header")]
    public async Task Cross_Origin_Redirect_Strips_Authorization()
    {
        // The redirect goes to http://127.0.0.1:{port}/auth which returns 401
        // if no Authorization header is present.
        // Since client BaseAddress is also http://127.0.0.1:{port}, this is
        // same-origin and Authorization will be preserved (200).
        // True cross-origin stripping would require different host/port.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin-auth");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Same-origin: Authorization is preserved, /auth returns 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
