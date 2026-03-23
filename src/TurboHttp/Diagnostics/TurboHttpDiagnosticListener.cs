using System;
using System.Diagnostics;
using System.Net.Http;

namespace TurboHttp.Diagnostics;

/// <summary>
/// Provides <see cref="DiagnosticListener"/> events for TurboHttp.
/// Subscribe via <c>DiagnosticListener.AllListeners.Subscribe()</c> and filter
/// for the <see cref="ListenerName"/> source.
/// </summary>
public static class TurboHttpDiagnosticListener
{
    public const string ListenerName = "TurboHttp";

    public const string RequestStartEvent = "TurboHttp.Request.Start";
    public const string RequestStopEvent = "TurboHttp.Request.Stop";
    public const string RequestFailedEvent = "TurboHttp.Request.Failed";
    public const string ConnectionOpenedEvent = "TurboHttp.Connection.Opened";
    public const string ConnectionClosedEvent = "TurboHttp.Connection.Closed";

    /// <summary>
    /// Shared <see cref="DiagnosticListener"/> instance for all TurboHttp events.
    /// </summary>
    public static DiagnosticListener Source { get; } = new(ListenerName);

    /// <summary>
    /// Fires <c>TurboHttp.Request.Start</c> with the outgoing request as payload.
    /// No-op when no subscriber is attached.
    /// </summary>
    public static void OnRequestStart(HttpRequestMessage request)
    {
        if (Source.IsEnabled(RequestStartEvent))
        {
            Source.Write(RequestStartEvent, request);
        }
    }

    /// <summary>
    /// Fires <c>TurboHttp.Request.Stop</c> with the response and elapsed duration.
    /// No-op when no subscriber is attached.
    /// </summary>
    public static void OnRequestStop(HttpResponseMessage response, TimeSpan duration)
    {
        if (Source.IsEnabled(RequestStopEvent))
        {
            Source.Write(RequestStopEvent, new { Response = response, Duration = duration });
        }
    }

    /// <summary>
    /// Fires <c>TurboHttp.Request.Failed</c> with the exception that caused the failure.
    /// No-op when no subscriber is attached.
    /// </summary>
    public static void OnRequestFailed(Exception exception)
    {
        if (Source.IsEnabled(RequestFailedEvent))
        {
            Source.Write(RequestFailedEvent, exception);
        }
    }

    /// <summary>
    /// Fires <c>TurboHttp.Connection.Opened</c> with connection details.
    /// No-op when no subscriber is attached.
    /// </summary>
    public static void OnConnectionOpened(string host, int port, string protocol)
    {
        if (Source.IsEnabled(ConnectionOpenedEvent))
        {
            Source.Write(ConnectionOpenedEvent, new { Host = host, Port = port, Protocol = protocol });
        }
    }

    /// <summary>
    /// Fires <c>TurboHttp.Connection.Closed</c> with connection details and lifetime.
    /// No-op when no subscriber is attached.
    /// </summary>
    public static void OnConnectionClosed(string host, int port, TimeSpan duration)
    {
        if (Source.IsEnabled(ConnectionClosedEvent))
        {
            Source.Write(ConnectionClosedEvent, new { Host = host, Port = port, Duration = duration });
        }
    }
}
