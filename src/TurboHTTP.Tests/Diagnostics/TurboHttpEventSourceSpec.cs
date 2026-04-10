using System.Diagnostics.Tracing;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

public sealed class TurboHttpEventSourceSpec : IDisposable
{
    private readonly TestEventListener _listener;

    public TurboHttpEventSourceSpec()
    {
        _listener = new TestEventListener();
        _listener.EnableEvents(TurboHttpEventSource.Instance, EventLevel.Verbose, EventKeywords.All);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public void EventSource_should_have_correct_name()
    {
        Assert.Equal("TurboHTTP", TurboHttpEventSource.Instance.Name);
    }

    [Fact]
    public void RequestStart_should_emit_event()
    {
        TurboHttpEventSource.Instance.RequestStart("GET", "https://example.com/");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 1);
        Assert.NotNull(evt);
        Assert.Equal("GET", evt.Payload?[0]);
    }

    [Fact]
    public void RequestStop_should_emit_event()
    {
        TurboHttpEventSource.Instance.RequestStop("GET", 200, 42.5);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 2);
        Assert.NotNull(evt);
        Assert.Equal(200, evt.Payload?[1]);
    }

    [Fact]
    public void RequestFailed_should_emit_event()
    {
        TurboHttpEventSource.Instance.RequestFailed("GET", "https://example.com/", "HttpRequestException");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 3);
        Assert.NotNull(evt);
        Assert.Equal("HttpRequestException", evt.Payload?[2]);
    }

    [Fact]
    public void ConnectionStart_should_emit_event()
    {
        TurboHttpEventSource.Instance.ConnectionStart("example.com", 443);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 10);
        Assert.NotNull(evt);
        Assert.Equal("example.com", evt.Payload?[0]);
    }

    [Fact]
    public void ConnectionStop_should_emit_event()
    {
        TurboHttpEventSource.Instance.ConnectionStop("example.com", 443, 1234.5);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 11);
        Assert.NotNull(evt);
    }

    [Fact]
    public void DnsLookupStart_should_emit_event()
    {
        TurboHttpEventSource.Instance.DnsLookupStart("example.com");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 20);
        Assert.NotNull(evt);
        Assert.Equal("example.com", evt.Payload?[0]);
    }

    [Fact]
    public void DnsLookupStop_should_emit_event()
    {
        TurboHttpEventSource.Instance.DnsLookupStop("example.com", 5.2);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 21);
        Assert.NotNull(evt);
    }

    [Fact]
    public void TlsHandshakeStart_should_emit_event()
    {
        TurboHttpEventSource.Instance.TlsHandshakeStart("example.com");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 30);
        Assert.NotNull(evt);
    }

    [Fact]
    public void TlsHandshakeStop_should_emit_event()
    {
        TurboHttpEventSource.Instance.TlsHandshakeStop("example.com", 15.3);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 31);
        Assert.NotNull(evt);
    }

    [Fact]
    public void Redirect_should_emit_event()
    {
        TurboHttpEventSource.Instance.Redirect(301, "https://example.com/new");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 40);
        Assert.NotNull(evt);
    }

    [Fact]
    public void RetryAttempt_should_emit_event()
    {
        TurboHttpEventSource.Instance.RetryAttempt(2);

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 50);
        Assert.NotNull(evt);
        Assert.Equal(2, evt.Payload?[0]);
    }

    [Fact]
    public void CacheHit_should_emit_event()
    {
        TurboHttpEventSource.Instance.CacheHit("https://example.com/cached");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 60);
        Assert.NotNull(evt);
    }

    [Fact]
    public void CacheMiss_should_emit_event()
    {
        TurboHttpEventSource.Instance.CacheMiss("https://example.com/uncached");

        var evt = _listener.Events.FirstOrDefault(e => e.EventId == 61);
        Assert.NotNull(evt);
    }

    private sealed class TestEventListener : EventListener
    {
        public List<EventWrittenEventArgs> Events { get; } = [];

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            Events.Add(eventData);
        }
    }
}
