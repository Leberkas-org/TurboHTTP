using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeServerOps : IServerStageOperations
{
    private readonly List<IFeatureCollection> _features = [];

    public List<IFeatureCollection> Requests => _features;
    public List<ITransportOutbound> Outbound { get; } = [];
    public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];

    public void OnRequest(IFeatureCollection features) => _features.Add(features);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

    public void OnScheduleTimer(string name, TimeSpan delay)
    {
        ScheduledTimers.RemoveAll(t => t.Name == name);
        ScheduledTimers.Add((name, delay));
    }

    public void OnCancelTimer(string name)
    {
        ScheduledTimers.RemoveAll(t => t.Name == name);
        CancelledTimers.Add(name);
    }

    public ILoggingAdapter Log => NoLogger.Instance;
    public IActorRef StageActor { get; set; } = ActorRefs.Nobody;
    public IMaterializer Materializer { get; set; } = null!;
}