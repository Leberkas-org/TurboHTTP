using System.Diagnostics;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Provides <see cref="DiagnosticSource"/> events for TurboHTTP, following the same
/// event patterns as <c>System.Net.Http</c>'s DiagnosticListener.
/// <para>
/// Subscribers filter via <c>DiagnosticListener.AllListeners.Subscribe(...)</c>
/// with listener name <c>"TurboHTTP"</c>.
/// </para>
/// <para>
/// Events emitted:
/// <list type="bullet">
///   <item><c>TurboHTTP.HttpRequestOut.Start</c> — request about to be sent</item>
///   <item><c>TurboHTTP.HttpRequestOut.Stop</c> — request completed (success or failure)</item>
///   <item><c>TurboHTTP.Exception</c> — exception during request processing</item>
/// </list>
/// </para>
/// </summary>
public static class TurboHttpDiagnosticSource
{
    /// <summary>
    /// The <see cref="DiagnosticListener"/> name. Subscribe with
    /// <c>DiagnosticListener.AllListeners.Subscribe(observer)</c>
    /// and filter for this name.
    /// </summary>
    public const string ListenerName = "TurboHTTP";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>
    /// Returns <c>true</c> when at least one subscriber is listening for request events.
    /// Use this to guard payload construction.
    /// </summary>
    public static bool IsEnabled => Listener.IsEnabled("TurboHTTP.HttpRequestOut");

    /// <summary>
    /// Emits the <c>TurboHTTP.HttpRequestOut.Start</c> event.
    /// </summary>
    public static void OnRequestStart(HttpRequestMessage request)
    {
        if (Listener.IsEnabled("TurboHTTP.HttpRequestOut.Start"))
        {
            Listener.Write("TurboHTTP.HttpRequestOut.Start", new { Request = request });
        }
    }

    /// <summary>
    /// Emits the <c>TurboHTTP.HttpRequestOut.Stop</c> event.
    /// </summary>
    /// <param name="request">The original request message.</param>
    /// <param name="response">The response, or <c>null</c> if the request failed.</param>
    /// <param name="taskStatus">The final <see cref="TaskStatus"/> of the request.</param>
    public static void OnRequestStop(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        TaskStatus taskStatus)
    {
        if (Listener.IsEnabled("TurboHTTP.HttpRequestOut"))
        {
            Listener.Write("TurboHTTP.HttpRequestOut.Stop", new
            {
                Request = request,
                Response = response,
                RequestTaskStatus = taskStatus,
            });
        }
    }

    /// <summary>
    /// Emits the <c>TurboHTTP.Exception</c> event.
    /// </summary>
    public static void OnException(HttpRequestMessage request, Exception exception)
    {
        if (Listener.IsEnabled("TurboHTTP.Exception"))
        {
            Listener.Write("TurboHTTP.Exception", new
            {
                Request = request,
                Exception = exception,
            });
        }
    }
}
