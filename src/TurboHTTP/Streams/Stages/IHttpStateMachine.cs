using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal interface IHttpStateMachine
{
    bool CanAcceptRequest { get; }

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void Cleanup();
}
