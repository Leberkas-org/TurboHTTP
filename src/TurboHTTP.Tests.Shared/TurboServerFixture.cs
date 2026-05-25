using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using Xunit;

namespace TurboHTTP.Tests.Shared;

public class TurboServerFixture : IAsyncLifetime
{
    private readonly Action<TurboWebApplication> _configureRoutes;
    private TurboWebApplication? _app;

    public TurboServerFixture(Action<TurboWebApplication> configureRoutes)
    {
        _configureRoutes = configureRoutes;
    }

    public Uri HttpBaseAddress { get; private set; } = null!;
    public int HttpPort { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var port = GetFreePort();
        var builder = TurboWebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Server.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = (ushort)port });

        _app = builder.Build();
        _configureRoutes(_app);

        await _app.StartAsync();

        HttpPort = port;
        HttpBaseAddress = new Uri($"http://127.0.0.1:{port}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
