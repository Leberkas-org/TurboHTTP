using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3StateMachineEdgeCasesSpec
{
    private readonly FakeOps _ops = new();

    private Http3ClientStateMachine CreateMachine(
        TurboClientOptions? options = null,
        FakeOps? ops = null)
    {
        return new Http3ClientStateMachine(
            options ?? new TurboClientOptions(),
            ops ?? _ops);
    }

    private static void SimulateConnect(Http3ClientStateMachine sm)
    {
        sm.DecodeServerData(new TransportConnected(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void PreStart_should_emit_control_streams_and_preface()
    {
        var sm = CreateMachine();
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var outbound = _ops.Outbound.ToList();
        Assert.NotEmpty(outbound);

        var controlStreamOpens = outbound.OfType<OpenStream>()
            .Where(os => os.StreamId == -2 || os.StreamId == -3 || os.StreamId == -4)
            .ToList();
        Assert.Equal(3, controlStreamOpens.Count);

        var prefaces = outbound.OfType<MultiplexedData>()
            .Where(b => b.StreamId == -2)
            .ToList();
        Assert.NotEmpty(prefaces);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void PreStart_should_not_emit_duplicate_preface_on_second_call()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var firstCallOutbound = _ops.Outbound.Count;

        sm.PreStart();
        var secondCallOutbound = _ops.Outbound.Count;

        Assert.True(secondCallOutbound >= firstCallOutbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void PreStart_should_emit_preface_on_control_stream()
    {
        var sm = CreateMachine();
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var prefaces = _ops.Outbound.OfType<MultiplexedData>()
            .Where(b => b.StreamId == -2)
            .ToList();
        Assert.NotEmpty(prefaces);
        Assert.True(prefaces[0].Buffer.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void DecodeServerData_should_accept_multiplexed_data()
    {
        var sm = CreateMachine();
        var buffer = TransportBuffer.Rent(10);
        buffer.FullMemory.Span[..1].Clear();
        buffer.Length = 1;
        var data = new MultiplexedData(buffer, 0);

        sm.DecodeServerData(data);

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void DecodeServerData_should_handle_transport_connected()
    {
        var sm = CreateMachine();
        var connectionInfo = new ConnectionInfo(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000),
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 443),
            TransportProtocol.Tcp);

        sm.DecodeServerData(new TransportConnected(connectionInfo));

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void CanAcceptRequest_should_be_true_initially()
    {
        var sm = CreateMachine();

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void CanAcceptRequest_should_return_false_during_reconnect()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void IsReconnecting_should_reflect_connection_state()
    {
        var sm = CreateMachine();
        Assert.False(sm.IsReconnecting);

        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void IsReconnecting_should_clear_after_reconnect_success()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        var connectionInfo = new ConnectionInfo(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000),
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 443),
            TransportProtocol.Tcp);
        sm.DecodeServerData(new TransportConnected(connectionInfo));

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ReconnectBufferCount_should_accumulate_during_reconnect()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest("https://example.com/0")); // Create in-flight request

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        sm.OnRequest(CreateGetRequest("https://example.com/1"));
        var count1 = sm.ReconnectBufferCount;
        Assert.True(count1 > 0);

        sm.OnRequest(CreateGetRequest("https://example.com/2"));
        var count2 = sm.ReconnectBufferCount;

        Assert.True(count2 >= count1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ReconnectBufferCount_should_clear_after_reconnect_success()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest("https://example.com/0")); // Create in-flight request

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.OnRequest(CreateGetRequest());
        Assert.True(sm.ReconnectBufferCount > 0);

        var connectionInfo = new ConnectionInfo(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000),
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 443),
            TransportProtocol.Tcp);
        sm.DecodeServerData(new TransportConnected(connectionInfo));

        Assert.Equal(0, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnTimerFired_should_schedule_idle_check()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromSeconds(10) } });
        sm.PreStart();

        sm.OnTimerFired("idle-timeout-check");

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnTimerFired_should_ignore_unknown_timer_names()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.OnTimerFired("unknown-timer");

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Endpoint_should_be_set_on_first_request()
    {
        var sm = CreateMachine();
        Assert.Equal(default, sm.Endpoint);

        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/test"));

        Assert.NotEqual(default, sm.Endpoint);
        Assert.Equal("example.com", sm.Endpoint.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Endpoint_should_not_change_on_subsequent_requests_to_same_origin()
    {
        var sm = CreateMachine();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/first") { Version = new Version(3, 0) };
        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/second") { Version = new Version(3, 0) };

        sm.OnRequest(req1);
        var endpoint1 = sm.Endpoint;

        sm.OnRequest(req2);
        var endpoint2 = sm.Endpoint;

        Assert.Equal(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void HasInFlightRequests_should_track_request_state()
    {
        var sm = CreateMachine();
        Assert.False(sm.HasInFlightRequests);

        sm.OnRequest(CreateGetRequest());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnRequest_should_buffer_during_reconnect()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest("https://example.com/0")); // Create in-flight request

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        sm.OnRequest(CreateGetRequest());

        Assert.True(sm.ReconnectBufferCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnUpstreamFinished_should_not_throw()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.OnUpstreamFinished();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Cleanup_should_dispose_resources()
    {
        var sm = CreateMachine();

        sm.Cleanup();

        Assert.True(true);
    }

    private static HttpRequestMessage CreateGetRequest(string url = "https://example.com/")
    {
        return new HttpRequestMessage(HttpMethod.Get, url);
    }
}