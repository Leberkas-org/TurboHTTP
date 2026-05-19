using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineReconnectSpec
{
    private static TransportBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static TurboClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null)
    {
        var options = new TurboClientOptions();
        if (maxConcurrentStreams.HasValue) options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        if (maxReconnect.HasValue) options.Http2.MaxReconnectAttempts = maxReconnect.Value;
        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/") =>
        new(HttpMethod.Post, $"https://example.com{path}");

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedGet(string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}");
        req.Options.Set(OptionsKey.Key, pending);
        req.Options.Set(OptionsKey.VersionKey, version);
        return (req, pending);
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_start_reconnect_on_disconnect_with_inflight()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(2, sm.ReconnectBufferCount);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_not_replay_non_idempotent_requests()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a")); // stream 1
        sm.OnRequest(MakePost("/b")); // stream 3
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(3, Http2ErrorCode.NoError);
        sm.DecodeServerData(new TransportData(SerializeFrame(goaway)));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(1, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_replay_requests_on_connection_restored()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.NotEmpty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_set_CanAcceptRequest_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_fail_when_max_reconnect_exceeded()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(maxReconnect: 1), ops);
        sm.PreStart();
        var (req, pending) = MakeTrackedGet();
        sm.OnRequest(req);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted, "Request should be faulted after max reconnect attempts");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_emit_new_connect_when_reconnect_under_limit()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(maxReconnect: 3), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }
}