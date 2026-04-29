using Akka.Event;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class MockTransportOperations : ITransportOperations
{
    public List<ITransportInbound> PushedOutputs { get; } = [];
    public int PullOutputCount { get; set; }
    public int CompleteStageCount { get; private set; }
    public List<(string Key, TimeSpan Delay)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    public void OnPushInbound(ITransportInbound item) => PushedOutputs.Add(item);
    public void OnSignalPullOutbound() => PullOutputCount++;
    public void OnCompleteStage() => CompleteStageCount++;
    public void OnScheduleTimer(string key, TimeSpan delay) => ScheduledTimers.Add((key, delay));
    public void OnCancelTimer(string key) => CancelledTimers.Add(key);
    public ILoggingAdapter Log => NoLogger.Instance;
}