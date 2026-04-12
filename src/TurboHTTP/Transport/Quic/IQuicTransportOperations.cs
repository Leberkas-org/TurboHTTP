using Akka.Event;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Callback interface for the stage Logic to receive effects from the QUIC transport state machine.
/// The Logic implements this and translates calls to Akka Push/Pull/Timer operations.
/// </summary>
internal interface IQuicTransportOperations
{
    void OnPushOutput(IInputItem item);
    void OnSignalPullInput();
    void OnFailStage(Exception ex);
    void OnCompleteStage();
    void OnScheduleTimer(string key, TimeSpan delay);
    void OnCancelTimer(string key);
    ILoggingAdapter Log { get; }
}
