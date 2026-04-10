using System.Diagnostics.Tracing;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// High-performance <see cref="EventSource"/> for TurboHTTP.
/// Enables zero-alloc structured logging for production diagnostics via ETW (Windows),
/// EventPipe, or <c>dotnet-trace</c>.
/// <para>
/// Enable with: <c>dotnet-trace collect -p {pid} --providers TurboHTTP</c>
/// </para>
/// </summary>
[EventSource(Name = "TurboHTTP")]
public sealed class TurboHttpEventSource : EventSource
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly TurboHttpEventSource Instance = new();

    private TurboHttpEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
    {
    }

    // --- Request lifecycle ---

    [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start,
        Keywords = Keywords.Request, Message = "Request started: {0} {1}")]
    public void RequestStart(string method, string url)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Request))
        {
            WriteEvent(1, method, url);
        }
    }

    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop,
        Keywords = Keywords.Request, Message = "Request completed: {0} {1} {2}ms")]
    public void RequestStop(string method, int statusCode, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Request))
        {
            WriteEvent(2, method, statusCode, durationMs);
        }
    }

    [Event(3, Level = EventLevel.Error, Keywords = Keywords.Request,
        Message = "Request failed: {0} {1} — {2}")]
    public void RequestFailed(string method, string url, string exceptionType)
    {
        if (IsEnabled(EventLevel.Error, Keywords.Request))
        {
            WriteEvent(3, method, url, exceptionType);
        }
    }

    // --- Connection lifecycle ---

    [Event(10, Level = EventLevel.Informational, Opcode = EventOpcode.Start,
        Keywords = Keywords.Connection, Message = "Connection opening: {0}:{1}")]
    public void ConnectionStart(string host, int port)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Connection))
        {
            WriteEvent(10, host, port);
        }
    }

    [Event(11, Level = EventLevel.Informational, Opcode = EventOpcode.Stop,
        Keywords = Keywords.Connection, Message = "Connection closed: {0}:{1} ({2}ms)")]
    public void ConnectionStop(string host, int port, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Connection))
        {
            WriteEvent(11, host, port, durationMs);
        }
    }

    // --- DNS ---

    [Event(20, Level = EventLevel.Informational, Opcode = EventOpcode.Start,
        Keywords = Keywords.Dns, Message = "DNS lookup: {0}")]
    public void DnsLookupStart(string hostname)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Dns))
        {
            WriteEvent(20, hostname);
        }
    }

    [Event(21, Level = EventLevel.Informational, Opcode = EventOpcode.Stop,
        Keywords = Keywords.Dns, Message = "DNS lookup completed: {0} ({1}ms)")]
    public void DnsLookupStop(string hostname, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Dns))
        {
            WriteEvent(21, hostname, durationMs);
        }
    }

    // --- TLS ---

    [Event(30, Level = EventLevel.Informational, Opcode = EventOpcode.Start,
        Keywords = Keywords.Tls, Message = "TLS handshake: {0}")]
    public void TlsHandshakeStart(string host)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Tls))
        {
            WriteEvent(30, host);
        }
    }

    [Event(31, Level = EventLevel.Informational, Opcode = EventOpcode.Stop,
        Keywords = Keywords.Tls, Message = "TLS handshake completed: {0} ({1}ms)")]
    public void TlsHandshakeStop(string host, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Tls))
        {
            WriteEvent(31, host, durationMs);
        }
    }

    // --- Redirect ---

    [Event(40, Level = EventLevel.Informational, Keywords = Keywords.Redirect,
        Message = "Redirect: {0} → {1}")]
    public void Redirect(int statusCode, string targetUrl)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Redirect))
        {
            WriteEvent(40, statusCode, targetUrl);
        }
    }

    // --- Retry ---

    [Event(50, Level = EventLevel.Warning, Keywords = Keywords.Retry,
        Message = "Retry attempt {0}")]
    public void RetryAttempt(int attemptNumber)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Retry))
        {
            WriteEvent(50, attemptNumber);
        }
    }

    // --- Cache ---

    [Event(60, Level = EventLevel.Informational, Keywords = Keywords.Cache,
        Message = "Cache hit: {0}")]
    public void CacheHit(string url)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Cache))
        {
            WriteEvent(60, url);
        }
    }

    [Event(61, Level = EventLevel.Informational, Keywords = Keywords.Cache,
        Message = "Cache miss: {0}")]
    public void CacheMiss(string url)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Cache))
        {
            WriteEvent(61, url);
        }
    }

    /// <summary>
    /// ETW keyword categories for filtering event streams.
    /// </summary>
    public static class Keywords
    {
        public const EventKeywords Request = (EventKeywords)0x01;
        public const EventKeywords Connection = (EventKeywords)0x02;
        public const EventKeywords Dns = (EventKeywords)0x04;
        public const EventKeywords Tls = (EventKeywords)0x08;
        public const EventKeywords Redirect = (EventKeywords)0x10;
        public const EventKeywords Retry = (EventKeywords)0x20;
        public const EventKeywords Cache = (EventKeywords)0x40;
    }
}
