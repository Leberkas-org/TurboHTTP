using Servus.Akka.IO;

namespace TurboHTTP.Streams.Stages;

internal interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(IOutputItem item);
    void OnWarning(string message);
    void OnReconnectFailed();
}