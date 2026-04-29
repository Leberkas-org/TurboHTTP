using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class RedirectSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RedirectSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private ClientHelper CreateRedirectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: builder => builder.WithRedirect(),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_301_should_follow_redirect_to_hello()
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
    public async Task Get_302_should_follow_redirect_to_hello()
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
    public async Task Get_303_should_follow_redirect_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/303/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_307_should_follow_redirect_to_hello()
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
    public async Task Get_308_should_follow_redirect_to_hello()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/308/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_303_should_rewrite_to_get()
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
    public async Task Post_302_should_rewrite_to_get()
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
    public async Task Post_307_should_preserve_method_and_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Fact(Timeout = 20000)]
    public async Task Post_308_should_preserve_method_and_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Theory(Timeout = 20000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Redirect_chain_should_follow_N_hops_to_hello(int hops)
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
    public async Task Infinite_redirect_loop_should_return_final_redirect_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRedirectClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/loop");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
