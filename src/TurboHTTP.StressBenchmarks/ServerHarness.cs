using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Server;

namespace TurboHTTP.StressBenchmarks;

public sealed class ServerHarness : IAsyncDisposable
{
    private WebApplication? _app;

    public Uri? BaseUri { get; private set; }

    public async Task StartAsync(ServerType serverType, Action<WebApplication> configureRoutes)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        if (serverType == ServerType.Turbo)
        {
            builder.Host.UseTurboHttp(options =>
            {
                options.Listen(IPAddress.Loopback, 0, lo =>
                    lo.Protocols = HttpProtocols.Http1);
            });
        }
        else
        {
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });
        }

        var app = builder.Build();
        configureRoutes(app);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        BaseUri = new Uri(addresses[0]);
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
