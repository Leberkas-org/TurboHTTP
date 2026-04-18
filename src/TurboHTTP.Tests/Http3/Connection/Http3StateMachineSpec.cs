using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3StateMachineSpec
{
    private readonly FakeOps _ops = new();

    private StateMachine CreateMachine(
        Http3EngineOptions? options = null,
        FakeOps? ops = null)
    {
        return new StateMachine(
            options ?? new Http3Options().ToEngineOptions(),
            ops ?? _ops);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_absorb_settings_frame()
    {
        var sm = CreateMachine();
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);

        var result = sm.ProcessFrame(settings);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_store_remote_settings()
    {
        var sm = CreateMachine();
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);

        sm.ProcessFrame(settings);

        Assert.True(sm.Connection.RemoteSettingsReceived);
        Assert.Equal(8192L, sm.Connection.RemoteMaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_warn_on_duplicate_settings()
    {
        var sm = CreateMachine();
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);

        sm.ProcessFrame(settings);
        sm.ProcessFrame(settings);

        Assert.Contains(_ops.Warnings, w => w.Contains("SETTINGS error absorbed"));
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_set_goaway_received_on_valid_goaway()
    {
        var sm = CreateMachine();
        var goaway = new Http3GoAwayFrame(8);

        sm.ProcessFrame(goaway);

        Assert.True(sm.Connection.GoAwayReceived);
        Assert.Equal(8L, sm.Connection.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_absorb_goaway_frame()
    {
        var sm = CreateMachine();
        var goaway = new Http3GoAwayFrame(4);

        var result = sm.ProcessFrame(goaway);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_accept_decreasing_goaway_stream_ids()
    {
        var sm = CreateMachine();

        sm.ProcessFrame(new Http3GoAwayFrame(12));
        sm.ProcessFrame(new Http3GoAwayFrame(8));
        sm.ProcessFrame(new Http3GoAwayFrame(4));

        Assert.Equal(4L, sm.Connection.LastGoAwayStreamId);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_warn_on_invalid_goaway_stream_id()
    {
        var sm = CreateMachine();

        // Stream ID 5 is not divisible by 4 — invalid for client-initiated bidi stream
        sm.ProcessFrame(new Http3GoAwayFrame(4));
        _ops.Warnings.Clear();

        sm.ProcessFrame(new Http3GoAwayFrame(8)); // increasing — invalid

        Assert.Contains(_ops.Warnings, w => w.Contains("GOAWAY error absorbed"));
        Assert.True(sm.Connection.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_warn_on_non_divisible_by_four_goaway()
    {
        var sm = CreateMachine();

        // Stream ID 5 is not a valid client-initiated bidi stream ID
        sm.ProcessFrame(new Http3GoAwayFrame(5));

        Assert.Contains(_ops.Warnings, w => w.Contains("GOAWAY error absorbed"));
        Assert.True(sm.Connection.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_reject_push_promise_when_push_disabled()
    {
        var sm = CreateMachine(new Http3Options { AllowServerPush = false }.ToEngineOptions());
        var push = new Http3PushPromiseFrame(1, new byte[] { 0x01 });

        var result = sm.ProcessFrame(push);

        Assert.Null(result);
        Assert.Single(_ops.Outbound); // serialized CANCEL_PUSH frame
        Assert.IsType<Http3NetworkBuffer>(_ops.Outbound[0]);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_warn_when_push_rejected()
    {
        var sm = CreateMachine(new Http3Options { AllowServerPush = false }.ToEngineOptions());

        sm.ProcessFrame(new Http3PushPromiseFrame(42, new byte[] { 0x01 }));

        Assert.Contains(_ops.Warnings, w => w.Contains("push promise rejected") && w.Contains("42"));
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_forward_push_promise_to_app_when_push_enabled()
    {
        var sm = CreateMachine(new Http3Options { AllowServerPush = true }.ToEngineOptions());
        var push = new Http3PushPromiseFrame(1, new byte[] { 0x01 });

        var result = sm.ProcessFrame(push);

        Assert.Same(push, result); // forwarded to app layer
        Assert.Empty(_ops.Outbound); // no CANCEL_PUSH sent
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_enforce_push_limit_when_push_enabled()
    {
        var sm = CreateMachine(new Http3Options { AllowServerPush = true }.ToEngineOptions());

        // The default maxPushCount is 100 when AllowServerPush = true.
        // Push 100 times to hit the limit, then one more should warn.
        for (var i = 0; i < 100; i++)
        {
            var forwarded = sm.ProcessFrame(new Http3PushPromiseFrame(i, new byte[] { 0x01 }));
            Assert.NotNull(forwarded); // each push is forwarded to app
        }

        _ops.Warnings.Clear();
        var exceeded = sm.ProcessFrame(new Http3PushPromiseFrame(100, new byte[] { 0x01 }));

        Assert.Null(exceeded); // push limit exceeded — not forwarded
        Assert.Contains(_ops.Warnings, w => w.Contains("Push limit exceeded"));
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_absorb_cancel_push_frame()
    {
        var sm = CreateMachine();

        var result = sm.ProcessFrame(new Http3CancelPushFrame(1));

        Assert.Null(result);
        Assert.True(sm.Connection.IsPushCancelled(1));
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_absorb_max_push_id_frame()
    {
        var sm = CreateMachine();

        var result = sm.ProcessFrame(new Http3MaxPushIdFrame(10));

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_forward_headers_frame_to_app()
    {
        var sm = CreateMachine();
        var headers = new Http3HeadersFrame(new byte[] { 0x01, 0x02 });

        var result = sm.ProcessFrame(headers);

        Assert.Same(headers, result);
    }

    [Fact(Timeout = 5000)]
    public void ProcessFrame_should_forward_data_frame_to_app()
    {
        var sm = CreateMachine();
        var data = new Http3DataFrame("He"u8.ToArray());

        var result = sm.ProcessFrame(data);

        Assert.Same(data, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void ProcessFrame_should_not_decrement_active_streams_on_response_headers()
    {
        var sm = CreateMachine();

        // Open a stream via EncodeRequest
        sm.EncodeRequest(CreateGetRequest());
        Assert.Equal(1, sm.Connection.ActiveStreamCount);

        // Receive response HEADERS — stream is still open until response is fully emitted
        sm.ProcessFrame(new Http3HeadersFrame(new byte[] { 0x02 }));

        Assert.Equal(1, sm.Connection.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_track_stream_open()
    {
        var sm = CreateMachine();

        sm.EncodeRequest(CreateGetRequest());

        Assert.Equal(1, sm.Connection.ActiveStreamCount);
        Assert.Equal(1, sm.Tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_emit_serialized_frames_via_outbound_callback()
    {
        var sm = CreateMachine();

        sm.EncodeRequest(CreateGetRequest());

        Assert.NotEmpty(_ops.Outbound); // at least HEADERS frame serialized
    }

    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_reject_after_goaway()
    {
        var sm = CreateMachine();
        sm.ProcessFrame(new Http3GoAwayFrame(0));

        var result = sm.EncodeRequest(CreateGetRequest());

        Assert.False(result);
        Assert.Contains(_ops.Warnings, w => w.Contains("GOAWAY received"));
    }

    [Fact(Timeout = 5000)]
    public void CanAcceptRequest_should_be_true_initially()
    {
        var sm = CreateMachine();

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void CanAcceptRequest_should_be_false_after_goaway()
    {
        var sm = CreateMachine();
        sm.ProcessFrame(new Http3GoAwayFrame(0));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void CanAcceptRequest_should_be_false_during_reconnect()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void CheckIdleTimeout_should_return_null_when_not_expired()
    {
        var sm = CreateMachine();

        var result = sm.CheckIdleTimeout();

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void CheckIdleTimeout_should_return_null_when_timeout_disabled()
    {
        var sm = CreateMachine(new Http3Options { IdleTimeout = TimeSpan.Zero }.ToEngineOptions());

        var result = sm.CheckIdleTimeout();

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void CheckIdleTimeout_should_return_goaway_when_expired_no_active_streams()
    {
        // Use a very short timeout so it expires immediately.
        var sm = CreateMachine(new Http3Options { IdleTimeout = TimeSpan.FromMilliseconds(1) }.ToEngineOptions());

        // Wait for timeout to expire.
        Thread.Sleep(10);

        var result = sm.CheckIdleTimeout();

        Assert.NotNull(result);
        Assert.Equal(0L, result.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void CheckIdleTimeout_should_not_expire_when_streams_active()
    {
        var sm = CreateMachine(new Http3Options { IdleTimeout = TimeSpan.FromMilliseconds(1) }.ToEngineOptions());
        sm.EncodeRequest(CreateGetRequest());

        Thread.Sleep(10);

        var result = sm.CheckIdleTimeout();

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionLost_should_enter_reconnect_state()
    {
        var sm = CreateMachine();

        sm.OnConnectionLost();

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionLost_should_reset_tracker()
    {
        var sm = CreateMachine();
        sm.EncodeRequest(CreateGetRequest());
        Assert.Equal(1, sm.Tracker.ActiveStreamCount);

        sm.OnConnectionLost();

        Assert.Equal(0, sm.Tracker.ActiveStreamCount);
        Assert.Equal(0L, sm.Tracker.NextStreamId);
    }

    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_buffer_frames_during_reconnect()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();
        _ops.Outbound.Clear();

        sm.EncodeRequest(CreateGetRequest());

        Assert.True(sm.ReconnectBufferCount > 0);
        Assert.Empty(_ops.Outbound); // not emitted during reconnect
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionRestored_should_replay_buffered_frames()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();
        _ops.Outbound.Clear();

        sm.EncodeRequest(CreateGetRequest());
        var bufferedCount = sm.ReconnectBufferCount;
        Assert.True(bufferedCount > 0);

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
        Assert.Equal(0, sm.ReconnectBufferCount);
        Assert.NotEmpty(_ops.Outbound); // replayed frames + preface
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionRestored_should_clear_reconnect_state()
    {
        var sm = CreateMachine();
        sm.OnConnectionLost();

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void OnReconnectAttemptFailed_should_signal_after_max_attempts()
    {
        var options = new Http3Options { MaxReconnectAttempts = 2 }.ToEngineOptions();
        var sm = CreateMachine(options);
        sm.OnConnectionLost(); // attempt 1

        var canRetry1 = sm.OnReconnectAttemptFailed(); // attempt 2
        Assert.True(canRetry1);

        var canRetry2 = sm.OnReconnectAttemptFailed(); // max reached
        Assert.False(canRetry2);
        Assert.True(_ops.ReconnectFailed);
    }

    [Fact(Timeout = 5000)]
    public void OnReconnectAttemptFailed_should_allow_retry_before_max()
    {
        var options = new Http3Options { MaxReconnectAttempts = 3 }.ToEngineOptions();
        var sm = CreateMachine(options);
        sm.OnConnectionLost(); // attempt 1

        Assert.True(sm.OnReconnectAttemptFailed()); // attempt 2
        Assert.True(sm.OnReconnectAttemptFailed()); // attempt 3 = max
        Assert.False(sm.OnReconnectAttemptFailed()); // exceeded

        Assert.True(_ops.ReconnectFailed);
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionLost_should_reset_connection_state()
    {
        var sm = CreateMachine();

        // Set some connection state first
        sm.ProcessFrame(new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]));
        Assert.True(sm.Connection.RemoteSettingsReceived);

        sm.OnConnectionLost();

        Assert.False(sm.Connection.RemoteSettingsReceived);
        Assert.False(sm.Connection.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleResponse_should_isolate_per_stream_state()
    {
        var sm = CreateMachine();

        // Build minimal QPACK-encoded HEADERS for two different status codes
        var qpack = new Protocol.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);
        var headers200 = new Http3HeadersFrame(qpack.Encode([(":status", "200")]));
        var headers404 = new Http3HeadersFrame(qpack.Encode([(":status", "404")]));

        // Assemble on two different streams
        sm.AssembleResponse(headers200, streamId: 0);
        sm.AssembleResponse(headers404, streamId: 4);

        // Data on stream 0
        sm.AssembleResponse(new Http3DataFrame("AB"u8.ToArray()), streamId: 0);

        // Flush stream 4 first (out-of-order)
        sm.EncodeRequest(CreateGetRequest("https://example.com/a"));
        sm.EncodeRequest(CreateGetRequest("https://example.com/b"));

        sm.FlushPendingResponse(4);
        Assert.Single(_ops.Responses);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, _ops.Responses[0].StatusCode);

        // Flush stream 0
        sm.FlushPendingResponse(0);
        Assert.Equal(2, _ops.Responses.Count);
        Assert.Equal(System.Net.HttpStatusCode.OK, _ops.Responses[1].StatusCode);
        Assert.NotNull(_ops.Responses[1].Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleResponse_should_correlate_by_stream_id()
    {
        var sm = CreateMachine();
        var qpack = new Protocol.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);

        // Send two requests — stream IDs allocated as 0 and 4
        var req1 = CreateGetRequest("https://example.com/first");
        var req2 = CreateGetRequest("https://example.com/second");
        sm.EncodeRequest(req1);
        sm.EncodeRequest(req2);

        // Respond to stream 4 first (out-of-order)
        var headers = new Http3HeadersFrame(qpack.Encode([(":status", "200")]));
        sm.AssembleResponse(headers, streamId: 4);
        sm.FlushPendingResponse(4);

        Assert.Single(_ops.Responses);
        Assert.Same(req2, _ops.Responses[0].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void HasInFlightRequests_should_track_correlation_map_and_streams()
    {
        var sm = CreateMachine();
        Assert.False(sm.HasInFlightRequests);

        sm.EncodeRequest(CreateGetRequest());
        Assert.True(sm.HasInFlightRequests);

        // Assemble response on stream 0 and flush
        var qpack = new Protocol.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);
        sm.AssembleResponse(new Http3HeadersFrame(qpack.Encode([(":status", "200")])), streamId: 0);
        sm.FlushPendingResponse(0);

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void FlushPendingResponse_should_emit_all_streams_on_parameterless_call()
    {
        var sm = CreateMachine();
        var qpack = new Protocol.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);

        sm.EncodeRequest(CreateGetRequest("https://example.com/a"));
        sm.EncodeRequest(CreateGetRequest("https://example.com/b"));

        sm.AssembleResponse(new Http3HeadersFrame(qpack.Encode([(":status", "200")])), streamId: 0);
        sm.AssembleResponse(new Http3HeadersFrame(qpack.Encode([(":status", "201")])), streamId: 4);

        sm.FlushPendingResponse(); // flush all

        Assert.Equal(2, _ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnConnectionLost_should_clean_up_per_stream_state()
    {
        var sm = CreateMachine();
        var qpack = new Protocol.Http3.Qpack.QpackEncoder(maxTableCapacity: 0);

        sm.EncodeRequest(CreateGetRequest());
        sm.AssembleResponse(new Http3HeadersFrame(qpack.Encode([(":status", "200")])), streamId: 0);

        sm.OnConnectionLost();

        // Correlation map preserved for replay, but stream assembly state cleared
        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeRequest_should_tag_outbound_frames_with_stream_id()
    {
        var sm = CreateMachine();

        sm.EncodeRequest(CreateGetRequest());

        // All request frames should be tagged as Http3NetworkBuffer with stream ID 0
        var tagged = _ops.Outbound
            .OfType<Http3NetworkBuffer>()
            .Where(t => t.StreamType == Http3StreamType.Request)
            .ToList();
        Assert.NotEmpty(tagged);
        Assert.All(tagged, t => Assert.Equal(0L, t.StreamId));

        // End-of-request marker should carry the same stream ID
        var endItem = _ops.Outbound.OfType<Http3EndOfRequestItem>().Single();
        Assert.Equal(0L, endItem.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeRequest_should_assign_distinct_stream_ids_to_concurrent_requests()
    {
        var sm = CreateMachine();

        sm.EncodeRequest(CreateGetRequest("https://example.com/a"));
        sm.EncodeRequest(CreateGetRequest("https://example.com/b"));

        var endItems = _ops.Outbound.OfType<Http3EndOfRequestItem>().ToList();
        Assert.Equal(2, endItems.Count);
        Assert.NotEqual(endItems[0].StreamId, endItems[1].StreamId);
    }

    private static HttpRequestMessage CreateGetRequest(string url = "https://example.com/")
    {
        return new HttpRequestMessage(HttpMethod.Get, url);
    }
}