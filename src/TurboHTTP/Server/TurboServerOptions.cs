using System.Net;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp.Listener;
using Servus.Akka.Transport.Quic.Listener;

namespace TurboHTTP.Server;

public sealed class TurboServerOptions
{
    public TurboServerLimits Limits { get; } = new();

    [Obsolete("Use Limits.MaxConcurrentConnections instead")]
    public int MaxConcurrentConnections
    {
        get => Limits.MaxConcurrentConnections;
        set => Limits.MaxConcurrentConnections = value;
    }

    [Obsolete("Use Limits.MaxConcurrentUpgradedConnections instead")]
    public int MaxConcurrentUpgradedConnections
    {
        get => Limits.MaxConcurrentUpgradedConnections;
        set => Limits.MaxConcurrentUpgradedConnections = value;
    }

    [Obsolete("Use Limits.KeepAliveTimeout instead")]
    public TimeSpan KeepAliveTimeout
    {
        get => Limits.KeepAliveTimeout;
        set => Limits.KeepAliveTimeout = value;
    }

    [Obsolete("Use Limits.RequestHeadersTimeout instead")]
    public TimeSpan RequestHeadersTimeout
    {
        get => Limits.RequestHeadersTimeout;
        set => Limits.RequestHeadersTimeout = value;
    }
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HandlerGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    public int BodyBufferThreshold { get; set; } = 64 * 1024;
    public TimeSpan BodyConsumptionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int ResponseBodyChunkSize { get; set; } = 16 * 1024;

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
    internal Action<TurboListenOptions>? EndpointDefaultsCallback { get; private set; }

    public IList<string> Urls { get; } = new List<string>();

    public void ConfigureHttpsDefaults(Action<TurboHttpsOptions> configure)
    {
        HttpsDefaultsCallback = configure;
    }

    public void ConfigureEndpointDefaults(Action<TurboListenOptions> configure)
    {
        EndpointDefaultsCallback = configure;
    }

    public void Listen(IPAddress address, ushort port)
    {
        var listenOptions = new TurboListenOptions(address, port);
        EndpointDefaultsCallback?.Invoke(listenOptions);
        ListenOptions.Add(listenOptions);
    }

    public void Listen(IPAddress address, ushort port, Action<TurboListenOptions> configure)
    {
        var listenOptions = new TurboListenOptions(address, port);
        EndpointDefaultsCallback?.Invoke(listenOptions);
        configure(listenOptions);
        ListenOptions.Add(listenOptions);
    }

    public void Listen(string url)
    {
        try
        {
            var listenOptions = EndpointResolver.ParseUrl(url);
            EndpointDefaultsCallback?.Invoke(listenOptions);
            ListenOptions.Add(listenOptions);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(url), ex);
        }
    }

    public void Listen(string url, Action<TurboListenOptions> configure)
    {
        try
        {
            var listenOptions = EndpointResolver.ParseUrl(url);
            EndpointDefaultsCallback?.Invoke(listenOptions);
            configure(listenOptions);
            ListenOptions.Add(listenOptions);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(url), ex);
        }
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