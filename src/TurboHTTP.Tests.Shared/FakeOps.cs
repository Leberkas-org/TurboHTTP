using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeOps : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<IOutputItem> Outbound { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool ReconnectFailed { get; private set; }

    public void OnResponse(HttpResponseMessage r) => Responses.Add(r);
    public void OnOutbound(IOutputItem item) => Outbound.Add(item);
    public void OnWarning(string msg) => Warnings.Add(msg);
    public void OnReconnectFailed() => ReconnectFailed = true;
}