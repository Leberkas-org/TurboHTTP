using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting.Logging;
using Akka.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server.Internal;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Server.Hosting;

internal sealed class TurboServerHostedService : IHostedService, IDisposable
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    private readonly TurboServerOptions _options;
    private readonly TurboRouteTable _routeTable;
    private readonly TurboPipelineBuilder _pipelineBuilder;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;

    private ActorSystem? _system;
    private bool _ownsSystem;
    private IActorRef _supervisor = ActorRefs.Nobody;

    public TurboServerHostedService(
        TurboServerOptions options,
        TurboRouteTable routeTable,
        TurboPipelineBuilder pipelineBuilder,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _routeTable = routeTable;
        _pipelineBuilder = pipelineBuilder;
        _services = services;
        _loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
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
        var routeTable = _routeTable.Freeze();
        var pipeline = _pipelineBuilder.Build();

        var resolver = new EndpointResolver();
        var resolvedEndpoints = resolver.Resolve(_options);

        var listenerProps = new List<Props>(resolvedEndpoints.Count);
        foreach (var endpoint in resolvedEndpoints)
        {
            listenerProps.Add(ListenerActor.Create(
                endpoint.Factory,
                endpoint.Options,
                _options,
                pipeline,
                routeTable,
                _services,
                materializer));
        }

        _supervisor = _system.ActorOf(
            Props.Create(() => new ServerSupervisorActor()),
            "turbo-server");

        await _supervisor.Ask<ServerSupervisorActor.ListenersReady>(
            new ServerSupervisorActor.StartListeners(listenerProps),
            TimeSpan.FromSeconds(30),
            cancellationToken);

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_system is not null)
        {
            await CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance);
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
