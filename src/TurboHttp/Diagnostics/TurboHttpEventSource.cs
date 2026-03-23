using System.Diagnostics.Tracing;

namespace TurboHttp.Diagnostics;

/// <summary>
/// ETW/EventPipe event source for TurboHttp diagnostics.
/// Collect events via <c>dotnet-trace --providers TurboHttp</c>.
/// </summary>
[EventSource(Name = SourceName)]
public sealed class TurboHttpEventSource : EventSource
{
    public const string SourceName = "TurboHttp";

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static TurboHttpEventSource Log { get; } = new();

    private TurboHttpEventSource() : base(SourceName)
    {
    }

    #region Keywords

    /// <summary>
    /// Keywords for filtering event categories.
    /// </summary>
    public static class Keywords
    {
        public const EventKeywords Connection = (EventKeywords)0x0001;
        public const EventKeywords Request = (EventKeywords)0x0002;
        public const EventKeywords Protocol = (EventKeywords)0x0004;
        public const EventKeywords Cache = (EventKeywords)0x0008;
    }

    #endregion

    #region Connection Events (1–2)

    [Event(1, Message = "Connection opened: {0}:{1} ({2})", Level = EventLevel.Informational, Keywords = Keywords.Connection)]
    public void ConnectionOpened(string host, int port, string protocol)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Connection))
        {
            WriteEvent(1, host, port, protocol);
        }
    }

    [Event(2, Message = "Connection closed: {0}:{1} ({2}ms)", Level = EventLevel.Informational, Keywords = Keywords.Connection)]
    public void ConnectionClosed(string host, int port, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Connection))
        {
            WriteEvent(2, host, port, durationMs);
        }
    }

    #endregion

    #region Request Events (3–5)

    [Event(3, Message = "Request started: {0} {1}", Level = EventLevel.Informational, Keywords = Keywords.Request)]
    public void RequestStart(string method, string uri)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Request))
        {
            WriteEvent(3, method, uri);
        }
    }

    [Event(4, Message = "Request completed: {0} ({1}ms)", Level = EventLevel.Informational, Keywords = Keywords.Request)]
    public void RequestStop(int statusCode, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Request))
        {
            WriteEvent(4, statusCode, durationMs);
        }
    }

    [Event(5, Message = "Request failed: {0} — {1}", Level = EventLevel.Warning, Keywords = Keywords.Request)]
    public void RequestFailed(string exceptionType, string message)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Request))
        {
            WriteEvent(5, exceptionType, message);
        }
    }

    #endregion

    #region Protocol Events (6–8)

    [Event(6, Message = "Headers decoded: count={0} compressed={1} decompressed={2}", Level = EventLevel.Verbose, Keywords = Keywords.Protocol)]
    public void HeadersDecoded(int headerCount, int compressedSize, int decompressedSize)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Protocol))
        {
            WriteEvent(6, headerCount, compressedSize, decompressedSize);
        }
    }

    [Event(7, Message = "Frame sent: {0} stream={1} length={2}", Level = EventLevel.Verbose, Keywords = Keywords.Protocol)]
    public void FrameSent(string frameType, int streamId, int length)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Protocol))
        {
            WriteEvent(7, frameType, streamId, length);
        }
    }

    [Event(8, Message = "Frame received: {0} stream={1} length={2}", Level = EventLevel.Verbose, Keywords = Keywords.Protocol)]
    public void FrameReceived(string frameType, int streamId, int length)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Protocol))
        {
            WriteEvent(8, frameType, streamId, length);
        }
    }

    #endregion

    #region Cache Events (9–10)

    [Event(9, Message = "Cache hit: {0}", Level = EventLevel.Informational, Keywords = Keywords.Cache)]
    public void CacheHit(string uri)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Cache))
        {
            WriteEvent(9, uri);
        }
    }

    [Event(10, Message = "Cache miss: {0}", Level = EventLevel.Informational, Keywords = Keywords.Cache)]
    public void CacheMiss(string uri)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Cache))
        {
            WriteEvent(10, uri);
        }
    }

    #endregion

    #region Retry & Redirect Events (11–12)

    [Event(11, Message = "Retry attempt: {0} {1} (attempt {2})", Level = EventLevel.Warning, Keywords = Keywords.Request)]
    public void RetryAttempt(string method, string uri, int attempt)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Request))
        {
            WriteEvent(11, method, uri, attempt);
        }
    }

    [Event(12, Message = "Redirect followed: {0} → {2} (HTTP {1})", Level = EventLevel.Informational, Keywords = Keywords.Request)]
    public void RedirectFollowed(string uri, int statusCode, string location)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Request))
        {
            WriteEvent(12, uri, statusCode, location);
        }
    }

    #endregion
}
