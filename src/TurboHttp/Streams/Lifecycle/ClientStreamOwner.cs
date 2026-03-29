using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Transport;
using OwnerMsg = TurboHttp.Streams.Lifecycle.ClientStreamOwner;

namespace TurboHttp.Streams.Lifecycle;

/// <summary>
/// Manages both the lifecycle and materialization of the Akka.Streams pipeline
/// for a single client. Receives <see cref="ClientStreamOwner.CreateStreamInstance"/>,
/// materializes the stream directly, tracks pending work from feature BidiStages,
/// coordinates graceful shutdown, and handles retry with exponential backoff.
/// <para>
/// Merged design (was: Owner + Instance actors): This single actor handles all
/// concerns — initialization, materialization, retry cleanup, and shutdown.
/// Resources are cleaned up explicitly on retry (via <see cref="CleanupForRetry"/>)
/// and on actor termination (via <see cref="PostStop"/>).
/// </para>
/// </summary>
internal sealed class ClientStreamOwnerActor : UntypedActor, IWithTimers
{
    private static readonly TimeSpan[] RetryBackoffs =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2)
    ];

    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private const string RetryTimerKey = "retry-create";
    private const string ShutdownTimerKey = "shutdown-timeout";

    private readonly ILoggingAdapter _log = Context.GetLogger();

    // === Lifecycle state ===
    private int _retryAttempts;
    private Exception? _lastError;
    private OwnerMsg.CreateStreamInstance? _createRequest;
    private IActorRef _createRequester = Nobody.Instance;
    private bool _shuttingDown;

    // === Stream materialization state ===
    private ConnectionPool? _pool;
    private ActorMaterializer? _materializer;
    private SharedKillSwitch? _killSwitch;
    private bool _streamRunning;

    public ITimerScheduler Timers { get; set; } = null!;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case OwnerMsg.CreateStreamInstance create:
                HandleCreateStreamInstance(create);
                break;

            case OwnerMsg.StreamInstanceFailed failed:
                HandleStreamInstanceFailed(failed);
                break;

            case OwnerMsg.Shutdown:
                HandleShutdown();
                break;

            case StreamSinkCompleted completed:
                HandleStreamSinkCompleted(completed);
                break;

            case RetryCreateInstance:
                ExecuteRetryCreate();
                break;

            case ShutdownTimeoutExpired:
                HandleShutdownTimeout();
                break;

            default:
                Unhandled(message);
                break;
        }
    }

    private void HandleCreateStreamInstance(OwnerMsg.CreateStreamInstance create)
    {
        _log.Info("Creating stream instance (options: BaseAddress={0})",
            create.ClientOptions.BaseAddress);

        _createRequest = create;
        _createRequester = Sender;
        _retryAttempts = 0;
        _lastError = null;

        MaterializeStream(create);
    }

    private void MaterializeStream(OwnerMsg.CreateStreamInstance create)
    {
        _log.Debug("Materializing stream pipeline (BaseAddress={0})",
            create.ClientOptions.BaseAddress);

        try
        {
            // Create connection pool
            _pool = new ConnectionPool(create.ClientOptions.IdleTimeout);

            // Build the engine flow
            var engine = new Engine();
            var engineFlow = engine.CreateFlow(
                _pool,
                create.ClientOptions,
                create.RequestOptionsFactory,
                create.Pipeline);

            // Materialize the graph
            var materializerSettings = ActorMaterializerSettings.Create(Context.System)
                .WithInputBuffer(initialSize: 4, maxSize: 16);
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
                    Sink.ForEach<HttpResponseMessage>(msg => create.ResponseWriter.TryWrite(msg)),
                    _materializer);

            MonitorSinkCompletion(completionTask);

            _streamRunning = true;
            _log.Debug("Stream pipeline materialized successfully");

            // Notify requester of successful materialization
            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new OwnerMsg.StreamInstanceCreated());
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
            var backoff = RetryBackoffs[Math.Min(_retryAttempts - 1, RetryBackoffs.Length - 1)];
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
                _createRequester.Tell(new OwnerMsg.StreamInstanceFailed(
                    _lastError!, _retryAttempts));
            }
        }
    }

    private void HandleStreamInstanceFailed(OwnerMsg.StreamInstanceFailed failed)
    {
        _lastError = failed.Reason;
        _retryAttempts = failed.AttemptNumber;

        _log.Warning("Stream instance failure reported (attempt {0}): {1}",
            failed.AttemptNumber, failed.Reason.Message);

        CleanupResources();

        if (_retryAttempts < MaxRetryAttempts && _createRequest is not null && !_shuttingDown)
        {
            var backoff = RetryBackoffs[Math.Min(_retryAttempts, RetryBackoffs.Length - 1)];
            _retryAttempts++;
            _log.Info("Scheduling retry attempt {0} after {1}ms backoff",
                _retryAttempts, backoff.TotalMilliseconds);

            Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
        }
        else
        {
            _log.Error("Retries exhausted ({0} attempts). Last error: {1}",
                _retryAttempts, _lastError?.Message);

            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new OwnerMsg.StreamInstanceFailed(
                    _lastError!, _retryAttempts));
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
            _log.Info("Shutdown requested — firing KillSwitch, pipeline will drain");
            _killSwitch.Shutdown();

            Timers.StartSingleTimer(ShutdownTimerKey, ShutdownTimeoutExpired.Instance, ShutdownTimeout);
        }
        else
        {
            _log.Info("Shutdown requested — no stream materialized, self-terminating");
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

    private void MonitorSinkCompletion(Task completionTask)
    {
        // Route the completion notification from the Sink.ForEach task into this
        // actor's mailbox via Self.Tell. This is safe because the actor processes
        // the message through OnReceive on its dispatcher thread.
        var self = Self;

        completionTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                self.Tell(new StreamSinkCompleted(t.Exception?.GetBaseException()));
            }
            else
            {
                self.Tell(new StreamSinkCompleted(null));
            }
        }, TaskScheduler.Default);
    }

    private void HandleStreamSinkCompleted(StreamSinkCompleted completed)
    {
        _log.Debug("Stream sink completed (error: {0})",
            completed.Error?.Message ?? "none");

        if (completed.Error is not null)
        {
            HandleMaterializationFailed(completed.Error);
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

        // Dispose connection pool
        if (_pool is not null)
        {
            try
            {
                _pool.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("Error disposing connection pool: {0}", ex.Message);
            }

            _pool = null;
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