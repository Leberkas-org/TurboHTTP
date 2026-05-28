using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Encoder;

public sealed class Http2ServerResponseEncoderSpec
{
    private readonly Http2ServerEncoder _encoder = new();
    private readonly HpackDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_no_body_returns_single_HeadersFrame_with_endStream_true()
    {
        var ctx = ServerTestContext.CreateResponse();
        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.True(frame.EndStream);
        Assert.True(frame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void EncodeHeaders_with_body_flag_returns_HeadersFrame_without_endStream()
    {
        var ctx = ServerTestContext.CreateResponse();
        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: true);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void EncodeHeaders_response_headers_are_HPACK_encoded()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["x-custom-header"] = "test-value";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);

        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);
        var statusHeader = decodedHeaders.FirstOrDefault(h => h.Name == ":status");
        var customHeader = decodedHeaders.FirstOrDefault(h => h.Name == "x-custom-header");

        Assert.True(statusHeader.Name == ":status");
        Assert.Equal("200", statusHeader.Value);
        Assert.True(customHeader.Name == "x-custom-header");
        Assert.Equal("test-value", customHeader.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_status_pseudo_header_is_first()
    {
        var ctx = ServerTestContext.CreateResponse(201);
        ctx.Get<IHttpResponseFeature>()?.Headers["x-first"] = "value";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.NotEmpty(decodedHeaders);
        Assert.Equal(":status", decodedHeaders[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void EncodeHeaders_filters_forbidden_headers()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["connection"] = "close";
        ctx.Get<IHttpResponseFeature>()?.Headers["transfer-encoding"] = "chunked";
        ctx.Get<IHttpResponseFeature>()?.Headers["x-allowed"] = "yes";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var connectionHeader = decodedHeaders.FirstOrDefault(h => h.Name == "connection");
        var transferHeader = decodedHeaders.FirstOrDefault(h => h.Name == "transfer-encoding");
        var allowedHeader = decodedHeaders.FirstOrDefault(h => h.Name == "x-allowed");

        Assert.False(connectionHeader.Name == "connection");
        Assert.False(transferHeader.Name == "transfer-encoding");
        Assert.True(allowedHeader.Name == "x-allowed");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void EncodeHeaders_204_NoContent_no_body_returns_endStream_true()
    {
        var ctx = ServerTestContext.CreateResponse(204);
        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void EncodeHeaders_response_with_content_headers()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["content-type"] = "application/json";
        ctx.Get<IHttpResponseFeature>()?.Headers["content-length"] = "4";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var contentType = decodedHeaders.FirstOrDefault(h => h.Name == "content-type");
        var contentLength = decodedHeaders.FirstOrDefault(h => h.Name == "content-length");

        Assert.True(contentType.Name == "content-type");
        Assert.Contains("application/json", contentType.Value);
        Assert.True(contentLength.Name == "content-length");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void EncodeHeaders_response_headers_are_lowercase()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["X-Custom-Header"] = "value";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var header = decodedHeaders.FirstOrDefault(h => h.Name == "x-custom-header");
        Assert.True(header.Name == "x-custom-header");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_multiple_responses_reuses_lists()
    {
        var ctx1 = ServerTestContext.CreateResponse();
        var ctx2 = ServerTestContext.CreateResponse(404);

        var frames1 = _encoder.EncodeHeaders(ctx1, streamId: 1, hasBody: false);
        var frames2 = _encoder.EncodeHeaders(ctx2, streamId: 3, hasBody: false);

        Assert.NotNull(frames1);
        Assert.NotNull(frames2);
        Assert.Single(frames1);
        Assert.Single(frames2);
    }
}
