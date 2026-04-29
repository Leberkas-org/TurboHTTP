using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;
using Servus.Akka.Transport.Tcp;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Pooling;

// QuicConnectionManagerActor is guarded on linux/macOS/windows — all desktop platforms.
#pragma warning disable CA1416

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ClientStreamOwner : ReceiveActor, IWithTimers
{
    internal sealed record CreateStreamInstance(
        TurboClientOptions ClientOptions,
        Func<TurboRequestOptions> RequestOptionsFactory,
        PipelineDescriptor Pipeline,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter);

    internal sealed record StreamInstanceCreated;

    internal sealed record StreamInstanceFailed(Exception Reason, int AttemptNumber);

    internal sealed record Shutdown;

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private const double BackoffMultiplier = 2.0;

    private const int MaxRetryAttempts = 10;

    private static TimeSpan CalculateBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(
            Math.Min(InitialBackoff.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt),
                MaxBackoff.TotalMilliseconds));

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private const string RetryTimerKey = "retry-create";
    private const string ShutdownTimerKey = "shutdown-timeout";

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private int _retryAttempts;
    private Exception? _lastError;
    private CreateStreamInstance? _createRequest;
    private IActorRef _createRequester = Nobody.Instance;
    private bool _shuttingDown;

    private IActorRef? _tcpConnectionManager;
    private IActorRef? _quicConnectionManager;
    private ActorMaterializer? _materializer;
    private SharedKillSwitch? _killSwitch;
    private bool _streamRunning;

    public ITimerScheduler Timers { get; set; } = null!;

    public ClientStreamOwner()
    {
        Receive<CreateStreamInstance>(HandleCreateStreamInstance);
        Receive<StreamInstanceFailed>(HandleStreamInstanceFailed);
        Receive<Shutdown>(_ => HandleShutdown());
        Receive<StreamSinkCompleted>(HandleStreamSinkCompleted);
        Receive<RetryCreateInstance>(_ => ExecuteRetryCreate());
        Receive<ShutdownTimeoutExpired>(_ => HandleShutdownTimeout());
    }

    private void HandleCreateStreamInstance(CreateStreamInstance create)
    {
        _log.Debug("Creating stream instance (options: BaseAddress={0})",
            create.ClientOptions.BaseAddress);

        _createRequest = create;
        _createRequester = Sender;
        _retryAttempts = 0;
        _lastError = null;

        MaterializeStream(create);
    }

    private void MaterializeStream(CreateStreamInstance create)
    {
        _log.Debug("Materializing stream pipeline (BaseAddress={0})",
            create.ClientOptions.BaseAddress);

        try
        {
            var opts = create.ClientOptions;

            var poolRegistry = new PoolConfigRegistry(new TcpPoolConfig(
                    1,
                    opts.PooledConnectionIdleTimeout,
                    opts.PooledConnectionLifetime,
                    ReuseOnUpstreamFinish: true))
                .Register(PoolKeys.Http10, new TcpPoolConfig(
                    MaxConnectionsPerHost: int.MaxValue,
                    IdleTimeout: TimeSpan.Zero,
                    ConnectionLifetime: TimeSpan.Zero,
                    ReuseOnUpstreamFinish: false))
                .Register(PoolKeys.Http11, new TcpPoolConfig(
                    opts.Http1.MaxConnectionsPerServer,
                    opts.PooledConnectionIdleTimeout,
                    opts.PooledConnectionLifetime,
                    ReuseOnUpstreamFinish: true))
                .Register(PoolKeys.Http2, new TcpPoolConfig(
                    opts.Http2.MaxConnectionsPerServer,
                    opts.PooledConnectionIdleTimeout,
                    opts.PooledConnectionLifetime,
                    ReuseOnUpstreamFinish: false));

            _tcpConnectionManager = Context.ActorOf(
                Props.Create(() => new TcpConnectionManagerActor(poolRegistry)), "tcp-pool");

            _quicConnectionManager = Context.ActorOf(Props.Create(() => new QuicConnectionManagerActor()), "quic-pool");

            var transports = new TransportRegistry()
                .Register(new Version(1, 0),
                    new TcpTransportFactory(_tcpConnectionManager, new Http10PoolingStrategy()))
                .Register(new Version(1, 1),
                    new TcpTransportFactory(_tcpConnectionManager, new Http11PoolingStrategy()))
                .Register(new Version(2, 0), new TcpTransportFactory(_tcpConnectionManager, new Http2PoolingStrategy()))
                .Register(new Version(3, 0), new QuicTransportFactory(_quicConnectionManager));

            var engine = new Engine();
            var engineFlow = engine.CreateFlow(
                transports,
                create.Pipeline,
                create.ClientOptions,
                create.RequestOptionsFactory);

            // Materialize the graph
            var materializerSettings = ActorMaterializerSettings.Create(Context.System)
                .WithInputBuffer(initialSize: 32, maxSize: 128);
            _materializer = Context.System.Materializer(
                settings: materializerSettings,
                namePrefix: $"stream-owner-{Self.Path.Name}");

            // KillSwitch absorbs ChannelSource completion so the pipeline stays alive
            // until explicitly shut down. This prevents premature completion when the
            // channel writer completes while feature BidiStages still have re-injections.
            _killSwitch = KillSwitches.Shared($"client-{Self.Path.Name}");

            // Use Sink.ForEach to write responses to the externally-owned writer.
            // The sink does NOT own the writer — the manager completes it on shutdown.
            // Sink.ForEach materializes a Task that completes when the stream terminates,
            // which we use for completion monitoring.
            var completionTask = ChannelSource.FromReader(create.RequestReader)
                .Via(_killSwitch.Flow<HttpRequestMessage>())
                .Via(engineFlow)
                .RunWith(
                    Sink.ForEach<HttpResponseMessage>(msg =>
                    {
                        // Direct PendingRequest completion — no dictionary lookup (G2).
                        // Version guard prevents stale completions when PendingRequest is pooled (E4).
                        if (msg.RequestMessage is { } req &&
                            req.Options.TryGetValue(TcsCorrelation.Key, out var pending) &&
                            req.Options.TryGetValue(TcsCorrelation.VersionKey, out var ver))
                        {
                            pending.TrySetResult(msg, ver);
                            return;
                        }

                        // Also write to the response channel for ITurboHttpClient.Responses consumers.
                        create.ResponseWriter.TryWrite(msg);
                    }),
                    _materializer);

            completionTask.PipeTo(Self, Self,
                () => new StreamSinkCompleted(null),
                ex => new StreamSinkCompleted(ex.GetBaseException()));

            _streamRunning = true;
            _log.Debug("Stream pipeline materialized successfully");

            // Notify requester of successful materialization
            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new StreamInstanceCreated());
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to materialize stream pipeline");
            CleanupResources();
            HandleMaterializationFailed(ex);
        }
    }

    private void HandleMaterializationFailed(Exception ex)
    {
        _lastError = ex;
        _retryAttempts++;

        _log.Warning("Stream materialization failed (attempt {0}/{1}): {2}",
            _retryAttempts, MaxRetryAttempts, ex.Message);

        if (_retryAttempts <= MaxRetryAttempts && _createRequest is not null && !_shuttingDown)
        {
            var backoff = CalculateBackoff(_retryAttempts - 1);
            _log.Info("Scheduling retry attempt {0} after {1}ms backoff",
                _retryAttempts, backoff.TotalMilliseconds);

            Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
        }
        else
        {
            _log.Error("Stream materialization failed after {0} attempts. Last error: {1}",
                _retryAttempts, _lastError?.Message);

            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new StreamInstanceFailed(_lastError!, _retryAttempts));
            }
        }
    }

    private void HandleStreamInstanceFailed(StreamInstanceFailed failed)
    {
        _lastError = failed.Reason;
        _retryAttempts = failed.AttemptNumber;

        _log.Warning("Stream instance failure reported (attempt {0}): {1}",
            failed.AttemptNumber, failed.Reason.Message);

        CleanupResources();

        if (_retryAttempts < MaxRetryAttempts && _createRequest is not null && !_shuttingDown)
        {
            var backoff = CalculateBackoff(_retryAttempts);
            _retryAttempts++;
            _log.Info("Scheduling retry attempt {0} after {1}ms backoff", _retryAttempts, backoff.TotalMilliseconds);

            Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
        }
        else
        {
            _log.Error("Retries exhausted ({0} attempts). Last error: {1}",
                _retryAttempts, _lastError?.Message);

            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new StreamInstanceFailed(_lastError!, _retryAttempts));
            }
        }
    }

    private void ExecuteRetryCreate()
    {
        if (_createRequest is null || _shuttingDown)
        {
            return;
        }

        _log.Info("Executing retry attempt {0}/{1}", _retryAttempts, MaxRetryAttempts);
        CleanupForRetry();
        MaterializeStream(_createRequest);
    }

    private void HandleShutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;

        if (_killSwitch is not null)
        {
            _log.Debug("Shutdown requested — firing KillSwitch, pipeline will drain");
            _killSwitch.Shutdown();

            Timers.StartSingleTimer(ShutdownTimerKey, ShutdownTimeoutExpired.Instance, ShutdownTimeout);
        }
        else
        {
            _log.Debug("Shutdown requested — no stream materialized, self-terminating");
            Context.Stop(Self);
        }
    }

    private void HandleShutdownTimeout()
    {
        _log.Warning("Shutdown safety timeout expired — pipeline did not drain within {0}s. Force-stopping.",
            ShutdownTimeout.TotalSeconds);
        CleanupResources();
        Context.Stop(Self);
    }

    private void HandleStreamSinkCompleted(StreamSinkCompleted completed)
    {
        _log.Debug("Stream sink completed (error: {0})",
            completed.Error?.Message ?? "none");

        if (completed.Error is not null)
        {
            if (_shuttingDown)
            {
                // Stream failed while graceful shutdown was in progress.
                // The error is expected (pipeline aborted by KillSwitch or connection failure).
                // Cancel the safety timeout and stop immediately — no need to wait 5s.
                Timers.Cancel(ShutdownTimerKey);
                _log.Debug("Stream failed during shutdown — stopping immediately");
                Context.Stop(Self);
            }
            else
            {
                HandleMaterializationFailed(completed.Error);
            }
        }
        else
        {
            // Pipeline drained cleanly (after KillSwitch.Shutdown or error-free completion).
            // Cancel the safety timeout — no force-stop needed.
            Timers.Cancel(ShutdownTimerKey);
            _log.Debug("Pipeline drained, stopping actor");
            Context.Stop(Self);
        }
    }

    private void CleanupForRetry()
    {
        _log.Debug("Cleaning up resources before retry");

        if (_killSwitch is not null)
        {
            try
            {
                _killSwitch.Abort(new Exception("Retry cleanup"));
            }
            catch (Exception ex)
            {
                _log.Warning("Error aborting KillSwitch: {0}", ex.Message);
            }
        }

        CleanupResources();
    }

    private void CleanupResources()
    {
        // NOTE: Do NOT complete _requestReader or _responseWriter here.
        // They are externally-owned by TurboClientStreamManager and must remain
        // open for potential retry (new materialization reconnecting to same channels).

        // Dispose materializer (stops the Akka stream graph)
        if (_materializer is not null)
        {
            try
            {
                _materializer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("Error disposing materializer: {0}", ex.Message);
            }

            _materializer = null;
        }

        // Stop connection manager actors (PostStop disposes all leases)
        if (_tcpConnectionManager is not null)
        {
            try
            {
                Context.Stop(_tcpConnectionManager);
            }
            catch (Exception ex)
            {
                _log.Warning("Error stopping TCP connection manager: {0}", ex.Message);
            }

            _tcpConnectionManager = null;
        }

        if (_quicConnectionManager is not null)
        {
            try
            {
                Context.Stop(_quicConnectionManager);
            }
            catch (Exception ex)
            {
                _log.Warning("Error stopping QUIC connection manager: {0}", ex.Message);
            }

            _quicConnectionManager = null;
        }

        _killSwitch = null;
        _streamRunning = false;
    }

    protected override void PostStop()
    {
        _log.Debug("PostStop: cleaning up resources (streamRunning: {0})", _streamRunning);
        CleanupResources();
        base.PostStop();
    }

    /// <summary>Internal signal to trigger a retry of stream instance creation.</summary>
    private sealed class RetryCreateInstance
    {
        public static readonly RetryCreateInstance Instance = new();

        private RetryCreateInstance()
        {
        }
    }

    /// <summary>Internal signal that the shutdown timeout has expired.</summary>
    private sealed class ShutdownTimeoutExpired
    {
        public static readonly ShutdownTimeoutExpired Instance = new();

        private ShutdownTimeoutExpired()
        {
        }
    }

    /// <summary>
    /// Internal signal that the stream sink has completed (success or failure).
    /// Routed from the async completion callback into the actor's mailbox.
    /// </summary>
    private sealed record StreamSinkCompleted(Exception? Error);
}