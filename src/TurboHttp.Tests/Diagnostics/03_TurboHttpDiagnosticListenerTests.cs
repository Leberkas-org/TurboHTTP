using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

public sealed class TurboHttpDiagnosticListenerTests : IDisposable
{
    private readonly ConcurrentBag<(string Name, object? Value)> _events = new();
    private readonly IDisposable _allListenersSubscription;
    private IDisposable? _listenerSubscription;

    public TurboHttpDiagnosticListenerTests()
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(new AllListenersObserver(this));
    }

    public void Dispose()
    {
        _listenerSubscription?.Dispose();
        _allListenersSubscription.Dispose();
    }

    // ── DiagnosticListener metadata ─────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-001: Source has name TurboHttp")]
    public void Source_HasCorrectName()
    {
        _events.Clear();
        Assert.Equal("TurboHttp", TurboHttpDiagnosticListener.ListenerName);
        Assert.Equal("TurboHttp", TurboHttpDiagnosticListener.Source.Name);
    }

    // ── RequestStart event ──────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-002: OnRequestStart fires TurboHttp.Request.Start event")]
    public void OnRequestStart_Fires_RequestStartEvent()
    {
        _events.Clear();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        TurboHttpDiagnosticListener.OnRequestStart(request);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStartEvent);
        Assert.Same(request, evt.Value);
    }

    [Fact(DisplayName = "Diagnostics-DL-003: OnRequestStart carries HttpRequestMessage as payload")]
    public void OnRequestStart_Carries_HttpRequestMessage()
    {
        _events.Clear();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");

        TurboHttpDiagnosticListener.OnRequestStart(request);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStartEvent);
        var payload = Assert.IsType<HttpRequestMessage>(evt.Value);
        Assert.Equal(HttpMethod.Post, payload.Method);
        Assert.Equal(new Uri("https://api.example.com/data"), payload.RequestUri);
    }

    // ── RequestStop event ───────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-004: OnRequestStop fires TurboHttp.Request.Stop event")]
    public void OnRequestStop_Fires_RequestStopEvent()
    {
        _events.Clear();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var duration = TimeSpan.FromMilliseconds(150);

        TurboHttpDiagnosticListener.OnRequestStop(response, duration);

        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStopEvent);
    }

    [Fact(DisplayName = "Diagnostics-DL-005: OnRequestStop payload contains Response and Duration")]
    public void OnRequestStop_Payload_ContainsResponseAndDuration()
    {
        _events.Clear();
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var duration = TimeSpan.FromMilliseconds(250);

        TurboHttpDiagnosticListener.OnRequestStop(response, duration);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStopEvent);
        Assert.NotNull(evt.Value);

        var responseProperty = evt.Value.GetType().GetProperty("Response");
        var durationProperty = evt.Value.GetType().GetProperty("Duration");
        Assert.NotNull(responseProperty);
        Assert.NotNull(durationProperty);
        Assert.Same(response, responseProperty.GetValue(evt.Value));
        Assert.Equal(duration, durationProperty.GetValue(evt.Value));
    }

    // ── RequestFailed event ─────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-006: OnRequestFailed fires TurboHttp.Request.Failed event")]
    public void OnRequestFailed_Fires_RequestFailedEvent()
    {
        _events.Clear();
        var exception = new HttpRequestException("Connection refused");

        TurboHttpDiagnosticListener.OnRequestFailed(exception);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestFailedEvent);
        Assert.Same(exception, evt.Value);
    }

    [Fact(DisplayName = "Diagnostics-DL-007: OnRequestFailed carries Exception as payload")]
    public void OnRequestFailed_Carries_Exception()
    {
        _events.Clear();
        var exception = new TimeoutException("Request timed out");

        TurboHttpDiagnosticListener.OnRequestFailed(exception);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestFailedEvent);
        var payload = Assert.IsType<TimeoutException>(evt.Value);
        Assert.Equal("Request timed out", payload.Message);
    }

    // ── ConnectionOpened event ──────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-008: OnConnectionOpened fires TurboHttp.Connection.Opened event")]
    public void OnConnectionOpened_Fires_ConnectionOpenedEvent()
    {
        _events.Clear();
        TurboHttpDiagnosticListener.OnConnectionOpened("example.com", 443, "HTTP/2");

        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionOpenedEvent);
    }

    [Fact(DisplayName = "Diagnostics-DL-009: OnConnectionOpened payload contains Host, Port, Protocol")]
    public void OnConnectionOpened_Payload_ContainsHostPortProtocol()
    {
        _events.Clear();
        TurboHttpDiagnosticListener.OnConnectionOpened("api.example.com", 8443, "HTTP/1.1");

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionOpenedEvent);
        Assert.NotNull(evt.Value);

        var hostProp = evt.Value.GetType().GetProperty("Host");
        var portProp = evt.Value.GetType().GetProperty("Port");
        var protocolProp = evt.Value.GetType().GetProperty("Protocol");
        Assert.NotNull(hostProp);
        Assert.NotNull(portProp);
        Assert.NotNull(protocolProp);
        Assert.Equal("api.example.com", hostProp.GetValue(evt.Value));
        Assert.Equal(8443, portProp.GetValue(evt.Value));
        Assert.Equal("HTTP/1.1", protocolProp.GetValue(evt.Value));
    }

    // ── ConnectionClosed event ──────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-010: OnConnectionClosed fires TurboHttp.Connection.Closed event")]
    public void OnConnectionClosed_Fires_ConnectionClosedEvent()
    {
        _events.Clear();
        TurboHttpDiagnosticListener.OnConnectionClosed("example.com", 443, TimeSpan.FromSeconds(30));

        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionClosedEvent);
    }

    [Fact(DisplayName = "Diagnostics-DL-011: OnConnectionClosed payload contains Host, Port, Duration")]
    public void OnConnectionClosed_Payload_ContainsHostPortDuration()
    {
        _events.Clear();
        var duration = TimeSpan.FromSeconds(42.5);

        TurboHttpDiagnosticListener.OnConnectionClosed("conn.example.com", 80, duration);

        var evt = Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionClosedEvent);
        Assert.NotNull(evt.Value);

        var hostProp = evt.Value.GetType().GetProperty("Host");
        var portProp = evt.Value.GetType().GetProperty("Port");
        var durationProp = evt.Value.GetType().GetProperty("Duration");
        Assert.NotNull(hostProp);
        Assert.NotNull(portProp);
        Assert.NotNull(durationProp);
        Assert.Equal("conn.example.com", hostProp.GetValue(evt.Value));
        Assert.Equal(80, portProp.GetValue(evt.Value));
        Assert.Equal(duration, durationProp.GetValue(evt.Value));
    }

    // ── Full lifecycle ──────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-012: Full request lifecycle emits Start then Stop events")]
    public void FullLifecycle_EmitsStartThenStop()
    {
        _events.Clear();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        TurboHttpDiagnosticListener.OnRequestStart(request);
        TurboHttpDiagnosticListener.OnRequestStop(response, TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, _events.Count);
        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStartEvent);
        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.RequestStopEvent);
    }

    [Fact(DisplayName = "Diagnostics-DL-013: Connection lifecycle emits Opened then Closed events")]
    public void ConnectionLifecycle_EmitsOpenedThenClosed()
    {
        _events.Clear();
        TurboHttpDiagnosticListener.OnConnectionOpened("example.com", 443, "HTTP/2");
        TurboHttpDiagnosticListener.OnConnectionClosed("example.com", 443, TimeSpan.FromSeconds(10));

        Assert.Equal(2, _events.Count);
        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionOpenedEvent);
        Assert.Single(_events, e => e.Name == TurboHttpDiagnosticListener.ConnectionClosedEvent);
    }

    // ── Event name constants ────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-014: Event name constants match expected values")]
    public void EventNameConstants_MatchExpectedValues()
    {
        _events.Clear();
        Assert.Equal("TurboHttp.Request.Start", TurboHttpDiagnosticListener.RequestStartEvent);
        Assert.Equal("TurboHttp.Request.Stop", TurboHttpDiagnosticListener.RequestStopEvent);
        Assert.Equal("TurboHttp.Request.Failed", TurboHttpDiagnosticListener.RequestFailedEvent);
        Assert.Equal("TurboHttp.Connection.Opened", TurboHttpDiagnosticListener.ConnectionOpenedEvent);
        Assert.Equal("TurboHttp.Connection.Closed", TurboHttpDiagnosticListener.ConnectionClosedEvent);
    }

    // ── Zero events when no listener subscribed ─────────────────────────

    [Fact(DisplayName = "Diagnostics-DL-015: No events fire when no listener is subscribed")]
    public void NoEvents_WhenNoListenerSubscribed()
    {
        _events.Clear();
        // Dispose our subscriptions so no listener is active
        _listenerSubscription?.Dispose();
        _listenerSubscription = null;
        _allListenersSubscription.Dispose();
        _events.Clear();

        // These calls should be no-ops because IsEnabled returns false
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        TurboHttpDiagnosticListener.OnRequestStart(request);
        TurboHttpDiagnosticListener.OnRequestStop(response, TimeSpan.FromMilliseconds(50));
        TurboHttpDiagnosticListener.OnRequestFailed(new Exception("test"));
        TurboHttpDiagnosticListener.OnConnectionOpened("example.com", 443, "HTTP/2");
        TurboHttpDiagnosticListener.OnConnectionClosed("example.com", 443, TimeSpan.FromSeconds(5));

        Assert.Empty(_events);
    }

    [Fact(DisplayName = "Diagnostics-DL-016: IsEnabled guards prevent allocation when unsubscribed")]
    public void IsEnabled_Guards_PreventAllocation()
    {
        _events.Clear();
        // Dispose subscriptions
        _listenerSubscription?.Dispose();
        _listenerSubscription = null;
        _allListenersSubscription.Dispose();

        // With no subscriber, IsEnabled should return false
        Assert.False(TurboHttpDiagnosticListener.Source.IsEnabled(TurboHttpDiagnosticListener.RequestStartEvent));
        Assert.False(TurboHttpDiagnosticListener.Source.IsEnabled(TurboHttpDiagnosticListener.RequestStopEvent));
        Assert.False(TurboHttpDiagnosticListener.Source.IsEnabled(TurboHttpDiagnosticListener.RequestFailedEvent));
        Assert.False(TurboHttpDiagnosticListener.Source.IsEnabled(TurboHttpDiagnosticListener.ConnectionOpenedEvent));
        Assert.False(TurboHttpDiagnosticListener.Source.IsEnabled(TurboHttpDiagnosticListener.ConnectionClosedEvent));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class AllListenersObserver : IObserver<DiagnosticListener>
    {
        private readonly TurboHttpDiagnosticListenerTests _parent;

        public AllListenersObserver(TurboHttpDiagnosticListenerTests parent)
        {
            _parent = parent;
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == TurboHttpDiagnosticListener.ListenerName)
            {
                _parent._listenerSubscription = value.Subscribe(new EventObserver(_parent));
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly TurboHttpDiagnosticListenerTests _parent;

        public EventObserver(TurboHttpDiagnosticListenerTests parent)
        {
            _parent = parent;
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            _parent._events.Add((value.Key, value.Value));
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
