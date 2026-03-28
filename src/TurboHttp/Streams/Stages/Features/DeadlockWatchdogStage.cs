using System;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Event raised when the watchdog detects that no element has flowed through the stage
/// for longer than the configured warning threshold.
/// </summary>
internal sealed record DeadlockStallEvent(
    string StageName,
    TimeSpan StallDuration,
    bool UpstreamDemandPending,
    bool DownstreamDemandPending,
    DateTimeOffset DetectedAt);

/// <summary>
/// Configuration options for <see cref="DeadlockWatchdogStage{T}"/>.
/// </summary>
internal sealed record DeadlockWatchdogOptions
{
    /// <summary>
    /// Duration of inactivity before the watchdog reports a stall. Default is 10 seconds.
    /// </summary>
    public TimeSpan WarningThreshold { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Logical name used in stall events to identify which pipeline segment stalled.
    /// </summary>
    public required string StageName { get; init; }

    /// <summary>
    /// Callback invoked when a stall is detected. Called on the stage's dispatcher thread.
    /// </summary>
    public required Action<DeadlockStallEvent> OnStall { get; init; }
}

/// <summary>
/// Transparent flow stage that monitors element throughput and reports stalls.
/// <para>
/// Elements pass through unchanged. When no element flows for longer than
/// <see cref="DeadlockWatchdogOptions.WarningThreshold"/>, the configured
/// <see cref="DeadlockWatchdogOptions.OnStall"/> callback fires with diagnostic details.
/// The timer reschedules after each stall report, providing continuous monitoring.
/// </para>
/// </summary>
internal sealed class DeadlockWatchdogStage<T> : GraphStage<FlowShape<T, T>>
{
    private const string StallTimerKey = "stall";

    private readonly DeadlockWatchdogOptions _options;

    private readonly Inlet<T> _in = new("DeadlockWatchdog.In");
    private readonly Outlet<T> _out = new("DeadlockWatchdog.Out");

    public override FlowShape<T, T> Shape { get; }

    public DeadlockWatchdogStage(DeadlockWatchdogOptions options)
    {
        _options = options;
        Shape = new FlowShape<T, T>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private readonly DeadlockWatchdogStage<T> _stage;

        public Logic(DeadlockWatchdogStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    CancelTimer(StallTimerKey);
                    Push(stage._out, Grab(stage._in));
                    ScheduleOnce(StallTimerKey, stage._options.WarningThreshold);
                },
                onUpstreamFinish: () =>
                {
                    CancelTimer(StallTimerKey);
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    CancelTimer(StallTimerKey);
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            Pull(_stage._in);
            ScheduleOnce(StallTimerKey, _stage._options.WarningThreshold);
        }

        protected override void OnTimer(object timerKey)
        {
            var options = _stage._options;
            var stallEvent = new DeadlockStallEvent(
                StageName: options.StageName,
                StallDuration: options.WarningThreshold,
                UpstreamDemandPending: HasBeenPulled(_stage._in),
                DownstreamDemandPending: IsAvailable(_stage._out),
                DetectedAt: DateTimeOffset.UtcNow);

            options.OnStall(stallEvent);

            // Reschedule for continuous monitoring
            ScheduleOnce(StallTimerKey, options.WarningThreshold);
        }
    }
}
