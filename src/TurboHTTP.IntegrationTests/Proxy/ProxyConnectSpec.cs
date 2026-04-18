using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.Proxy;

[Collection("Proxy")]
[Obsolete("Replaced by StreamTests.Acceptance.Proxy.ProxyConnectSpec")]
public sealed class ProxyConnectSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ProxyServer? _proxy;

    public ProxyConnectSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        _proxy = ProxyServer.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_proxy is not null)
        {
            await _proxy.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_tunnel_https_request_via_connect()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy!.Port}");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
        Assert.True(_proxy!.ConnectRequestCount >= 1, "Proxy should have received a CONNECT request");
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_send_proxy_authorization_when_credentials_set()
    {
        _proxy!.RequiredCredentials = new NetworkCredential("proxyuser", "proxypass");

        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy.Port}")
                {
                    Credentials = new NetworkCredential("proxyuser", "proxypass")
                };
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_proxy.LastProxyAuthHeader);
        Assert.StartsWith("Basic ", _proxy.LastProxyAuthHeader);
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_send_default_proxy_credentials_when_set()
    {
        _proxy!.RequiredCredentials = new NetworkCredential("defuser", "defpass");

        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy.Port}");
                opts.DefaultProxyCredentials = new NetworkCredential("defuser", "defpass");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_proxy.LastProxyAuthHeader);
        Assert.StartsWith("Basic ", _proxy.LastProxyAuthHeader);
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_bypass_when_use_proxy_is_false()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = false;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy!.Port}");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        // Request should succeed directly (not through proxy)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _proxy!.ConnectRequestCount);
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_bypass_when_proxy_is_null()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = null;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _proxy!.ConnectRequestCount);
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_work_with_preauthenticate_through_tunnel()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy!.Port}");
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_proxy!.ConnectRequestCount >= 1);
    }
}
