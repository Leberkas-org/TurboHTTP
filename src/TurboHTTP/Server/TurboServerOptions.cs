using System.Net;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp.Listener;
using Servus.Akka.Transport.Quic.Listener;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboServerOptions
{
    public int MaxConcurrentConnections { get; set; }
    public int MaxConcurrentUpgradedConnections { get; set; }

    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int BodyBufferThreshold { get; set; } = 65536;
    public TimeSpan BodyConsumptionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int ResponseBodyChunkSize { get; set; } = 16384;

    public Http1ServerOptions Http1 { get; } = new();
    public Http2ServerOptions Http2 { get; } = new();
    public Http3ServerOptions Http3 { get; } = new();

    public IList<ListenerBinding> Endpoints { get; } = new List<ListenerBinding>();

    public void Bind(TcpListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new TcpListenerFactory() });
    }

    public void Bind(QuicListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new QuicListenerFactory() });
    }

    public void BindTcp(string host, ushort port) => Bind(new TcpListenerOptions() { Host = host, Port = port });

    internal IList<TurboListenOptions> ListenOptions { get; } = new List<TurboListenOptions>();
    internal Action<TurboHttpsOptions>? HttpsDefaultsCallback { get; private set; }

    public IList<string> Urls { get; } = new List<string>();

    public void ConfigureHttpsDefaults(Action<TurboHttpsOptions> configure)
    {
        HttpsDefaultsCallback = configure;
    }

    public void Listen(IPAddress address, ushort port)
    {
        var listenOptions = new TurboListenOptions(address, port);
        ListenOptions.Add(listenOptions);
    }

    public void Listen(IPAddress address, ushort port, Action<TurboListenOptions> configure)
    {
        var listenOptions = new TurboListenOptions(address, port);
        configure(listenOptions);
        ListenOptions.Add(listenOptions);
    }

    public void ListenLocalhost(ushort port)
    {
        Listen(IPAddress.Loopback, port);
    }

    public void ListenLocalhost(ushort port, Action<TurboListenOptions> configure)
    {
        Listen(IPAddress.Loopback, port, configure);
    }

    public void ListenAnyIP(ushort port)
    {
        Listen(IPAddress.Any, port);
    }

    public void ListenAnyIP(ushort port, Action<TurboListenOptions> configure)
    {
        Listen(IPAddress.Any, port, configure);
    }

    public void Bind(ListenerOptions options, IListenerFactory factory)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = factory });
    }
}