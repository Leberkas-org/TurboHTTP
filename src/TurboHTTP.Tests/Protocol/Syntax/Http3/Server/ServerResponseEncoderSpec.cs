using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

[Trait("Component", "Http3ServerResponseEncoder")]
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
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        var frame = _encoder.EncodeHeaders(response);

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
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test response body"u8.ToArray()),
        };

        var frame = _encoder.EncodeHeaders(response);

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
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Created)
        {
            Content = new ByteArrayContent("test"u8.ToArray()),
        };
        response.Headers.Add("custom-header", "value");

        var headersFrame = _encoder.EncodeHeaders(response);

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
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("connection", "close");
        response.Headers.Add("transfer-encoding", "chunked");
        response.Headers.Add("custom-allowed", "yes");

        var headersFrame = _encoder.EncodeHeaders(response);

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
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("X-Custom-Header", "value");
        response.Headers.Add("Server", "TestServer");

        var headersFrame = _encoder.EncodeHeaders(response);

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
        var content = new ByteArrayContent("data"u8.ToArray());
        content.Headers.ContentType = new("application/json");
        content.Headers.ContentLength = 4;

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = content,
        };

        var headersFrame = _encoder.EncodeHeaders(response);

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
        var largeData = new byte[32768]; // Larger than max frame size (16384)
        Array.Fill(largeData, (byte)'x');

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(largeData),
        };

        var frame = _encoder.EncodeHeaders(response);

        // Synchronize encoder instructions to decoder's table
        var encoderInstructions = _encoder.EncoderInstructions;
        if (!encoderInstructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(encoderInstructions.Span);
        }

        Assert.IsType<HeadersFrame>(frame);
    }
}