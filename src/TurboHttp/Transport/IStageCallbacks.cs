using System;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Exposes stage primitives that <see cref="ITransportHandler"/> implementations need
/// to interact with the enclosing <see cref="ConnectionStage"/> event loop.
/// Implemented directly by <c>ConnectionStage.Logic</c>.
/// </summary>
internal interface IStageCallbacks
{
    /// <summary>Push <paramref name="item"/> downstream, or enqueue it if the outlet is not yet available.</summary>
    void PushOutput(IInputItem item);

    /// <summary>TryPull the stage inlet to request the next upstream element.</summary>
    void SignalPullInput();

    /// <summary>Returns <c>true</c> when the outlet is available for an immediate push.</summary>
    bool IsOutputAvailable();

    /// <summary>Returns <c>true</c> when the inlet has been closed by upstream.</summary>
    bool IsInputClosed();

    /// <summary>Returns <c>true</c> when the inlet has already been pulled and is awaiting an element.</summary>
    bool HasInputBeenPulled();

    /// <summary>Schedule the connect-acquisition timeout timer.</summary>
    void ScheduleConnectTimeout(TimeSpan timeout);

    /// <summary>Cancel the connect-acquisition timeout timer.</summary>
    void CancelConnectTimeout();

    /// <summary>Request that the stage completes gracefully.</summary>
    void RequestCompleteStage();

    /// <summary>Emit a warning-level log message through the stage's logging adapter.</summary>
    void LogWarning(string format, params object[] args);

    /// <summary>
    /// Wraps <paramref name="handler"/> in an Akka async callback so it can be invoked safely
    /// from outside the stage event loop (e.g. from <c>Task</c> continuations).
    /// </summary>
    Action<T> GetAsyncCallback<T>(Action<T> handler);

    /// <summary>
    /// Wraps <paramref name="handler"/> in a parameterless Akka async callback.
    /// </summary>
    Action GetAsyncCallback(Action handler);

    /// <summary>
    /// Clears the pending output queue, discarding all buffered inbound items.
    /// Used by transport handlers to discard stale items after a new connection is established.
    /// </summary>
    void ClearPendingOutput();
}
