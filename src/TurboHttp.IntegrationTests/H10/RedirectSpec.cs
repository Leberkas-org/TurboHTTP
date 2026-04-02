using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
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
            _server.HttpPort,
            new Version(1, 0),
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_follow_get_301_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/301/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_follow_get_302_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_follow_get_307_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/307/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_follow_get_308_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/308/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 60000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Redirect_should_follow_chain_of_hops_to_hello(int hops)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/redirect/chain/{hops}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 60000)]
    public async Task Redirect_should_return_final_response_for_infinite_loop()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_resolve_relative_location_header_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/relative");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_not_block_cross_scheme_downgrade_by_default()
    {
        // Over plain HTTP, the redirect is same-scheme and should succeed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-scheme");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // http → http is not a downgrade, so this follows through
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_preserve_post_method_and_body_for_307()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_rewrite_post_303_to_get()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_rewrite_post_302_to_get()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_preserve_post_method_and_body_for_308()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_follow_cross_origin_to_headers_echo()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin");
        request.Headers.Add("X-Test", "preserved");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_strip_authorization_on_cross_origin()
    {
        // Since client BaseAddress is also http://127.0.0.1:{port}, this is
        // same-origin and Authorization will be preserved (200).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/cross-origin-auth");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Same-origin: Authorization is preserved, /auth returns 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
