using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal interface IServerStageOperations
{
    void OnRequest(IFeatureCollection features);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan delay);
    void OnCancelTimer(string name);
    ILoggingAdapter Log { get; }
    IActorRef StageActor { get; }
    IMaterializer Materializer { get; }
    IServiceProvider? Services => null;
    TurboHttpConnectionFeature? ConnectionFeature => null;
    TlsHandshakeFeature? TlsHandshakeFeature => null;
}