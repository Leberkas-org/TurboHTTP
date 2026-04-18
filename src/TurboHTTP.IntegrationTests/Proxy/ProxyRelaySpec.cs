using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.Proxy;

[Collection("Proxy")]
[Obsolete("Replaced by StreamTests.Acceptance.Proxy.ProxyRelaySpec")]
public sealed class ProxyRelaySpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ProxyServer? _proxy;

    public ProxyRelaySpec(ServerFixture server, ActorSystemFixture systemFixture)
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
    public async Task Proxy_should_relay_plain_http_request()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
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
        Assert.True(_proxy!.RelayRequestCount >= 1, "Proxy should have relayed a request");
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_relay_multiple_requests_on_same_connection()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = true;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy!.Port}");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            var response = await helper.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Connection pooling means subsequent requests reuse the relay stream,
        // so only the first request triggers a new proxy relay.
        Assert.True(_proxy!.RelayRequestCount >= 1);
    }

    [Fact(Timeout = 30000)]
    public async Task Proxy_should_bypass_for_plain_http_when_use_proxy_false()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.UseProxy = false;
                opts.Proxy = new WebProxy($"http://127.0.0.1:{_proxy!.Port}");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _proxy!.RelayRequestCount);
    }
}
