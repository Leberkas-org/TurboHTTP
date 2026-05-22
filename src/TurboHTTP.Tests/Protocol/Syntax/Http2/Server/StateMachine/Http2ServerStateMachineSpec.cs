using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using AkkaActor = Akka.Actor;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

/// <summary>
/// Unit tests for HTTP/2 Http2ServerStateMachine.
/// Tests frame decoding, request assembly, response encoding, and flow control.
/// </summary>
public sealed class Http2ServerStateMachineSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public AkkaActor.IActorRef StageActor { get; set; } = AkkaActor.ActorRefs.Nobody;

        public void OnRequest(TurboHttpContext context) { }

        public void OnOutbound(ITransportOutbound item)
        {
            EmittedOutbound.Add(item);
        }

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
        }

        public void OnCancelTimer(string name)
        {
        }
    }

    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        // Frame header: length (3 bytes), type (1), flags (1), stream ID (4)
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;

        byte flags = 0;
        if (endStream) flags |= (byte)Headers.EndStream;
        if (endHeaders) flags |= (byte)Headers.EndHeaders;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static byte[] BuildSettingsFrame(bool isAck = false)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize;
        var frame = new byte[frameSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 0;
        frame[3] = (byte)FrameType.Settings;
        frame[4] = isAck ? (byte)Settings.Ack : (byte)0;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 0;

        return frame;
    }

    private static byte[] BuildPingFrame(bool isAck = false)
    {
        const int frameHeaderSize = 9;
        const int pingDataSize = 8;
        var frameSize = frameHeaderSize + pingDataSize;
        var frame = new byte[frameSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = pingDataSize;
        frame[3] = (byte)FrameType.Ping;
        frame[4] = isAck ? (byte)PingFlags.Ack : (byte)0;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 0;

        // Ping data (8 bytes of arbitrary data)
        for (var i = 0; i < pingDataSize; i++)
        {
            frame[frameHeaderSize + i] = (byte)i;
        }

        return frame;
    }

    private static ReadOnlyMemory<byte> EncodeHeaders(string method, string path, string authority = "localhost")
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":method", method),
            new(":path", path),
            new(":scheme", "https"),
            new(":authority", authority),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: true);

        return new Memory<byte>(buffer, 0, written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void PreStart_should_emit_settings_frame()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        Assert.Single(ops.EmittedOutbound);
        Assert.IsType<TransportData>(ops.EmittedOutbound[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_with_headers_should_produce_request_with_stream_id()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Verify stream ID was stored in request options
        Assert.True(request.Options.TryGetValue(StreamIdKey.Http2, out var streamId));
        Assert.Equal(1, streamId);

        // Verify request properties
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("/", request.RequestUri?.AbsolutePath);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_with_headers_incomplete_should_not_emit_request_until_end_headers()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        // Split header block: first part without EndHeaders
        var partSize = headerBlock.Length / 2;
        var headersFrameData = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..partSize],
            endStream: false,
            endHeaders: false);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        // No request emitted yet, waiting for CONTINUATION
        Assert.Empty(ops.EmittedRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeClientData_with_ping_should_echo_ack()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.EmittedOutbound.Clear();

        var pingFrameData = BuildPingFrame(isAck: false);
        var buffer = TransportBuffer.Rent(pingFrameData.Length);
        pingFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = pingFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedOutbound);
        var outbound = ops.EmittedOutbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var responseData = transportData.Buffer.Span;

        // Frame type should be PING (0x6), flags should include ACK (0x1)
        Assert.Equal((byte)FrameType.Ping, responseData[3]);
        Assert.True((responseData[4] & (byte)PingFlags.Ack) != 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void DecodeClientData_with_settings_should_ack()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();
        ops.EmittedOutbound.Clear();

        var settingsFrameData = BuildSettingsFrame(isAck: false);
        var buffer = TransportBuffer.Rent(settingsFrameData.Length);
        settingsFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = settingsFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedOutbound);
        var outbound = ops.EmittedOutbound[0];
        Assert.IsType<TransportData>(outbound);

        var transportData = (TransportData)outbound;
        var responseData = transportData.Buffer.Span;

        // Frame type should be SETTINGS (0x4), flags should include ACK (0x1)
        Assert.Equal((byte)FrameType.Settings, responseData[3]);
        Assert.True((responseData[4] & (byte)Settings.Ack) != 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void OnResponse_should_encode_and_emit_frames()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Receive a request first
        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Now send a response
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent("Hello, World!")
        };

        ops.EmittedOutbound.Clear();
        sm.OnResponse(response);

        // Should emit response frames
        Assert.NotEmpty(ops.EmittedOutbound);

        // At minimum, should have HEADERS frame
        var outbound = ops.EmittedOutbound[0];
        Assert.IsType<TransportData>(outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void CanAcceptResponse_should_be_true_when_request_received()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        Assert.False(sm.CanAcceptResponse);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        sm.DecodeClientData(new TransportData(buffer));

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Cleanup_should_dispose_decoder()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Should not throw
        sm.Cleanup();
    }
}


