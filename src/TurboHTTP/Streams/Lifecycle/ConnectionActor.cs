using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using static Servus.Core.Servus;

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
    private long _connectionTimestamp;
    private Activity? _connectionActivity;

    public sealed record Materialize(
        Flow<ITransportOutbound, ITransportInbound, NotUsed> ConnectionFlow,
        IServerProtocolEngine Engine,
        Flow<IFeatureCollection, IFeatureCollection, NotUsed> BridgeFlow,
        IServiceProvider Services,
        IMaterializer Materializer,
        string? ConnectionLoggingCategory = null,
        long ConnectionTimestamp = 0,
        Activity? ConnectionActivity = null);

    public sealed record GracefulStop(TimeSpan Timeout);

    public sealed record StreamCompleted(Exception? Error);

    public sealed record ConnectionCompleted(string ConnectionId, ConnectionCompletionReason Reason, long ConnectionTimestamp = 0, Activity? ConnectionActivity = null);

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
        _connectionTimestamp = msg.ConnectionTimestamp;
        _connectionActivity = msg.ConnectionActivity;
        _log.Debug("Connection {0} materializing pipeline", _connectionId);

        var negotiationStart = Stopwatch.GetTimestamp();

        _killSwitch = KillSwitches.Shared("connection-" + _connectionId);

        var protocolBidi = msg.Engine.CreateFlow(msg.Services);
        var composed = protocolBidi.Join(msg.BridgeFlow);

        if (Metrics.ProtocolNegotiationDuration().Enabled)
        {
            RecordProtocolNegotiation(negotiationStart, msg.Engine);
        }

        var self = Self;
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
            .Via(_killSwitch.Flow<ITransportInbound>());

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordProtocolNegotiation(long startTimestamp, IServerProtocolEngine engine)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        var version = engine.ProtocolVersion;
        Metrics.ProtocolNegotiationDuration().Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("network.protocol.version",
                TurboHttpInstrumentationExtensions.FormatProtocolVersion(version)));
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

        var completion = new ConnectionCompleted(_connectionId, reason, _connectionTimestamp, _connectionActivity);
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
