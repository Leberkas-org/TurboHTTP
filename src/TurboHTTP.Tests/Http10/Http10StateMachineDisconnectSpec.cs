using System.Text;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http10;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10StateMachineDisconnectSpec
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
        var bytes = Encoding.ASCII.GetBytes(responseText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.1")]
    public void HandleDisconnect_should_decode_eof_on_graceful_close()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());
        ops.Outbound.Clear();

        var response = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n\r\nHello";
        sm.DecodeServerData(new TransportData(CreateResponseBuffer(response)));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.1")]
    public void HandleDisconnect_should_fail_inflight_on_error_close_with_no_reconnect()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 0 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.1")]
    public void HandleDisconnect_should_fail_inflight_with_content_length_mismatch_message()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 0 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);

        var partial = "HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nPartial";
        sm.DecodeServerData(new TransportData(CreateResponseBuffer(partial)));

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.1")]
    public void OnUpstreamFinished_should_decode_eof_and_complete_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());

        var response = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n\r\nBody";
        sm.DecodeServerData(new TransportData(CreateResponseBuffer(response)));

        sm.OnUpstreamFinished();

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.1")]
    public void OnUpstreamFinished_should_fail_orphaned_request()
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
    [Trait("RFC", "RFC1945-8")]
    public void OnUpstreamFinished_should_fail_buffered_request_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });
        var (request, pending) = MakeTrackedRequest();

        sm.OnRequest(request);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);

        sm.OnUpstreamFinished();

        Assert.False(sm.IsReconnecting);
        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void OnUpstreamFinished_should_clear_reconnect_state_when_reconnecting_without_buffered()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });

        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);

        sm.OnUpstreamFinished();

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeServerData_should_ignore_unknown_transport_messages()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportConnected(null!));
        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Cleanup_should_reset_state()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        sm.OnRequest(MakeRequest());
        Assert.True(sm.HasInFlightRequest);

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void PendingRequestCount_should_count_inflight_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());

        Assert.Equal(0, sm.PendingRequestCount);

        sm.OnRequest(MakeRequest());

        Assert.Equal(1, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void PendingRequestCount_should_count_buffered_request_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops,
            new TurboClientOptions { Http1 = new Http1Options { MaxReconnectAttempts = 3 } });

        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(1, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeResponse_should_fail_request_on_malformed_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, new TurboClientOptions());
        var (request, _) = MakeTrackedRequest();

        sm.OnRequest(request);

        var garbage = CreateResponseBuffer("NOT-HTTP-AT-ALL\r\n\r\n");
        sm.DecodeServerData(new TransportData(garbage));

        Assert.Empty(ops.Responses);
    }
}