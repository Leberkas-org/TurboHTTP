using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.Tests.Shared;

internal sealed class MockTransportOperations : ITransportOperations
{
    public List<IInputItem> PushedOutputs { get; } = [];
    public int PullInputCount { get; set; }
    public int CompleteStageCount { get; private set; }
    public List<(string Key, TimeSpan Delay)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    public void OnPushOutput(IInputItem item) => PushedOutputs.Add(item);
    public void OnSignalPullInput() => PullInputCount++;
    public void OnCompleteStage() => CompleteStageCount++;
    public void OnScheduleTimer(string key, TimeSpan delay) => ScheduledTimers.Add((key, delay));
    public void OnCancelTimer(string key) => CancelledTimers.Add(key);
    public ILoggingAdapter Log => NoLogger.Instance;
}