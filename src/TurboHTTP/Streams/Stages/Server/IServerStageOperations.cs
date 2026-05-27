using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal interface IServerStageOperations
{
    void OnRequest(RequestContext context);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan delay);
    void OnCancelTimer(string name);
    ILoggingAdapter Log { get; }
    IActorRef StageActor { get; }
    IMaterializer Materializer { get; }
    IServiceProvider? Services => null;
    TurboConnectionInfo? ConnectionInfo => null;
    TlsHandshakeFeature? TlsHandshakeFeature => null;
}