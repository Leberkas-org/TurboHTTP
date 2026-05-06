using System.Net;
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
        return new HeadersFrame(streamId, hpack, endStream, endHeaders);
    }

    private static DataFrame MakeData(int streamId, byte[] data, bool endStream = true)
        => new(streamId, data, endStream);

    private static TransportBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static TransportBuffer SerializeFrames(params Http2Frame[] frames)
    {
        var totalSize = frames.Sum(f => f.SerializedSize);
        var buffer = TransportBuffer.Rent(totalSize);
        var span = buffer.FullMemory.Span;
        var offset = 0;
        foreach (var frame in frames)
        {
            var frameSpan = span.Slice(offset);
            frame.WriteTo(ref frameSpan);
            offset += frame.SerializedSize;
        }
        buffer.Length = totalSize;
        return buffer;
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        null, null);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PreStart_should_emit_preface()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        sm.PreStart();

        Assert.NotEmpty(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_emit_headers_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet());

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_warn_when_goaway_received()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.DecodeServerData(new TransportData(SerializeFrame(goaway)));
        ops.Warnings.Clear();

        sm.OnRequest(MakeGet());

        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        Assert.Equal(default, sm.Endpoint);

        sm.OnRequest(MakeGet());

        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void OnRequest_should_emit_data_frame_when_request_has_body()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var content = new ByteArrayContent([1, 2, 3]);
        sm.OnRequest(MakePost("/", content));

        var frames = ops.Outbound.OfType<TransportData>().ToList();
        Assert.True(frames.Count > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void OnRequest_should_allocate_incremented_stream_ids()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        sm.OnRequest(MakeGet("/c"));

        Assert.Equal(3, ops.Outbound.OfType<TransportData>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4")]
    public void DecodeServerData_should_process_settings_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([]);
        sm.DecodeServerData(new TransportData(SerializeFrame(settings)));

        Assert.NotEmpty(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_produce_response_from_headers_and_data()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data = MakeData(1, [1, 2, 3], endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrames(headers, data)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_complete_response_on_headers_with_endstream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_accumulate_headers_without_endheaders()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var split = hpack.Length / 2;
        var partial = new HeadersFrame(1, hpack.Slice(0, split), endHeaders: false, endStream: false);

        sm.DecodeServerData(new TransportData(SerializeFrame(partial)));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_handle_continuation_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var fullHpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var hpackSize = fullHpack.Length;
        var split = hpackSize / 2;

        var headers = new HeadersFrame(1, fullHpack[..split], endHeaders: false, endStream: false);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var cont = new ContinuationFrame(1, fullHpack[split..], endHeaders: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(cont)));

        var data = MakeData(1, [], endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(data)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void DecodeServerData_should_handle_rst_stream_frame()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel);
        sm.DecodeServerData(new TransportData(SerializeFrame(rst)));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_connection()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(0, 16384);
        sm.DecodeServerData(new TransportData(SerializeFrame(win)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(1, 8192);
        sm.DecodeServerData(new TransportData(SerializeFrame(win)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_respond_to_ping_with_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var ping = new PingFrame(new byte[8], isAck: false);
        sm.DecodeServerData(new TransportData(SerializeFrame(ping)));

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_ignore_ping_ack()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var pong = new PingFrame(new byte[8], isAck: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(pong)));

        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_trigger_reconnect_on_goaway_with_inflight()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.DecodeServerData(new TransportData(SerializeFrame(goaway)));

        Assert.True(sm.IsReconnecting);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_warn_when_connection_flow_control_violated()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var largeData = new byte[100000];
        var data = new DataFrame(1, largeData, endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrames(headers, data)));

        Assert.Contains(ops.Warnings, w => w.Contains("flow control window exceeded"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_correlate_request_with_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        var req = MakeGet("/test");
        sm.OnRequest(req);
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal(req.RequestUri, response.RequestMessage.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void DecodeServerData_should_handle_multiple_concurrent_streams()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        var headers3 = MakeResponseHeaders(3);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers3)));

        var headers1 = MakeResponseHeaders(1);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers1)));

        Assert.Equal(2, ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void CanAcceptRequest_should_respect_max_concurrent_streams()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(maxConcurrentStreams: 2), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_1xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "100", endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(100, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_4xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "404", endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_5xx_status_codes()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "500", endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(500, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_warn_when_continuation_for_unknown_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(data)));

        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_warn_when_data_for_unknown_stream()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(data)));

        Assert.Single(ops.Warnings);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_accumulate_response_body_across_multiple_frames()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data1 = MakeData(1, [1, 2, 3], endStream: false);
        var data2 = MakeData(1, [4, 5, 6], endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrames(headers, data1, data2)));

        var response = Assert.Single(ops.Responses);
        var body = response.Content.ReadAsStream(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.1")]
    public void Endpoint_should_be_initialized_default()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);

        Assert.Equal(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_true_when_requests_pending()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_false_after_response()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_preserve_response_headers()
    {
        var ops = new FakeOps();
        var sm = new StateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("cache-control", "max-age=3600")
        ]);
        var headers = new HeadersFrame(1, hpack, endHeaders: true, endStream: true);
        sm.DecodeServerData(new TransportData(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.True(response.Content?.Headers.ContentType is not null);
    }
}
