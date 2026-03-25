using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

public sealed class TurboHttpEventSourceTests : IDisposable
{
    private readonly TestEventListener _listener;

    public TurboHttpEventSourceTests()
    {
        _listener = new TestEventListener();
        _listener.EnableEvents(TurboHttpEventSource.Log, EventLevel.Verbose, EventKeywords.All);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    // ── EventSource metadata ────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-001: EventSource has name TurboHttp")]
    public void EventSource_HasCorrectName()
    {
        Assert.Equal("TurboHttp", TurboHttpEventSource.SourceName);
        Assert.Equal("TurboHttp", TurboHttpEventSource.Log.Name);
    }

    [Fact(DisplayName = "Diagnostics-ES-002: EventSource singleton is not null")]
    public void EventSource_Singleton_IsNotNull()
    {
        Assert.NotNull(TurboHttpEventSource.Log);
    }

    // ── ConnectionOpened event (1) ──────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-003: ConnectionOpened fires event with host, port, protocol")]
    public void ConnectionOpened_Fires_EventWithPayload()
    {
        _listener.Clear();
        TurboHttpEventSource.Log.ConnectionOpened("example.com", 443, "HTTP/2");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 1);
        Assert.Equal("example.com", evt.Payload![0]);
        Assert.Equal(443, evt.Payload[1]);
        Assert.Equal("HTTP/2", evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-004: ConnectionOpened has Informational level")]
    public void ConnectionOpened_HasInformationalLevel()
    {
        _listener.Clear();
        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 1);
        Assert.Equal(EventLevel.Informational, evt.Level);
    }

    [Fact(DisplayName = "Diagnostics-ES-005: ConnectionOpened has Connection keyword")]
    public void ConnectionOpened_HasConnectionKeyword()
    {
        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 1);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Connection));
    }

    // ── ConnectionClosed event (2) ──────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-006: ConnectionClosed fires event with host, port, durationMs")]
    public void ConnectionClosed_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.ConnectionClosed("example.com", 443, 1500.5);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 2);
        Assert.Equal("example.com", evt.Payload![0]);
        Assert.Equal(443, evt.Payload[1]);
        Assert.Equal(1500.5, evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-007: ConnectionClosed has Informational level")]
    public void ConnectionClosed_HasInformationalLevel()
    {
        TurboHttpEventSource.Log.ConnectionClosed("test.com", 80, 100.0);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 2);
        Assert.Equal(EventLevel.Informational, evt.Level);
    }

    // ── RequestStart event (3) ──────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-008: RequestStart fires event with method and uri")]
    public void RequestStart_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.RequestStart("GET", "https://example.com/path");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 3);
        Assert.Equal("GET", evt.Payload![0]);
        Assert.Equal("https://example.com/path", evt.Payload[1]);
    }

    [Fact(DisplayName = "Diagnostics-ES-009: RequestStart has Informational level and Request keyword")]
    public void RequestStart_HasInformationalLevelAndRequestKeyword()
    {
        TurboHttpEventSource.Log.RequestStart("POST", "https://api.example.com/data");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 3);
        Assert.Equal(EventLevel.Informational, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Request));
    }

    // ── RequestStop event (4) ───────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-010: RequestStop fires event with statusCode and durationMs")]
    public void RequestStop_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.RequestStop(200, 125.5);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 4);
        Assert.Equal(200, evt.Payload![0]);
        Assert.Equal(125.5, evt.Payload[1]);
    }

    [Fact(DisplayName = "Diagnostics-ES-011: RequestStop has Informational level")]
    public void RequestStop_HasInformationalLevel()
    {
        TurboHttpEventSource.Log.RequestStop(404, 50.0);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 4);
        Assert.Equal(EventLevel.Informational, evt.Level);
    }

    // ── RequestFailed event (5) ─────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-012: RequestFailed fires event with exceptionType and message")]
    public void RequestFailed_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.RequestFailed("HttpRequestException", "Connection refused");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 5);
        Assert.Equal("HttpRequestException", evt.Payload![0]);
        Assert.Equal("Connection refused", evt.Payload[1]);
    }

    [Fact(DisplayName = "Diagnostics-ES-013: RequestFailed has Warning level")]
    public void RequestFailed_HasWarningLevel()
    {
        TurboHttpEventSource.Log.RequestFailed("TimeoutException", "Timed out");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 5);
        Assert.Equal(EventLevel.Warning, evt.Level);
    }

    // ── HeadersDecoded event (6) ────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-014: HeadersDecoded fires event with header count and sizes")]
    public void HeadersDecoded_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.HeadersDecoded(5, 128, 256);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 6);
        Assert.Equal(5, evt.Payload![0]);
        Assert.Equal(128, evt.Payload[1]);
        Assert.Equal(256, evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-015: HeadersDecoded has Verbose level and Protocol keyword")]
    public void HeadersDecoded_HasVerboseLevelAndProtocolKeyword()
    {
        TurboHttpEventSource.Log.HeadersDecoded(3, 64, 128);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 6);
        Assert.Equal(EventLevel.Verbose, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Protocol));
    }

    // ── FrameSent event (7) ─────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-016: FrameSent fires event with frameType, streamId, length")]
    public void FrameSent_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.FrameSent("HEADERS", 1, 64);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 7);
        Assert.Equal("HEADERS", evt.Payload![0]);
        Assert.Equal(1, evt.Payload[1]);
        Assert.Equal(64, evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-017: FrameSent has Verbose level and Protocol keyword")]
    public void FrameSent_HasVerboseLevelAndProtocolKeyword()
    {
        TurboHttpEventSource.Log.FrameSent("DATA", 3, 1024);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 7);
        Assert.Equal(EventLevel.Verbose, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Protocol));
    }
    
    // ── CacheHit event (9) ──────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-019: CacheHit fires event with uri")]
    public void CacheHit_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.CacheHit("https://example.com/cached");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 9);
        Assert.Equal("https://example.com/cached", evt.Payload![0]);
    }

    [Fact(DisplayName = "Diagnostics-ES-020: CacheHit has Informational level and Cache keyword")]
    public void CacheHit_HasInformationalLevelAndCacheKeyword()
    {
        TurboHttpEventSource.Log.CacheHit("https://example.com/cached");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 9);
        Assert.Equal(EventLevel.Informational, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Cache));
    }

    // ── CacheMiss event (10) ────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-021: CacheMiss fires event with uri")]
    public void CacheMiss_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.CacheMiss("https://example.com/uncached");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 10);
        Assert.Equal("https://example.com/uncached", evt.Payload![0]);
    }

    [Fact(DisplayName = "Diagnostics-ES-022: CacheMiss has Informational level and Cache keyword")]
    public void CacheMiss_HasInformationalLevelAndCacheKeyword()
    {
        TurboHttpEventSource.Log.CacheMiss("https://example.com/uncached");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 10);
        Assert.Equal(EventLevel.Informational, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Cache));
    }

    // ── RetryAttempt event (11) ─────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-023: RetryAttempt fires event with method, uri, attempt")]
    public void RetryAttempt_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.RetryAttempt("GET", "https://example.com/retry", 2);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 11);
        Assert.Equal("GET", evt.Payload![0]);
        Assert.Equal("https://example.com/retry", evt.Payload[1]);
        Assert.Equal(2, evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-024: RetryAttempt has Warning level and Request keyword")]
    public void RetryAttempt_HasWarningLevelAndRequestKeyword()
    {
        TurboHttpEventSource.Log.RetryAttempt("POST", "https://example.com/retry", 1);

        var evt = Assert.Single(_listener.Events, e => e.EventId == 11);
        Assert.Equal(EventLevel.Warning, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Request));
    }

    // ── RedirectFollowed event (12) ─────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-025: RedirectFollowed fires event with uri, statusCode, location")]
    public void RedirectFollowed_Fires_EventWithPayload()
    {
        TurboHttpEventSource.Log.RedirectFollowed("https://example.com/old", 301, "https://example.com/new");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 12);
        Assert.Equal("https://example.com/old", evt.Payload![0]);
        Assert.Equal(301, evt.Payload[1]);
        Assert.Equal("https://example.com/new", evt.Payload[2]);
    }

    [Fact(DisplayName = "Diagnostics-ES-026: RedirectFollowed has Informational level and Request keyword")]
    public void RedirectFollowed_HasInformationalLevelAndRequestKeyword()
    {
        TurboHttpEventSource.Log.RedirectFollowed("https://a.com", 302, "https://b.com");

        var evt = Assert.Single(_listener.Events, e => e.EventId == 12);
        Assert.Equal(EventLevel.Informational, evt.Level);
        Assert.True(evt.Keywords.HasFlag(TurboHttpEventSource.Keywords.Request));
    }

    // ── Keyword filtering ───────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-027: Connection keyword filters only connection events")]
    public void ConnectionKeyword_FiltersOnlyConnectionEvents()
    {
        // Re-enable with only Connection keyword
        _listener.DisableEvents(TurboHttpEventSource.Log);
        _listener.Clear();
        _listener.EnableEvents(TurboHttpEventSource.Log, EventLevel.Verbose, TurboHttpEventSource.Keywords.Connection);

        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");
        TurboHttpEventSource.Log.RequestStart("GET", "https://test.com/");
        TurboHttpEventSource.Log.CacheHit("https://test.com/cached");
        TurboHttpEventSource.Log.FrameSent("HEADERS", 1, 64);

        Assert.Single(_listener.Events);
        Assert.Equal(1, _listener.Events[0].EventId); // ConnectionOpened
    }

    [Fact(DisplayName = "Diagnostics-ES-028: Request keyword filters only request events")]
    public void RequestKeyword_FiltersOnlyRequestEvents()
    {
        _listener.DisableEvents(TurboHttpEventSource.Log);
        _listener.Clear();
        _listener.EnableEvents(TurboHttpEventSource.Log, EventLevel.Verbose, TurboHttpEventSource.Keywords.Request);

        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");
        TurboHttpEventSource.Log.RequestStart("GET", "https://test.com/");
        TurboHttpEventSource.Log.RequestStop(200, 50.0);
        TurboHttpEventSource.Log.CacheHit("https://test.com/cached");

        Assert.Equal(2, _listener.Events.Count);
        Assert.All(_listener.Events, e =>
            Assert.True(e.Keywords.HasFlag(TurboHttpEventSource.Keywords.Request)));
    }

    // ── Zero events when no listener ────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-029: No events fire when EventListener is not enabled")]
    public void NoEvents_WhenListenerNotEnabled()
    {
        _listener.DisableEvents(TurboHttpEventSource.Log);
        _listener.Clear();

        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");
        TurboHttpEventSource.Log.ConnectionClosed("test.com", 80, 100.0);
        TurboHttpEventSource.Log.RequestStart("GET", "https://test.com/");
        TurboHttpEventSource.Log.RequestStop(200, 50.0);
        TurboHttpEventSource.Log.RequestFailed("Exception", "error");
        TurboHttpEventSource.Log.HeadersDecoded(5, 128, 256);
        TurboHttpEventSource.Log.FrameSent("HEADERS", 1, 64);
        TurboHttpEventSource.Log.FrameReceived("DATA", 1, 1024);
        TurboHttpEventSource.Log.CacheHit("https://test.com/");
        TurboHttpEventSource.Log.CacheMiss("https://test.com/other");
        TurboHttpEventSource.Log.RetryAttempt("GET", "https://test.com/", 1);
        TurboHttpEventSource.Log.RedirectFollowed("https://test.com/old", 301, "https://test.com/new");

        Assert.Empty(_listener.Events);
    }

    // ── Full lifecycle ──────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-030: Full request lifecycle emits events in order")]
    public void FullLifecycle_EmitsEventsInOrder()
    {
        _listener.Clear();

        // Connection opened
        TurboHttpEventSource.Log.ConnectionOpened("example.com", 443, "HTTP/2");
        // Request start
        TurboHttpEventSource.Log.RequestStart("GET", "https://example.com/resource");
        // Frame sent (headers)
        TurboHttpEventSource.Log.FrameSent("HEADERS", 1, 64);
        // Frame received (headers)
        TurboHttpEventSource.Log.FrameReceived("HEADERS", 1, 128);
        // Frame received (data)
        TurboHttpEventSource.Log.FrameReceived("DATA", 1, 4096);
        // Request complete
        TurboHttpEventSource.Log.RequestStop(200, 75.5);

        Assert.Equal(6, _listener.Events.Count);
        Assert.Equal(1, _listener.Events[0].EventId); // ConnectionOpened
        Assert.Equal(3, _listener.Events[1].EventId); // RequestStart
        Assert.Equal(7, _listener.Events[2].EventId); // FrameSent
        Assert.Equal(8, _listener.Events[3].EventId); // FrameReceived (headers)
        Assert.Equal(8, _listener.Events[4].EventId); // FrameReceived (data)
        Assert.Equal(4, _listener.Events[5].EventId); // RequestStop
    }

    // ── Level filtering ─────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-ES-031: Informational level excludes Verbose protocol events")]
    public void InformationalLevel_ExcludesVerboseEvents()
    {
        _listener.DisableEvents(TurboHttpEventSource.Log);
        _listener.Clear();
        _listener.EnableEvents(TurboHttpEventSource.Log, EventLevel.Informational, EventKeywords.All);

        TurboHttpEventSource.Log.ConnectionOpened("test.com", 80, "HTTP/1.1");
        TurboHttpEventSource.Log.RequestStart("GET", "https://test.com/");
        TurboHttpEventSource.Log.HeadersDecoded(5, 128, 256); // Verbose — should be excluded
        TurboHttpEventSource.Log.FrameSent("HEADERS", 1, 64); // Verbose — should be excluded
        TurboHttpEventSource.Log.FrameReceived("DATA", 1, 1024); // Verbose — should be excluded
        TurboHttpEventSource.Log.RequestStop(200, 50.0);

        // Only ConnectionOpened, RequestStart, and RequestStop should fire
        Assert.Equal(3, _listener.Events.Count);
        Assert.DoesNotContain(_listener.Events, e => e.Level == EventLevel.Verbose);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class TestEventListener : EventListener
    {
        private readonly object _lock = new();
        public List<EventWrittenEventArgs> Events { get; } = new();

        public void Clear()
        {
            lock (_lock)
            {
                Events.Clear();
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == TurboHttpEventSource.SourceName)
            {
                lock (_lock)
                {
                    Events.Add(eventData);
                }
            }
        }
    }
}
