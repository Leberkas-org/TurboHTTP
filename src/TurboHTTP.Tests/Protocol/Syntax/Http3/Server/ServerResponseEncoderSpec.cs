using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class ServerResponseEncoderSpec
{
    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly Http3ServerEncoder _encoder;

    public ServerResponseEncoderSpec()
    {
        _encoder = new Http3ServerEncoder(_encoderTableSync);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_200_OK_returns_single_HEADERS_frame()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);

        var frame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        Assert.IsType<HeadersFrame>(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_200_with_body_returns_HEADERS_frame_only()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write("test response body"u8.ToArray());

        var frame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        Assert.IsType<HeadersFrame>(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_status_is_first_header()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 201);
        ctx.Get<IHttpResponseFeature>()?.Headers["custom-header"] = "value";
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write("test"u8.ToArray());

        var headersFrame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decodedHeaders = _decoderTableSync.Decoder.Decode(headersFrame.HeaderBlock.Span, streamId: 1);

        Assert.NotEmpty(decodedHeaders);
        Assert.Equal(":status", decodedHeaders[0].Name);
        Assert.Equal("201", decodedHeaders[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_forbidden_headers_are_filtered()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["connection"] = "close";
        ctx.Get<IHttpResponseFeature>()?.Headers["transfer-encoding"] = "chunked";
        ctx.Get<IHttpResponseFeature>()?.Headers["custom-allowed"] = "yes";

        var headersFrame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decodedHeaders = _decoderTableSync.Decoder.Decode(headersFrame.HeaderBlock.Span, streamId: 1);

        var headerNames = decodedHeaders.Select(h => h.Name).ToList();
        Assert.DoesNotContain("connection", headerNames);
        Assert.DoesNotContain("transfer-encoding", headerNames);
        Assert.Contains("custom-allowed", headerNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_header_names_are_lowercase()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["X-Custom-Header"] = "value";
        ctx.Get<IHttpResponseFeature>()?.Headers["Server"] = "TestServer";

        var headersFrame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decodedHeaders = _decoderTableSync.Decoder.Decode(headersFrame.HeaderBlock.Span, streamId: 1);

        var customHeader = decodedHeaders.FirstOrDefault(h => h.Name.Contains("custom"));
        Assert.Equal("x-custom-header", customHeader.Name);

        var serverHeader = decodedHeaders.FirstOrDefault(h => h.Name == "server");
        Assert.Equal("server", serverHeader.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_content_headers_are_included()
    {
        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseFeature>()?.Headers["content-type"] = "application/json";
        ctx.Get<IHttpResponseFeature>()?.Headers["content-length"] = "4";
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write("data"u8.ToArray());

        var headersFrame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        var decodedHeaders = _decoderTableSync.Decoder.Decode(headersFrame.HeaderBlock.Span, streamId: 1);

        var headerNames = decodedHeaders.Select(h => h.Name).ToList();
        Assert.Contains("content-type", headerNames);
        Assert.Contains("content-length", headerNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_with_large_body_returns_HEADERS_frame_only()
    {
        var largeData = new byte[32 * 1024]; // Larger than max frame size (16384)
        Array.Fill(largeData, (byte)'x');

        var ctx = ServerTestContext.CreateH3Response(streamId: 1, statusCode: 200);
        ctx.Get<IHttpResponseBodyFeature>()?.Writer.Write(largeData);

        var frame = _encoder.EncodeHeaders(ctx);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        Assert.IsType<HeadersFrame>(frame);
    }
}