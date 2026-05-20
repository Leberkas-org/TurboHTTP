using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeServerOps : IServerStageOperations
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];

    public void OnRequest(HttpRequestMessage request) => Requests.Add(request);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

    public void OnScheduleTimer(string name, TimeSpan delay)
    {
    }

    public void OnCancelTimer(string name)
    {
    }

    public ILoggingAdapter Log => NoLogger.Instance;
    public IActorRef StageActor { get; set; } = ActorRefs.Nobody;
}