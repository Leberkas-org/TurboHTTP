using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RedirectSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSpec(ServerFixture server, ActorSystemFixture systemFixture)
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

    [Fact(Timeout = 20000)]
    public async Task Get_301_redirect_should_follow_to_hello_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/301/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_302_redirect_should_follow_to_hello_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_307_redirect_should_follow_to_hello_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/307/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_308_redirect_should_follow_to_hello_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/308/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 5000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Redirect_chain_should_follow_n_hops_to_hello_over_https(int hops)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/redirect/chain/{hops}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Infinite_redirect_loop_should_return_final_redirect_response_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Relative_location_header_should_be_resolved_to_hello_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/relative");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Cross_scheme_downgrade_should_be_blocked_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Post_307_should_preserve_method_and_body_to_echo_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Post_303_should_rewrite_to_get_at_hello_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Post_302_should_rewrite_to_get_at_hello_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Post_308_should_preserve_method_and_body_to_echo_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Cross_origin_redirect_should_be_blocked_as_downgrade_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Cross_origin_auth_redirect_should_be_blocked_as_downgrade_over_https()
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
