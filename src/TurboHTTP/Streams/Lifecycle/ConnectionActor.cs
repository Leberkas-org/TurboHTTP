using System.Net.Security;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams.Lifecycle;

internal enum ConnectionCompletionReason
{
    Normal,
    Error,
    Timeout,
    ServerShutdown
}

internal sealed class ConnectionActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _connectionId;
    private SharedKillSwitch? _killSwitch;
    private bool _draining;
    private readonly CancellationTokenSource _cts = new();

    public sealed record Materialize(
        Flow<ITransportOutbound, ITransportInbound, NotUsed> ConnectionFlow,
        IServerProtocolEngine Engine,
        TurboRequestDelegate Pipeline,
        RouteTable RouteTable,
        TurboConnectionInfo ConnectionInfo,
        IServiceProvider Services,
        IMaterializer Materializer,
        string? ConnectionLoggingCategory = null);

    public sealed record GracefulStop(TimeSpan Timeout);

    public sealed record StreamCompleted(Exception? Error);

    public sealed record ConnectionCompleted(string ConnectionId, ConnectionCompletionReason Reason);

    public ConnectionActor(string connectionId)
    {
        _connectionId = connectionId;

        Receive<Materialize>(OnMaterialize);
        Receive<StreamCompleted>(OnStreamCompleted);
        Receive<GracefulStop>(OnGracefulStop);
        Receive<ReceiveTimeout>(_ => OnDrainTimeout());
    }

    private void OnMaterialize(Materialize msg)
    {
        _log.Debug("Connection {0} materializing pipeline", _connectionId);

        _killSwitch = KillSwitches.Shared("connection-" + _connectionId);

        var contextBidi = BidiFlow.FromGraph(
            new HttpContextBidiStage(msg.ConnectionInfo, msg.Services, _cts.Token));
        var middleware = Flow.FromGraph(new MiddlewarePipelineStage(msg.Pipeline));
        var routing = Flow.FromGraph(new RoutingStage(msg.RouteTable));
        var innerFlow = middleware.Via(routing);
        var httpFlow = contextBidi.Join(innerFlow);

        var protocolBidi = msg.Engine.CreateFlow();
        var composed = protocolBidi.Join(httpFlow);

        var self = Self;
        var connectionInfo = msg.ConnectionInfo;
        var inboundTap = Flow.Create<ITransportInbound>()
            .Select(item =>
            {
                switch (item)
                {
                    case TransportConnected { Info: { Remote: System.Net.IPEndPoint remote } } connected:
                        connectionInfo.RemoteIpAddress = remote.Address;
                        connectionInfo.RemotePort = remote.Port;
                        if (connected.Info is { Local: System.Net.IPEndPoint local })
                        {
                            connectionInfo.LocalIpAddress = local.Address;
                            connectionInfo.LocalPort = local.Port;
                        }
                        if (connected.Info.Security is { } security)
                        {
                            connectionInfo.SetSecurityInfo(security);
                            connectionInfo.SetNegotiatedProtocol(security.ApplicationProtocol);
                        }
                        break;
                    case TransportTlsState tlsState:
                        connectionInfo.SetTlsState(tlsState.SslStream, tlsState.AllowDelayedNegotiation);
                        if (tlsState.SslStream is not null)
                        {
                            connectionInfo.SetClientCertificateFromHandshake(tlsState.SslStream);
                        }
                        break;
                }

                return item;
            });

        Flow<ITransportInbound, ITransportInbound, NotUsed>? loggingFlow = null;
        if (msg.ConnectionLoggingCategory is { } loggingCategory)
        {
            var loggerFactory = msg.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggingCategory);
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                loggingFlow = Flow.Create<ITransportInbound>()
                    .Select(item =>
                    {
                        if (item is TransportData { Buffer: var buffer })
                        {
                            var dump = HexDumpFormatter.Format(buffer.Span);
                            logger.LogDebug("ReadAsync[{Length}]{NewLine}{Dump}",
                                buffer.Length, Environment.NewLine, dump);
                        }

                        return item;
                    });
            }
        }

        var pipeline = msg.ConnectionFlow
            .Via(_killSwitch.Flow<ITransportInbound>())
            .Via(inboundTap);

        if (loggingFlow is not null)
        {
            pipeline = pipeline.Via(loggingFlow);
        }

        var completionTask = pipeline
            .ViaMaterialized(
                Flow.Create<ITransportInbound>().WatchTermination(Keep.Right),
                Keep.Right)
            .Join(composed)
            .Run(msg.Materializer);

        completionTask.PipeTo(self,
            success: () => new StreamCompleted(null),
            failure: ex => new StreamCompleted(ex));
    }

    private void OnStreamCompleted(StreamCompleted msg)
    {
        var reason = _draining
            ? ConnectionCompletionReason.ServerShutdown
            : msg.Error is null
                ? ConnectionCompletionReason.Normal
                : ConnectionCompletionReason.Error;

        if (msg.Error is not null)
        {
            _log.Warning("Connection {0} stream failed: {1}", _connectionId, msg.Error.Message);
        }
        else
        {
            _log.Debug("Connection {0} stream completed normally", _connectionId);
        }

        var completion = new ConnectionCompleted(_connectionId, reason);
        Context.Parent.Tell(completion);
        Self.Tell(PoisonPill.Instance);
    }

    private void OnGracefulStop(GracefulStop msg)
    {
        _log.Info("Connection {0} graceful stop requested (timeout: {1})", _connectionId, msg.Timeout);
        _draining = true;
        _cts.Cancel();
        _killSwitch?.Shutdown();
        SetReceiveTimeout(msg.Timeout);
    }

    private void OnDrainTimeout()
    {
        _log.Warning("Connection {0} drain timeout expired", _connectionId);
        var completion = new ConnectionCompleted(_connectionId, ConnectionCompletionReason.Timeout);
        Context.Parent.Tell(completion);
        Self.Tell(PoisonPill.Instance);
    }

    public static Props Create(string connectionId)
        => Props.Create(() => new ConnectionActor(connectionId));
}
