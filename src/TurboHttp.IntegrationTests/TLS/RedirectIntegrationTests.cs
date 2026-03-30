using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RedirectIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateRedirectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Redirect-TLS-001: GET 301 redirect follows to /hello over HTTPS")]
    public async Task Get_301_Redirect_Follows_To_Hello_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/301/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-TLS-002: GET 302 redirect follows to /hello over HTTPS")]
    public async Task Get_302_Redirect_Follows_To_Hello_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-TLS-003: GET 307 redirect follows to /hello over HTTPS")]
    public async Task Get_307_Redirect_Follows_To_Hello_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/307/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-TLS-004: GET 308 redirect follows to /hello over HTTPS")]
    public async Task Get_308_Redirect_Follows_To_Hello_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/308/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Theory(DisplayName = "Redirect-TLS-005: Redirect chain of N hops ends at /hello over HTTPS")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Redirect_Chain_Follows_N_Hops_To_Hello_Over_Https(int hops)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/redirect/chain/{hops}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-TLS-006: Infinite redirect loop returns final redirect response over HTTPS")]
    public async Task Infinite_Redirect_Loop_Returns_Final_Redirect_Response_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(DisplayName = "Redirect-TLS-007: Relative Location header resolved to /hello over HTTPS")]
    public async Task Relative_Location_Header_Resolved_To_Hello_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/relative");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "Redirect-TLS-008: Cross-scheme HTTPS to HTTP downgrade blocked by default")]
    public async Task Cross_Scheme_Downgrade_Blocked_Over_Https()
    {
        // Over HTTPS, the /redirect/cross-scheme route sets Location to http://{host}/hello.
        // This is an HTTPS→HTTP downgrade. The redirect stage should block this by default
        // and return the redirect response (3xx) rather than following it.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // HTTPS→HTTP is a downgrade — stage returns the redirect response
        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }

    [Fact(DisplayName = "Redirect-TLS-009: POST 307 preserves method and body to /echo over HTTPS")]
    public async Task Post_307_Preserves_Method_And_Body_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var payload = "redirect-tls-307-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/307")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(DisplayName = "Redirect-TLS-010: POST 303 rewrites to GET at /hello over HTTPS")]
    public async Task Post_303_Rewrites_To_Get_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Fact(DisplayName = "Redirect-TLS-011: POST 302 rewrites to GET at /hello over HTTPS")]
    public async Task Post_302_Rewrites_To_Get_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Fact(DisplayName = "Redirect-TLS-012: POST 308 preserves method and body to /echo over HTTPS")]
    public async Task Post_308_Preserves_Method_And_Body_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var payload = "redirect-tls-308-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "/redirect/308")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(DisplayName = "Redirect-TLS-013: Cross-origin redirect to http:// is blocked as downgrade over HTTPS")]
    public async Task Cross_Origin_Redirect_Blocked_As_Downgrade_Over_Https()
    {
        // /redirect/cross-origin redirects to http://127.0.0.1:{port}/headers/echo.
        // Over HTTPS this is an HTTPS→HTTP downgrade — the redirect stage returns the
        // 3xx response without following it.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin");
        request.Headers.Add("X-Test", "preserved");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }

    [Fact(DisplayName = "Redirect-TLS-014: Cross-origin auth redirect to http:// is blocked as downgrade over HTTPS")]
    public async Task Cross_Origin_Auth_Redirect_Blocked_As_Downgrade_Over_Https()
    {
        // /redirect/cross-origin-auth redirects to http://127.0.0.1:{port}/auth.
        // Over HTTPS this is an HTTPS→HTTP downgrade — the redirect stage returns the
        // 3xx response without following it, regardless of the Authorization header.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin-auth");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }
}
