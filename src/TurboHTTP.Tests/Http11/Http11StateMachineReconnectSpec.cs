using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11StateMachineReconnectSpec
{
    private static HttpRequestMessage MakeRequest(string path = "/") =>
        new(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedRequest(string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };
        request.Options.Set(TurboClientCorrelation.Key, pending);
        request.Options.Set(TurboClientCorrelation.VersionKey, version);
        return (request, pending);
    }

    private static TurboClientOptions MakeConfig(int maxPipelineDepth = 4, int maxReconnectAttempts = 3) =>
        new() { Http1 = new Http1Options { MaxPipelineDepth = maxPipelineDepth, MaxReconnectAttempts = maxReconnectAttempts } };

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 80),
        TransportProtocol.Tcp);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_start_reconnect_on_disconnect_with_inflight_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/a"));
        sm.OnRequest(MakeRequest("/b"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequests);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_set_CanAcceptRequest_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_replay_buffered_requests_on_connection_restored()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/a"));
        sm.OnRequest(MakeRequest("/b"));
        ops.Outbound.Clear();
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests);
        Assert.Equal(2, ops.Outbound.OfType<TransportData>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_fail_requests_when_max_reconnect_attempts_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig(maxReconnectAttempts: 1));
        var (request, pending) = MakeTrackedRequest();
        sm.OnRequest(request);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
        Assert.False(sm.IsReconnecting);
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_emit_new_connect_when_reconnect_attempt_under_limit()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(ops, MakeConfig(maxReconnectAttempts: 3));
        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }
}
