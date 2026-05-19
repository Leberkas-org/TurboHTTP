using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

public sealed class ResponseRetentionSpec
{
    private static TurboClientOptions MakeConfig() => new();

    private static HttpRequestMessage MakeGet(string path = "/")
        => new(HttpMethod.Get, $"https://example.com{path}");

    private static HeadersFrame MakeResponseHeaders(int streamId, bool endStream = true)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var hpack = encoder.Encode([(":status", "200"), ("content-type", "text/plain")]);
        return new HeadersFrame(streamId, hpack, endStream, endHeaders: true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_should_retain_response_when_rst_stream_no_error_follows_headers()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        // Send a request
        sm.OnRequest(MakeGet());

        // Simulate server sending response headers without END_STREAM, then RST_STREAM with NO_ERROR
        // The response should be retained and emitted to the caller
        var headersFrame = MakeResponseHeaders(1, endStream: false);
        var buffer = TransportBuffer.Rent(headersFrame.SerializedSize);
        var span = buffer.FullMemory.Span;
        headersFrame.WriteTo(ref span);
        buffer.Length = headersFrame.SerializedSize;

        sm.DecodeServerData(new TransportData(buffer));

        // After headers without END_STREAM, response should be available
        Assert.Single(ops.Responses);

        // Now send RST_STREAM with NO_ERROR
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.NoError);
        var rstBuffer = TransportBuffer.Rent(rstFrame.SerializedSize);
        var rstSpan = rstBuffer.FullMemory.Span;
        rstFrame.WriteTo(ref rstSpan);
        rstBuffer.Length = rstFrame.SerializedSize;

        sm.DecodeServerData(new TransportData(rstBuffer));

        // Response should still be retained (still single response)
        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.7")]
    public void FrameDecoder_should_decode_refused_stream_error_code()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.RefusedStream);
        var frames = decoder.Decode(rstFrame.Serialize());

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.RefusedStream, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.7")]
    public void FrameDecoder_should_decode_no_error_code()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.NoError);
        var frames = decoder.Decode(rstFrame.Serialize());

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.NoError, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void FrameDecoder_should_preserve_stream_id_in_rst_stream()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(42, Http2ErrorCode.Cancel);
        var frames = decoder.Decode(rstFrame.Serialize());

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(42, rst.StreamId);
    }
}
