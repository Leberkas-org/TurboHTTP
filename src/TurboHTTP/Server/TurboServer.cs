using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting.Logging;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Server;

public sealed class TurboServer : IServer
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    private readonly TurboServerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly FeatureCollection _features = new();

    private ActorSystem? _system;
    private bool _ownsSystem;
    private IActorRef _supervisor = ActorRefs.Nobody;
    private IActorRef _pipelineOwner = ActorRefs.Nobody;

    public TurboServer(IOptions<TurboServerOptions> options, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _services = services;

        var addressesFeature = new ServerAddressesFeature();
        _features.Set<IServerAddressesFeature>(addressesFeature);
    }

    public IFeatureCollection Features => _features;

    public async Task StartAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull
    {
        _system = _services.GetService<ActorSystem>();
        if (_system is null)
        {
            var setup = BootstrapSetup.Create()
                .WithConfig(LoggingHocon)
                .And(new LoggerFactorySetup(_loggerFactory));
            _system = ActorSystem.Create("turbo-server", setup);
            _ownsSystem = true;
        }

        var materializer = _system.Materializer();

        var parallelism = _options.Http2.MaxConcurrentStreams;
        var bridgeFlow = Flow.FromGraph(new SharedBridgeStage<TContext>(
            application,
            parallelism,
            _options.HandlerTimeout,
            _options.HandlerGracePeriod));

        _pipelineOwner = _system.ActorOf(
            Props.Create(() => new ServerPipelineOwner(bridgeFlow)),
            "turbo-pipeline");

        var ready = await _pipelineOwner.Ask<ServerPipelineOwner.PipelineReady>(
            new ServerPipelineOwner.Initialize(),
            TimeSpan.FromSeconds(10),
            cancellationToken);

        var resolver = new EndpointResolver();
        var resolvedEndpoints = resolver.Resolve(_options);

        var listenerProps = new List<Props>(resolvedEndpoints.Count);
        foreach (var endpoint in resolvedEndpoints)
        {
            listenerProps.Add(ListenerActor.Create(
                endpoint.Factory,
                endpoint.Options,
                _options,
                ready.RequestIngress,
                ready.Dispatcher,
                _services,
                materializer,
                endpoint.ConnectionLoggingCategory));
        }

        _supervisor = _system.ActorOf(
            Props.Create(() => new ServerSupervisorActor()),
            "turbo-server");

        var listenersReady = await _supervisor.Ask<ServerSupervisorActor.ListenersReady>(
            new ServerSupervisorActor.StartListeners(listenerProps),
            TimeSpan.FromSeconds(30),
            cancellationToken);

        var addressesFeature = _features.Get<IServerAddressesFeature>()!;
        for (var i = 0; i < resolvedEndpoints.Count; i++)
        {
            var opts = resolvedEndpoints[i].Options;
            var scheme = opts is TcpListenerOptions { ServerCertificate: not null } ? "https" : "http";
            var host = opts.Host;
            if (host is "0.0.0.0" or "::")
            {
                host = "localhost";
            }

            var port = i < listenersReady.BoundPorts.Count ? listenersReady.BoundPorts[i] : opts.Port;
            addressesFeature.Addresses.Add(string.Concat(scheme, "://", host, ":", port.ToString()));
        }

        if (_ownsSystem)
        {
            var cs = CoordinatedShutdown.Get(_system);

            cs.AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "turbo-stop-accepting", () =>
            {
                _supervisor.Tell(new ServerSupervisorActor.StopAccepting());
                return Task.FromResult(Done.Instance);
            });

            cs.AddTask(CoordinatedShutdown.PhaseServiceUnbind, "turbo-goaway", () =>
            {
                _supervisor.Tell(new ServerSupervisorActor.BeginDrain(_options.GracefulShutdownTimeout));
                return Task.FromResult(Done.Instance);
            });

            cs.AddTask(CoordinatedShutdown.PhaseServiceRequestsDone, "turbo-drain", async () =>
            {
                await Task.Delay(_options.GracefulShutdownTimeout, CancellationToken.None);
                return Done.Instance;
            });
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_system is null)
        {
            return;
        }

        if (_ownsSystem)
        {
            await CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
        else
        {
            _supervisor.Tell(new ServerSupervisorActor.StopAccepting());
            _supervisor.Tell(new ServerSupervisorActor.BeginDrain(_options.GracefulShutdownTimeout));
            await Task.Delay(_options.GracefulShutdownTimeout, cancellationToken);
            await _pipelineOwner.GracefulStop(_options.GracefulShutdownTimeout);
            await _supervisor.GracefulStop(_options.GracefulShutdownTimeout);
        }
    }

    public void Dispose()
    {
        if (_ownsSystem)
        {
            _system?.Dispose();
        }
    }
}

internal sealed class ServerAddressesFeature : IServerAddressesFeature
{
    public ICollection<string> Addresses { get; } = new List<string>();
    public bool PreferHostingUrls { get; set; }
}
