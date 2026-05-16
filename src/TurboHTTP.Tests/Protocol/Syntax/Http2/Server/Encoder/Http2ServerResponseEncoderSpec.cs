using System.Net;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Encoder;

public sealed class Http2ServerResponseEncoderSpec
{
    private readonly Http2ServerEncoder _encoder = new();
    private readonly HpackDecoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_no_body_returns_single_HeadersFrame_with_endStream_true()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);

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
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test body"u8.ToArray()),
        };
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: true);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(headersFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void EncodeHeaders_response_headers_are_HPACK_encoded()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Headers.Add("x-custom-header", "test-value");

        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);
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
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new ByteArrayContent([]),
        };
        response.Headers.Add("x-first", "value");

        var hpackBlock = _encoder.EncodeToHpackBlock(response);
        var decodedHeaders = _decoder.Decode(hpackBlock);

        Assert.NotEmpty(decodedHeaders);
        Assert.Equal(":status", decodedHeaders[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void EncodeHeaders_filters_forbidden_headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Headers.TryAddWithoutValidation("connection", "close");
        response.Headers.TryAddWithoutValidation("transfer-encoding", "chunked");
        response.Headers.Add("x-allowed", "yes");

        var hpackBlock = _encoder.EncodeToHpackBlock(response);
        var decodedHeaders = _decoder.Decode(hpackBlock);

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
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var frames = _encoder.EncodeHeaders(response, streamId: 1, hasBody: false);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void EncodeHeaders_response_with_content_headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test"u8.ToArray()),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        response.Content.Headers.ContentLength = 4;

        var hpackBlock = _encoder.EncodeToHpackBlock(response);
        var decodedHeaders = _decoder.Decode(hpackBlock);

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
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Headers.Add("X-Custom-Header", "value");

        var hpackBlock = _encoder.EncodeToHpackBlock(response);
        var decodedHeaders = _decoder.Decode(hpackBlock);

        var header = decodedHeaders.FirstOrDefault(h => h.Name == "x-custom-header");
        Assert.True(header.Name == "x-custom-header");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void EncodeHeaders_multiple_responses_reuses_lists()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK);
        var response2 = new HttpResponseMessage(HttpStatusCode.NotFound);

        var frames1 = _encoder.EncodeHeaders(response1, streamId: 1, hasBody: false);
        var frames2 = _encoder.EncodeHeaders(response2, streamId: 3, hasBody: false);

        Assert.NotNull(frames1);
        Assert.NotNull(frames2);
        Assert.Single(frames1);
        Assert.Single(frames2);
    }
}