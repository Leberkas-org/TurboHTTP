using Akka.Event;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(ITransportOutbound item);
    void OnWarning(string message);
    void OnReconnectFailed();
    void OnScheduleTimer(string name, TimeSpan duration);
    void OnCancelTimer(string name);
    void OnComplete();
    void OnFail(Exception exception);
    ILoggingAdapter Log { get; }
}