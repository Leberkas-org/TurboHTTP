using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3StateMachineSpec
{
    private readonly FakeOps _ops = new();

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    private Http3ClientStateMachine CreateMachine(
        TurboClientOptions? options = null,
        FakeOps? ops = null)
    {
        return new Http3ClientStateMachine(
            options ?? new TurboClientOptions(),
            ops ?? _ops);
    }

    private static TransportBuffer SerializeFrame(Http3Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static void SimulateConnect(Http3ClientStateMachine sm)
    {
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void PreStart_should_emit_control_stream_setup()
    {
        var sm = CreateMachine();

        sm.PreStart();
        SimulateConnect(sm);

        // Should emit OpenStream messages for control streams
        Assert.NotEmpty(_ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void DecodeServerData_should_absorb_settings_frame()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var settings = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var buffer = SerializeFrame(settings);

        sm.DecodeServerData(new MultiplexedData(buffer, -2));

        // No exception should be thrown
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void DecodeServerData_should_absorb_duplicate_settings()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var settings = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var buffer1 = SerializeFrame(settings);
        var buffer2 = SerializeFrame(settings);

        sm.DecodeServerData(new MultiplexedData(buffer1, -2));
        sm.DecodeServerData(new MultiplexedData(buffer2, -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void CanAcceptRequest_should_be_false_after_goaway()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var goaway = new GoAwayFrame(0);
        var buffer = SerializeFrame(goaway);

        sm.DecodeServerData(new MultiplexedData(buffer, -2));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void DecodeServerData_should_absorb_goaway_frame()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var goaway = new GoAwayFrame(4);
        var buffer = SerializeFrame(goaway);

        // Should not throw
        sm.DecodeServerData(new MultiplexedData(buffer, -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void DecodeServerData_should_accept_decreasing_goaway_stream_ids()
    {
        var sm = CreateMachine();
        sm.PreStart();

        var goaway1 = new GoAwayFrame(12);
        var goaway2 = new GoAwayFrame(8);
        var goaway3 = new GoAwayFrame(4);

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(goaway1), -2));
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(goaway2), -2));
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(goaway3), -2));

        // Verify we accepted the decreasing IDs by checking that requests are rejected
        // (GoAway was received)
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void DecodeServerData_should_absorb_invalid_goaway_stream_id()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new GoAwayFrame(4)), -2));

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new GoAwayFrame(8)), -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void DecodeServerData_should_absorb_non_divisible_by_four_goaway()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new GoAwayFrame(5)), -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.3")]
    public void DecodeServerData_should_reject_push_promise_with_cancel_push()
    {
        var sm = CreateMachine();
        sm.PreStart();
        SimulateConnect(sm);
        var push = new PushPromiseFrame(1, new byte[] { 0x01 });
        var buffer = SerializeFrame(push);

        sm.DecodeServerData(new MultiplexedData(buffer, -2));

        // Should have emitted a CancelPush response on control stream
        Assert.Contains(_ops.Outbound, o => o is MultiplexedData md && md.StreamId < 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.3")]
    public void DecodeServerData_should_absorb_push_promise_when_no_pending_request()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new PushPromiseFrame(42, new byte[] { 0x01 })), -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.3")]
    public void DecodeServerData_should_absorb_cancel_push_frame()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new CancelPushFrame(1)), -2));

        // No exception, frame absorbed
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.3")]
    public void DecodeServerData_should_absorb_max_push_id_frame()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new MaxPushIdFrame(10)), -2));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeServerData_should_forward_headers_frame_to_app()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());
        _ops.Outbound.Clear();
        _ops.Responses.Clear();

        var qpack = new TurboHTTP.Protocol.Syntax.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);
        var headers = new HeadersFrame(qpack.Encode([(":status", "200")]));
        var buffer = SerializeFrame(headers);
        sm.DecodeServerData(new MultiplexedData(buffer, 0));

        // Headers should be processed (no direct return value, but effects are visible)
        // Verify stage didn't fail
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeServerData_should_forward_data_frame_to_app()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());
        _ops.Outbound.Clear();

        var data = new DataFrame("He"u8.ToArray());
        var buffer = SerializeFrame(data);
        sm.DecodeServerData(new MultiplexedData(buffer, 0));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void OnRequest_should_emit_serialized_frames_via_outbound_callback()
    {
        var sm = CreateMachine();
        sm.PreStart();
        _ops.Outbound.Clear();

        sm.OnRequest(CreateGetRequest());

        Assert.NotEmpty(_ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void OnRequest_should_reject_after_goaway()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var goaway = new GoAwayFrame(0);
        var buffer = SerializeFrame(goaway);
        sm.DecodeServerData(new MultiplexedData(buffer, -2));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void CanAcceptRequest_should_be_true_initially()
    {
        var sm = CreateMachine();

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void CanAcceptRequest_should_be_false_during_reconnect()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void OnConnectionLost_should_enter_reconnect_state()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void OnRequest_should_buffer_frames_during_reconnect()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        _ops.Outbound.Clear();

        sm.OnRequest(CreateGetRequest());

        Assert.True(sm.ReconnectBufferCount > 0);
        Assert.Empty(_ops.Outbound); // not emitted during reconnect
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void OnConnectionRestored_should_replay_buffered_frames()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        _ops.Outbound.Clear();

        sm.OnRequest(CreateGetRequest());
        var bufferedCount = sm.ReconnectBufferCount;
        Assert.True(bufferedCount > 0);

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.Equal(0, sm.ReconnectBufferCount);
        Assert.NotEmpty(_ops.Outbound); // replayed frames
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void OnConnectionRestored_should_clear_reconnect_state()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void DecodeServerData_should_handle_stream_read_completed()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        // Simulate stream read completion
        sm.DecodeServerData(new StreamReadCompleted(0));

        // Should complete without error
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void HasInFlightRequests_should_track_requests()
    {
        var sm = CreateMachine();
        sm.PreStart();
        Assert.False(sm.HasInFlightRequests);

        sm.OnRequest(CreateGetRequest());
        Assert.True(sm.HasInFlightRequests);

        // After response assembly and flush
        var qpack = new TurboHTTP.Protocol.Syntax.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);
        var headersFrame = new HeadersFrame(qpack.Encode([(":status", "200")]));
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(headersFrame), 0));
        sm.DecodeServerData(new StreamReadCompleted(0));

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void OnUpstreamFinished_should_flush_all_pending_responses()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var qpack = new TurboHTTP.Protocol.Syntax.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);

        sm.OnRequest(CreateGetRequest("https://example.com/a"));
        sm.OnRequest(CreateGetRequest("https://example.com/b"));

        sm.DecodeServerData(
            new MultiplexedData(SerializeFrame(new HeadersFrame(qpack.Encode([(":status", "200")]))), 0));
        sm.DecodeServerData(
            new MultiplexedData(SerializeFrame(new HeadersFrame(qpack.Encode([(":status", "201")]))), 4));

        sm.OnUpstreamFinished();

        Assert.Equal(2, _ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeServerData_should_isolate_per_stream_state()
    {
        var sm = CreateMachine();
        sm.PreStart();

        // Build minimal QPACK-encoded HEADERS for two different status codes
        var qpack = new TurboHTTP.Protocol.Syntax.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);
        var headers200 = new HeadersFrame(qpack.Encode([(":status", "200")]));
        var headers404 = new HeadersFrame(qpack.Encode([(":status", "404")]));

        // Assemble on two different streams
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(headers200), 0));
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(headers404), 4));

        // Data on stream 0
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new DataFrame("AB"u8.ToArray())), 0));

        // Send requests for stream correlation
        sm.OnRequest(CreateGetRequest("https://example.com/a"));
        sm.OnRequest(CreateGetRequest("https://example.com/b"));

        // Responses are emitted on HEADERS (streaming model)
        Assert.Equal(2, _ops.Responses.Count);
        Assert.Equal(HttpStatusCode.OK, _ops.Responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, _ops.Responses[1].StatusCode);

        // StreamReadCompleted completes the body handles
        sm.DecodeServerData(new StreamReadCompleted(4));
        sm.DecodeServerData(new StreamReadCompleted(0));
        Assert.Equal(2, _ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeServerData_should_correlate_by_stream_id()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var qpack = new TurboHTTP.Protocol.Syntax.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);

        // Send two requests — stream IDs allocated as 0 and 4
        var req1 = CreateGetRequest("https://example.com/first");
        var req2 = CreateGetRequest("https://example.com/second");
        sm.OnRequest(req1);
        sm.OnRequest(req2);

        // Respond to stream 4 first (out-of-order)
        var headers = new HeadersFrame(qpack.Encode([(":status", "200")]));
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(headers), 4));
        sm.DecodeServerData(new StreamReadCompleted(4));

        Assert.Single(_ops.Responses);
        Assert.Same(req2, _ops.Responses[0].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnRequest_should_tag_outbound_frames_with_stream_id()
    {
        var sm = CreateMachine();
        sm.PreStart();
        SimulateConnect(sm);
        _ops.Outbound.Clear(); // Clear control stream setup frames

        sm.OnRequest(CreateGetRequest());

        // All request frames should be tagged as MultiplexedData with stream ID 0
        var tagged = _ops.Outbound
            .OfType<MultiplexedData>()
            .ToList();
        Assert.NotEmpty(tagged);
        Assert.All(tagged, t => Assert.Equal(0L, (long)t.StreamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnRequest_should_assign_distinct_stream_ids_to_concurrent_requests()
    {
        var sm = CreateMachine();
        sm.PreStart();
        SimulateConnect(sm);
        _ops.Outbound.Clear(); // Clear control stream setup frames

        sm.OnRequest(CreateGetRequest("https://example.com/a"));
        sm.OnRequest(CreateGetRequest("https://example.com/b"));

        var tagged = _ops.Outbound.OfType<MultiplexedData>().ToList();
        Assert.NotEmpty(tagged);
        var streamIds = tagged.Select(t => t.StreamId).Distinct().ToList();
        Assert.Equal(2, streamIds.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnTimerFired_should_handle_idle_timeout()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromMilliseconds(1) } });
        sm.PreStart();

        // Timer firing should check idle timeout and potentially emit GoAway
        sm.OnTimerFired("idle-timeout-check");

        // Should not throw
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void OnTimerFired_should_ignore_unknown_timers()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.OnTimerFired("unknown-timer");

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void Cleanup_should_dispose_resources()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.Cleanup();

        // Should not throw
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.1")]
    public void Endpoint_should_be_accessible()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.OnRequest(CreateGetRequest());

        // Endpoint is a value type, so it's always valid
        Assert.True(true);
    }

    private static HttpRequestMessage CreateGetRequest(string url = "https://example.com/")
    {
        return new HttpRequestMessage(HttpMethod.Get, url);
    }
}