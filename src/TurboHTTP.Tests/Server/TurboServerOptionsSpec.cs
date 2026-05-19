using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp.Listener;

namespace TurboHTTP.Tests.Server;

public sealed class TurboServerOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_keep_alive_to_120_seconds()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(TimeSpan.FromSeconds(120), options.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_request_headers_timeout_to_30_seconds()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestHeadersTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_graceful_shutdown_to_30_seconds()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.GracefulShutdownTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Http2ServerOptions_should_default_max_concurrent_streams_to_100()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(100, options.Http2.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void Http2ServerOptions_should_default_max_frame_size_to_16384()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(16384, options.Http2.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    public void Http3ServerOptions_should_default_max_concurrent_streams_to_100()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(100, options.Http3.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void Http3ServerOptions_should_default_web_transport_to_disabled()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.False(options.Http3.EnableWebTransport);
    }

    [Fact(Timeout = 5000)]
    public void Endpoints_should_be_empty_by_default()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();

        Assert.Empty(options.Endpoints);
    }

    [Fact(Timeout = 5000)]
    public void Endpoints_should_accept_listener_bindings()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        options.Endpoints.Add(new TurboHTTP.Server.ListenerBinding
        {
            Options = new TcpListenerOptions { Host = "0.0.0.0", Port = 8080 },
            Factory = new TcpListenerFactory()
        });

        Assert.Single(options.Endpoints);
        Assert.Equal(8080, ((TcpListenerOptions)options.Endpoints[0].Options).Port);
    }

    [Fact(Timeout = 5000)]
    public void Bind_with_TcpListenerOptions_should_add_endpoint_with_TcpListenerFactory()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        options.Bind(new TcpListenerOptions { Host = "0.0.0.0", Port = 8080 });
        Assert.Single(options.Endpoints);
        Assert.IsType<TcpListenerFactory>(options.Endpoints[0].Factory);
    }

    [Fact(Timeout = 5000)]
    public void Bind_with_custom_factory_should_add_endpoint()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        var factory = new TcpListenerFactory();
        options.Bind(new TcpListenerOptions { Host = "0.0.0.0", Port = 9090 }, factory);
        Assert.Single(options.Endpoints);
        Assert.Same(factory, options.Endpoints[0].Factory);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_body_buffer_threshold_to_65536()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(65536, options.BodyBufferThreshold);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_body_consumption_timeout_to_30_seconds()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.BodyConsumptionTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_default_response_body_chunk_size_to_16384()
    {
        var options = new TurboHTTP.Server.TurboServerOptions();
        Assert.Equal(16384, options.ResponseBodyChunkSize);
    }
}