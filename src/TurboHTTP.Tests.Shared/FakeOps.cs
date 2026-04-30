using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeOps : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool ReconnectFailed { get; private set; }

    public void OnResponse(HttpResponseMessage r) => Responses.Add(r);
    public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);
    public void OnWarning(string msg) => Warnings.Add(msg);
    public void OnReconnectFailed() => ReconnectFailed = true;
    public ILoggingAdapter Log => NoLogger.Instance;
}