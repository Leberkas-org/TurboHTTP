using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class DynamicPortSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Host.UseTurboHttp(options =>
        {
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);
        });

        _app = builder.Build();
        _app.MapGet("/ping", () => Results.Content("pong", "text/plain"));
        await _app.StartAsync();
        _client = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public void Address_feature_should_report_non_zero_port()
    {
        var addresses = _app!.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        Assert.Single(addresses);
        var uri = new Uri(addresses[0]);
        Assert.NotEqual(0, uri.Port);
    }

    [Fact(Timeout = 10000)]
    public async Task Request_to_dynamic_port_should_succeed()
    {
        var addresses = _app!.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        var baseUri = new Uri(addresses[0]);
        var response = await _client!.GetAsync(new Uri(baseUri, "/ping"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("pong", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Multiple_requests_to_dynamic_port_should_all_succeed()
    {
        var addresses = _app!.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        var baseUri = new Uri(addresses[0]);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client!.GetAsync(new Uri(baseUri, "/ping"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
