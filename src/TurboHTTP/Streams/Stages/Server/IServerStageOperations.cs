using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages.Server;

internal interface IServerStageOperations
{
    void OnRequest(HttpRequestMessage request);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan delay);
    void OnCancelTimer(string name);
    ILoggingAdapter Log { get; }
    IActorRef StageActor { get; }
}