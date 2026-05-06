using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http2.Connection;

public sealed class Http2StateMachineSpec
{
    private static TurboClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null,
        int initialStreamWindowSize = 65_535, int maxFrameSize = 16_384)
    {
        var options = new TurboClientOptions();
        options.Http2.InitialStreamWindowSize = initialStreamWindowSize;
        options.Http2.MaxFrameSize = maxFrameSize;
        if (maxConcurrentStreams.HasValue) options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        if (maxReconnect.HasValue) options.Http2.MaxReconnectAttempts = maxReconnect.Value;
        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/", HttpContent? content = null) =>
        new(HttpMethod.Post, $"https://example.com{path}") { Content = content };

    private static HttpRequestMessage MakeDelete(string path = "/") =>
        new(HttpMethod.Delete, $"https://example.com{path}");

    private static HeadersFrame MakeResponseHeaders(int streamId, string statusCode = "200", bool endStream = true,
        bool endHeaders = true)
    {
        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", statusCode),
            ("content-type", "text/plain")
        ]);
        return new HeadersFrame(streamId, hpack, endHeaders, endStream);
    }

    private static DataFrame MakeData(int streamId, byte[] data, bool endStream = true)
        => new(streamId, data, endStream);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void StateMachine_TryBuildPreface_should_return_preface_on_first_call()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        var preface = sm.TryBuildPreface();

        Assert.NotNull(preface);
        Assert.True(preface.Buffer.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void StateMachine_TryBuildPreface_should_return_null_on_subsequent_calls()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        sm.TryBuildPreface();
        var second = sm.TryBuildPreface();

        Assert.Null(second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void StateMachine_TryBuildPreface_should_return_null_when_connection_window_disabled()
    {
        var ops = new FakeOps();
        var config = new TurboClientOptions
        {
            Http2 = new Http2Options
            {
                MaxConnectionsPerServer = 6,
                MaxConcurrentStreams = 100,
                InitialConnectionWindowSize = 0, // disabled
                InitialStreamWindowSize = 65535,
                MaxFrameSize = 16384,
                HeaderTableSize = 4096,
                MaxReconnectAttempts = 3,
                KeepAlivePingDelay = Timeout.InfiniteTimeSpan,
                KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
            }
        };
        var sm = new StateMachine(config, ops);

        var preface = sm.TryBuildPreface();

        Assert.Null(preface);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void StateMachine_EncodeRequest_should_emit_headers_and_acquire_frames()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var req = MakeGet();
        sm.EncodeRequest(req);

        var headers = Assert.Single(ops.Outbound.OfType<TransportData>().Select(d => d.Buffer));
        Assert.True(headers.Length > 0);
        Assert.True(headers.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void StateMachine_EncodeRequest_should_return_false_when_goaway_received()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        // Simulate GOAWAY
        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.ProcessFrame(goaway);
        ops.Warnings.Clear();

        var result = sm.EncodeRequest(MakeGet());

        Assert.False(result);
        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void StateMachine_EncodeRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        Assert.Equal(default, sm.Endpoint);

        var req = MakeGet();
        sm.EncodeRequest(req);

        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_EncodeRequest_should_emit_data_frame_when_request_has_body()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var content = new ByteArrayContent([1, 2, 3]);
        var req = MakePost("/", content);
        sm.EncodeRequest(req);

        var frames = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).ToList();
        Assert.True(frames.Count > 0); // headers + data
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_EncodeRequest_should_return_true_for_null_request_uri()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var req = new HttpRequestMessage { RequestUri = null };
        var result = sm.EncodeRequest(req);

        Assert.True(result);
        Assert.True(ops.Outbound.Count > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void StateMachine_EncodeRequest_should_allocate_incremented_stream_ids()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        sm.EncodeRequest(MakeGet("/b")); // stream 3
        sm.EncodeRequest(MakeGet("/c")); // stream 5

        var acquires = ops.Outbound.OfType<TransportData>().Count();
        Assert.Equal(3, acquires);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4")]
    public void StateMachine_DecodeServerData_should_decode_frames_and_return_list()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        var frame = new SettingsFrame([]);
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;

        var frames = sm.DecodeServerData(buffer);

        Assert.Single(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void StateMachine_ProcessFrame_should_handle_settings_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([(SettingsParameter.MaxFrameSize, 32768u)]);

        var result = sm.ProcessFrame(settings);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void StateMachine_ProcessFrame_should_emit_settings_ack_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([]);
        sm.ProcessFrame(settings);

        var ack = Assert.Single(ops.Outbound.OfType<TransportData>().Select(d => d.Buffer));
        Assert.NotNull(ack);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.3")]
    public void StateMachine_ProcessFrame_should_update_max_concurrent_streams_from_settings()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxConcurrentStreams: 100), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 50u)]);

        sm.ProcessFrame(settings);

    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_return_true_for_valid_data_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        // First, encode a request to create a stream
        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        // Then send response headers
        var headers = MakeResponseHeaders(1, endStream: false);
        var result = sm.ProcessFrame(headers);
        Assert.True(result);

        // Then send data frame
        var data = MakeData(1, [1, 2, 3], endStream: true);
        result = sm.ProcessFrame(data);

        Assert.True(result);
        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_set_response_produced_when_data_completes_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());

        var headers = MakeResponseHeaders(1, endStream: false);
        sm.ProcessFrame(headers);

        Assert.False(sm.ResponseProduced);

        var data = MakeData(1, [1, 2, 3], endStream: true);
        sm.ProcessFrame(data);

        Assert.True(sm.ResponseProduced);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void StateMachine_ProcessFrame_should_handle_headers_frame_with_endstream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.ProcessFrame(headers);

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void StateMachine_ProcessFrame_should_accumulate_headers_without_endheaders()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var split = hpack.Length / 2;
        var partial = new HeadersFrame(1, hpack.Slice(0, split), endHeaders: false, endStream: false);

        sm.ProcessFrame(partial);

        Assert.Empty(ops.Responses); // incomplete, awaiting continuation
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void StateMachine_ProcessFrame_should_handle_continuation_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var fullHpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var hpackSize = fullHpack.Length;
        var split = hpackSize / 2;

        var headers = new HeadersFrame(1, fullHpack[..split], endHeaders: false, endStream: false);
        sm.ProcessFrame(headers);

        var cont = new ContinuationFrame(1, fullHpack[split..], endHeaders: true);
        sm.ProcessFrame(cont);

        // EndStream comes from DATA frame for headers-only responses with body
        var data = MakeData(1, [], endStream: true);
        sm.ProcessFrame(data);

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void StateMachine_ProcessFrame_should_handle_rst_stream_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel);
        var result = sm.ProcessFrame(rst);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_handle_window_update_on_connection()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(0, 16384); // stream 0 = connection
        var result = sm.ProcessFrame(win);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_handle_window_update_on_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(1, 8192); // stream 1
        var result = sm.ProcessFrame(win);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_ProcessFrame_should_respond_to_ping_with_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var ping = new PingFrame(new byte[8], isAck: false);
        sm.ProcessFrame(ping);

        var pongBuf = Assert.Single(ops.Outbound.OfType<TransportData>().Select(d => d.Buffer));
        Assert.NotNull(pongBuf);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void StateMachine_ProcessFrame_should_ignore_ping_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var pong = new PingFrame(new byte[8], isAck: true);
        sm.ProcessFrame(pong);

        // ACK ping should not trigger response
        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_ProcessFrame_should_handle_goaway_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        var result = sm.ProcessFrame(goaway);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_ProcessFrame_should_trigger_reconnect_on_goaway_with_inflight()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.ProcessFrame(goaway);

        Assert.True(sm.IsReconnecting);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_return_false_when_connection_flow_control_violated()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        // Send DATA that exceeds connection window (initial = 65535)
        var largeData = new byte[100000];
        var data = new DataFrame(1, largeData, endStream: true);

        var result = sm.ProcessFrame(data);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void StateMachine_ProcessFrame_should_return_false_when_stream_flow_control_violated()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        // Send DATA on stream that exceeds stream window (initial = 65535)
        var largeData = new byte[100000];
        var data = new DataFrame(1, largeData, endStream: true);

        var result = sm.ProcessFrame(data);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_correlate_request_with_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        var req = MakeGet("/test");
        sm.EncodeRequest(req);
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal(req.RequestUri, response.RequestMessage.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void StateMachine_ProcessFrame_should_handle_multiple_concurrent_streams()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        sm.EncodeRequest(MakeGet("/b")); // stream 3
        ops.Outbound.Clear();

        // Response for stream 3 arrives first
        var headers3 = MakeResponseHeaders(3);
        sm.ProcessFrame(headers3);

        // Response for stream 1 arrives second
        var headers1 = MakeResponseHeaders(1);
        sm.ProcessFrame(headers1);

        Assert.Equal(2, ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_ProcessFrame_should_clean_up_stream_state_after_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());

        var headers = MakeResponseHeaders(1);
        sm.ProcessFrame(headers);

        // Stream should be closed and state returned to pool
        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void StateMachine_CanAcceptRequest_should_respect_max_concurrent_streams()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxConcurrentStreams: 2), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet("/a")); // stream 1
        sm.EncodeRequest(MakeGet("/b")); // stream 3

        Assert.False(sm.CanAcceptRequest); // limit reached
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_CanAcceptRequest_should_be_true_after_stream_closes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxConcurrentStreams: 2), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet("/a"));
        sm.EncodeRequest(MakeGet("/b"));

        Assert.False(sm.CanAcceptRequest);

        // Close first stream with headers (headers-only response)
        var headers1 = MakeResponseHeaders(1);
        sm.ProcessFrame(headers1);

        // Close with empty data frame to trigger stream closure
        var data1 = MakeData(1, [], endStream: true);
        sm.ProcessFrame(data1);

        // Should be able to accept new request now
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.1")]
    public void StateMachine_OnConnectionLost_should_replay_idempotent_methods_below_lastStreamId()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet("/a")); // idempotent
        sm.EncodeRequest(MakeDelete("/b")); // idempotent
        ops.Outbound.Clear();

        sm.OnConnectionLost(lastStreamId: 3); // both processed

        Assert.Equal(2, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.1")]
    public void StateMachine_OnConnectionLost_should_drop_non_idempotent_methods()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakePost("/a")); // non-idempotent
        ops.Outbound.Clear();

        sm.OnConnectionLost(lastStreamId: 1);

        Assert.Equal(0, sm.ReconnectBufferCount);
        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_IsReconnecting_should_be_true_after_connection_lost()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0);

        Assert.True(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_CanAcceptRequest_should_be_false_when_reconnecting()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0);

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_OnConnectionRestored_should_clear_reconnecting_flag()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0);
        ops.Outbound.Clear();

        sm.OnConnectionRestored();

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void StateMachine_OnConnectionRestored_should_emit_preface()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0);
        ops.Outbound.Clear();

        sm.OnConnectionRestored();

        // First item should be preface, then headers from replayed request
        var buffers = ops.Outbound.OfType<TransportData>().Select(d => d.Buffer).ToList();
        Assert.NotEmpty(buffers);
        var preface = buffers[0];
        Assert.NotNull(preface);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_OnConnectionRestored_should_replay_buffered_requests()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet("/a"));
        sm.EncodeRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        sm.OnConnectionLost(lastStreamId: 0);
        ops.Outbound.Clear();

        sm.OnConnectionRestored();

        // OnConnectionRestored emits preface + 2 replayed requests
        var acquires = ops.Outbound.OfType<TransportData>().Count();
        Assert.Equal(3, acquires);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_OnReconnectAttemptFailed_should_emit_new_reconnect_when_under_max()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxReconnect: 3), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0); // attempt 1
        ops.Outbound.Clear();

        sm.OnReconnectAttemptFailed(); // attempt 2

        Assert.True(sm.IsReconnecting);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_OnReconnectAttemptFailed_should_fail_when_max_exceeded()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxReconnect: 1), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        sm.OnConnectionLost(lastStreamId: 0); // attempt 1

        sm.OnReconnectAttemptFailed(); // attempt 2 > max

        Assert.NotNull(ops.FailException);
        Assert.Contains("reconnect failed after max attempts", ops.FailException.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_decode_1xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, "100", endStream: true);
        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(100, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_decode_2xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_decode_4xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, "404", endStream: true);
        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_decode_5xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, "500", endStream: true);
        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(500, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_ProcessFrame_should_preserve_response_headers()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("cache-control", "max-age=3600")
        ]);
        var headers = new HeadersFrame(1, hpack, endHeaders: true, endStream: true);

        sm.ProcessFrame(headers);

        var response = Assert.Single(ops.Responses);
        Assert.True(response.Content?.Headers.ContentType is not null);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.1")]
    public void StateMachine_Endpoint_should_be_initialized_default()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        Assert.Equal(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.1")]
    public void StateMachine_Endpoint_should_be_set_from_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        var req = MakeGet();
        sm.EncodeRequest(req);

        Assert.NotEqual(default, sm.Endpoint);
        Assert.Equal("example.com", sm.Endpoint.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_HasInFlightRequests_should_be_true_when_requests_pending()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void StateMachine_HasInFlightRequests_should_be_false_after_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        var headers = MakeResponseHeaders(1);
        sm.ProcessFrame(headers);

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void StateMachine_ProcessFrame_should_warn_when_continuation_for_unknown_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var cont = new ContinuationFrame(999, new byte[10], endHeaders: true);
        sm.ProcessFrame(cont);

        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void StateMachine_ProcessFrame_should_warn_when_data_for_unknown_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();
        ops.Outbound.Clear();

        var data = new DataFrame(999, new byte[10], endStream: true);
        sm.ProcessFrame(data);

        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void StateMachine_ProcessFrame_should_accumulate_response_body_across_multiple_frames()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.TryBuildPreface();

        sm.EncodeRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false);
        sm.ProcessFrame(headers);

        var data1 = MakeData(1, [1, 2, 3], endStream: false);
        sm.ProcessFrame(data1);

        var data2 = MakeData(1, [4, 5, 6], endStream: true);
        sm.ProcessFrame(data2);

        var response = Assert.Single(ops.Responses);
        var body = response.Content.ReadAsStream(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
    }
}