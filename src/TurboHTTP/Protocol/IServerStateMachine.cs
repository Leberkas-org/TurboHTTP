using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal interface IServerStateMachine
{
    bool CanAcceptResponse { get; }
    bool ShouldComplete { get; }
    int MaxQueuedRequests { get; }

    void PreStart();
    void OnResponse(RequestContext context);
    void DecodeClientData(ITransportInbound data);
    void OnDownstreamFinished();
    void OnTimerFired(string name);
    void OnBodyMessage(object msg);
    void Cleanup();
}

