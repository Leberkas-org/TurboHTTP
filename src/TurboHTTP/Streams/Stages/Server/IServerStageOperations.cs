using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal interface IServerStageOperations
{
    void OnRequest(TurboHttpContext context);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan delay);
    void OnCancelTimer(string name);
    ILoggingAdapter Log { get; }
    IActorRef StageActor { get; }
    IServiceProvider? Services => null;
    TurboConnectionInfo? ConnectionInfo => null;
}