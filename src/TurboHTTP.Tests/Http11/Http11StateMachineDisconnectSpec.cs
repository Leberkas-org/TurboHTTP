using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11StateMachineDisconnectSpec
{
    private static HttpRequestMessage MakeRequest(string uri = "http://example.com/") =>
        new(HttpMethod.Get, uri);

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedRequest(
        string uri = "http://example.com/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Options.Set(TurboClientCorrelation.Key, pending);
        request.Options.Set(TurboClientCorrelation.VersionKey, version);
        return (request, pending);
    }

    private static TransportBuffer CreateResponseBuffer(string responseText)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(responseText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Http11StateMachine_should_fail_inflight_on_abrupt_disconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 0 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Http11StateMachine_should_try_eof_decode_on_graceful_disconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());

        var response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nHello";
        sm.DecodeServerData(new TransportData(CreateResponseBuffer(response)));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Http11StateMachine_should_reconnect_on_disconnect_with_inflight()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });

        sm.OnRequest(MakeRequest());
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Contains(ops.Outbound, o => o is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Http11StateMachine_should_replay_buffered_requests_on_reconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });

        sm.OnRequest(MakeRequest());
        sm.OnRequest(MakeRequest("http://example.com/other"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportConnected(null!));

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Http11StateMachine_should_fail_buffered_on_max_reconnect_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 1 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.IsReconnecting);
        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnUpstreamFinished_should_fail_orphaned_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.OnUpstreamFinished();

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnUpstreamFinished_should_fail_buffered_queue_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        sm.OnUpstreamFinished();

        Assert.False(sm.IsReconnecting);
        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Cleanup_should_clear_all_state()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());
        Assert.True(sm.HasInFlightRequests);

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequests);
        Assert.Equal(0, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void PendingRequestCount_should_reflect_inflight_queue()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 4 } });

        Assert.Equal(0, sm.PendingRequestCount);

        sm.OnRequest(MakeRequest());
        sm.OnRequest(MakeRequest("http://example.com/b"));

        Assert.Equal(2, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void PendingRequestCount_should_reflect_reconnect_buffer()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3, MaxPipelineDepth = 4 } });

        sm.OnRequest(MakeRequest());
        sm.OnRequest(MakeRequest("http://example.com/b"));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(2, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptRequest_should_be_false_when_pipeline_full()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions { Http1 = new Http1Options { MaxPipelineDepth = 1 } });

        sm.OnRequest(MakeRequest());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });

        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }
}