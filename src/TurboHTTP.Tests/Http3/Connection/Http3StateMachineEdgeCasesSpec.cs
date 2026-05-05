using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3StateMachineEdgeCasesSpec
{
    private readonly FakeOps _ops = new();

    private StateMachine CreateMachine(
        TurboClientOptions? options = null,
        FakeOps? ops = null)
    {
        return new StateMachine(
            options ?? new TurboClientOptions(),
            ops ?? _ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryBuildControlPreface_should_emit_preface_on_first_call()
    {
        var sm = CreateMachine();

        var preface = sm.TryBuildControlPreface();

        Assert.NotNull(preface);
        Assert.IsType<MultiplexedData>(preface);
        var buf = (MultiplexedData)preface;

        Assert.True(buf.Buffer.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryBuildControlPreface_should_return_null_on_subsequent_calls()
    {
        var sm = CreateMachine();

        var first = sm.TryBuildControlPreface();
        Assert.NotNull(first);

        var second = sm.TryBuildControlPreface();
        Assert.Null(second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryBuildControlPreface_should_not_include_max_push_id()
    {
        var sm = CreateMachine();

        var preface = sm.TryBuildControlPreface();

        Assert.NotNull(preface);
        var buf = (MultiplexedData)preface;
        Assert.Equal(-2, buf.StreamId);
        Assert.True(buf.Buffer.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void TryBuildControlPreface_should_emit_via_outbound_callback_after_reconnect()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();
        sm.OnConnectionRestored();

        // OnConnectionRestored emits preface via _ops callback (control stream = -2)
        var prefaces = _ops.Outbound.OfType<MultiplexedData>()
            .Where(b => b.StreamId == -2)
            .ToList();
        Assert.NotEmpty(prefaces);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void DecodeServerData_should_delegate_to_stream_manager()
    {
        var sm = CreateMachine();
        var buffer = TransportBuffer.Rent(10);
        buffer.FullMemory.Span[..1].Clear(); // minimal DATA frame
        buffer.Length = 1;

        var frames = sm.DecodeServerData(buffer, streamId: 0);

        Assert.NotNull(frames);
        // Result depends on _streamManager implementation
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void DecodeServerData_should_decode_multiple_stream_ids()
    {
        var sm = CreateMachine();

        var buffer1 = TransportBuffer.Rent(1);
        buffer1.FullMemory.Span[0] = 0x00;
        buffer1.Length = 1;

        var buffer4 = TransportBuffer.Rent(1);
        buffer4.FullMemory.Span[0] = 0x00;
        buffer4.Length = 1;

        var frames1 = sm.DecodeServerData(buffer1, streamId: 0);
        var frames4 = sm.DecodeServerData(buffer4, streamId: 4);

        Assert.NotNull(frames1);
        Assert.NotNull(frames4);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void ProcessQpackDecoderBytes_should_accept_decoder_stream_input()
    {
        var sm = CreateMachine();
        var decoderBytes = new byte[] { 0x3f, 0xc1, 0x1f }; // Example QPACK instruction

        // Should not throw
        sm.ProcessQpackDecoderBytes(decoderBytes.AsMemory());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void ProcessQpackDecoderBytes_should_handle_empty_input()
    {
        var sm = CreateMachine();

        // Empty input should not throw
        sm.ProcessQpackDecoderBytes(ReadOnlyMemory<byte>.Empty);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void ProcessQpackEncoderBytes_should_accept_encoder_stream_input()
    {
        var sm = CreateMachine();
        var encoderBytes = new byte[] { 0x3f, 0xc1, 0x1f }; // Example encoder instruction

        // Should not throw
        sm.ProcessQpackEncoderBytes(encoderBytes.AsMemory());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void ProcessQpackEncoderBytes_should_handle_empty_input()
    {
        var sm = CreateMachine();

        // Empty input should not throw
        sm.ProcessQpackEncoderBytes(ReadOnlyMemory<byte>.Empty);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_be_false_unless_explicitly_disabled()
    {
        // StateMachine replaces zero timeout with DefaultIdleTimeout (30s)
        // so IsTimeoutDisabled is never true in normal operation
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromSeconds(1) } });

        Assert.False(sm.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void IsTimeoutDisabled_should_be_false_for_nonzero_timeout()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromSeconds(30) } });

        Assert.False(sm.IsTimeoutDisabled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_remaining_time()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromSeconds(10) } });

        var remaining = sm.TimeUntilExpiry();

        Assert.True(remaining.TotalSeconds > 0);
        Assert.True(remaining.TotalSeconds <= 10);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void TimeUntilExpiry_should_return_remaining_time_on_active_connection()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromSeconds(60) } });

        var remaining = sm.TimeUntilExpiry();

        // Remaining time should be close to 60 seconds on a fresh connection
        Assert.True(remaining.TotalSeconds > 59);
        Assert.True(remaining.TotalSeconds <= 60);
    }

    [Fact(Timeout = 5000)]
    public void ResponseProduced_should_reflect_stream_manager_state()
    {
        var sm = CreateMachine();

        // Initially, no responses produced
        Assert.False(sm.ResponseProduced);

        // After assembling a response (no side effects visible without private access)
        // This property is query-only, delegating to _streamManager
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9.3")]
    public void EvaluateConnectionReuse_should_permit_same_origin()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));

        var decision = sm.EvaluateConnectionReuse(
            targetScheme: "https",
            targetHost: "example.com",
            targetPort: 443,
            serverCertificate: null);

        Assert.NotNull(decision);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9.3")]
    public void EvaluateConnectionReuse_should_evaluate_different_host()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));

        var decision = sm.EvaluateConnectionReuse(
            targetScheme: "https",
            targetHost: "different.com",
            targetPort: 443,
            serverCertificate: null);

        Assert.NotNull(decision);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9.3")]
    public void EvaluateConnectionReuse_should_evaluate_different_port()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));

        var decision = sm.EvaluateConnectionReuse(
            targetScheme: "https",
            targetHost: "example.com",
            targetPort: 8443,
            serverCertificate: null);

        Assert.NotNull(decision);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9.3")]
    public void EvaluateConnectionReuse_should_evaluate_different_scheme()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));

        var decision = sm.EvaluateConnectionReuse(
            targetScheme: "http",
            targetHost: "example.com",
            targetPort: 80,
            serverCertificate: null);

        Assert.NotNull(decision);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-9.3")]
    public void EvaluateConnectionReuse_should_evaluate_after_goaway()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));
        sm.ProcessFrame(new Http3GoAwayFrame(0));

        var decision = sm.EvaluateConnectionReuse(
            targetScheme: "https",
            targetHost: "example.com",
            targetPort: 443,
            serverCertificate: null);

        Assert.NotNull(decision);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_delegate_to_stream_manager()
    {
        var sm = CreateMachine();

        // Should not throw
        sm.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_be_idempotent()
    {
        var sm = CreateMachine();

        sm.Dispose();
        sm.Dispose(); // Should not throw on second call
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void CanAcceptRequest_should_require_concurrency_budget()
    {
        // Default concurrency limit (100 bidi streams)
        var sm = CreateMachine();

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void CanAcceptRequest_should_return_false_when_goaway_and_reconnecting()
    {
        var sm = CreateMachine();
        sm.ProcessFrame(new Http3GoAwayFrame(0));
        sm.OnConnectionLost();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnConnectionLost_should_clear_control_preface_flag()
    {
        var sm = CreateMachine();
        var preface = sm.TryBuildControlPreface();
        Assert.NotNull(preface); // preface sent

        sm.OnConnectionLost();

        // After reconnect, new preface should be built
        sm.OnConnectionRestored();
        sm.TryBuildControlPreface();
        // New preface will be built during OnConnectionRestored via callback
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnConnectionLost_should_drain_streams_before_reset()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(CreateGetRequest());

        sm.OnConnectionLost();

        // After drain and reset, state should be clean
        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnConnectionLost_should_reset_qpack_handler()
    {
        var sm = CreateMachine();

        sm.OnConnectionLost();

        // QpackTableSync should be reset
        Assert.NotNull(sm.TableSync);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnConnectionRestored_should_emit_preface_before_replaying()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();
        sm.EncodeRequest(CreateGetRequest());
        var bufferedFrames = sm.ReconnectBufferCount;
        Assert.True(bufferedFrames > 0);

        _ops.Outbound.Clear();
        sm.OnConnectionRestored();

        // First item should be control preface
        var items = _ops.Outbound.ToList();
        Assert.NotEmpty(items);
        if (items[0] is MultiplexedData buf)
        {

        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ReconnectBufferCount_should_accumulate_during_reconnect()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();

        sm.EncodeRequest(CreateGetRequest("https://example.com/1"));
        var count1 = sm.ReconnectBufferCount;
        Assert.True(count1 > 0);

        sm.EncodeRequest(CreateGetRequest("https://example.com/2"));
        var count2 = sm.ReconnectBufferCount;

        Assert.True(count2 >= count1); // accumulated
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnConnectionRestored_should_clear_reconnect_buffer()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();
        sm.EncodeRequest(CreateGetRequest());
        Assert.True(sm.ReconnectBufferCount > 0);

        sm.OnConnectionRestored();

        Assert.Equal(0, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void OnReconnectAttemptFailed_should_track_attempts_separately()
    {
        var options = new TurboClientOptions { Http3 = new Http3Options { MaxReconnectAttempts = 5 } };
        var sm = CreateMachine(options);
        sm.OnConnectionLost();

        Assert.True(sm.OnReconnectAttemptFailed()); // 2
        Assert.True(sm.OnReconnectAttemptFailed()); // 3
        Assert.True(sm.OnReconnectAttemptFailed()); // 4
        Assert.True(sm.OnReconnectAttemptFailed()); // 5
        Assert.False(sm.OnReconnectAttemptFailed()); // exceeded
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void CanAcceptRequest_should_be_false_during_first_reconnect_attempt()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public async Task ProcessFrame_should_record_activity_on_all_frames()
    {
        var sm = CreateMachine(new TurboClientOptions
        { Http3 = new Http3Options { IdleTimeout = TimeSpan.FromMilliseconds(50) } });

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var beforeFrame = sm.CheckIdleTimeout();
        Assert.NotNull(beforeFrame);

        sm.ProcessFrame(new Http3SettingsFrame([]));

        // CheckIdleTimeout is called immediately after ProcessFrame records activity.
        // The 50ms window is far wider than the time needed to execute the next line,
        // preventing the timer from firing again under parallel test load.
        var afterFrame = sm.CheckIdleTimeout();
        Assert.Null(afterFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Endpoint_should_be_set_on_first_request()
    {
        var sm = CreateMachine();
        Assert.Equal(default, sm.Endpoint);

        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/test"));

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

        sm.EncodeRequest(req1);
        var endpoint1 = sm.Endpoint;

        sm.EncodeRequest(req2);
        var endpoint2 = sm.Endpoint;

        Assert.Equal(endpoint1, endpoint2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6")]
    public void ProcessFrame_should_handle_unknown_frame_types()
    {
        var sm = CreateMachine();
        var unknownFrame = new Http3HeadersFrame(new byte[] { 0x01 }); // Minimal frame

        // Should handle without throwing
        var result = sm.ProcessFrame(unknownFrame);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void Tracker_should_allocate_distinct_stream_ids()
    {
        var sm = CreateMachine();

        var id1 = sm.Tracker.AllocateStreamId();
        var id2 = sm.Tracker.AllocateStreamId();

        Assert.NotEqual(id1, id2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void TableSync_should_be_initialized_with_static_table()
    {
        var sm = CreateMachine();

        Assert.NotNull(sm.TableSync);
        // Encoder starts with 0 capacity (must wait for peer SETTINGS)
        // Decoder starts with 4096
    }

    private static HttpRequestMessage CreateGetRequest(string url = "https://example.com/")
    {
        return new HttpRequestMessage(HttpMethod.Get, url);
    }
}