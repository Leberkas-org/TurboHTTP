using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Encoder;

public sealed class Http2ServerResponseFrameSpec
{
    private readonly Http2ServerEncoder _encoder = new();
    private readonly HpackDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_produces_HeadersFrame()
    {
        var ctx = ServerTestContext.CreateResponse();

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headersFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_status_pseudo_header_present_in_HPACK_block()
    {
        var ctx = ServerTestContext.CreateResponse(201);

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var statusHeader = decodedHeaders.FirstOrDefault(h => h.Name == ":status");
        Assert.Equal(":status", statusHeader.Name);
        Assert.Equal("201", statusHeader.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_status_pseudo_header_is_first_in_header_block()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["x-first"] = "value";
        ctx.Get<IHttpResponseFeature>()?.Headers["x-second"] = "value";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.NotEmpty(decodedHeaders);
        Assert.Equal(":status", decodedHeaders[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_headers_only_response_endStream_on_HeadersFrame()
    {
        var ctx = ServerTestContext.CreateResponse(204);

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream);
        Assert.True(headersFrame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void EncodeHeaders_response_with_body_does_not_set_endStream()
    {
        var ctx = ServerTestContext.CreateResponse();

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: true);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void EncodeHeaders_no_body_sets_endStream()
    {
        var ctx = ServerTestContext.CreateResponse();

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void EncodeHeaders_filters_forbidden_connection_specific_headers()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["connection"] = "close";
        ctx.Get<IHttpResponseFeature>()?.Headers["transfer-encoding"] = "chunked";
        ctx.Get<IHttpResponseFeature>()?.Headers["x-allowed"] = "yes";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var connectionHeader = decodedHeaders.FirstOrDefault(h => h.Name == "connection");
        var teHeader = decodedHeaders.FirstOrDefault(h => h.Name == "transfer-encoding");
        var allowedHeader = decodedHeaders.FirstOrDefault(h => h.Name == "x-allowed");

        Assert.True(string.IsNullOrEmpty(connectionHeader.Name));
        Assert.True(string.IsNullOrEmpty(teHeader.Name));
        Assert.Equal("x-allowed", allowedHeader.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void EncodeHeaders_header_names_lowercased()
    {
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["X-Custom-Header"] = "value";
        ctx.Get<IHttpResponseFeature>()?.Headers["X-Another-Header"] = "another";

        var frames = _encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var decodedHeaders = _decoder.Decode(headersFrame.HeaderBlockFragment.Span);

        var customHeader = decodedHeaders.FirstOrDefault(h => h.Name.Contains("custom"));
        var anotherHeader = decodedHeaders.FirstOrDefault(h => h.Name.Contains("another"));

        Assert.Equal("x-custom-header", customHeader.Name);
        Assert.Equal("x-another-header", anotherHeader.Name);
    }
}
