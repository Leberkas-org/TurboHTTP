using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

[Trait("Component", "Http3ServerEncoderHardening")]
public sealed class Http3ServerEncoderHardeningSpec
{
    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
    private readonly Http3ServerEncoder _encoder;

    public Http3ServerEncoderHardeningSpec()
    {
        _encoder = new Http3ServerEncoder(_encoderTableSync);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void EncodeHeaders_status_should_be_first()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Created)
        {
            Content = new ByteArrayContent("test"u8.ToArray()),
        };
        response.Headers.Add("x-test", "value");

        var frame = _encoder.EncodeHeaders(response);

        var decoded = DecodeFrame(frame);

        Assert.NotEmpty(decoded);
        Assert.Equal(":status", decoded[0].Name);
        Assert.Equal("201", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void EncodeHeaders_should_filter_forbidden_headers()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("connection", "close");
        response.Headers.Add("transfer-encoding", "chunked");
        response.Headers.Add("x-allowed", "yes");

        var frame = _encoder.EncodeHeaders(response);

        var decoded = DecodeFrame(frame);

        Assert.DoesNotContain(decoded, h => h.Name == "connection");
        Assert.DoesNotContain(decoded, h => h.Name == "transfer-encoding");
        Assert.Contains(decoded, h => h.Name == "x-allowed" && h.Value == "yes");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void EncodeHeaders_should_lowercase_header_names()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("X-Custom-Header", "test-value");
        response.Headers.Add("Server", "TestServer");

        var frame = _encoder.EncodeHeaders(response);

        var decoded = DecodeFrame(frame);

        Assert.Contains(decoded, h => h.Name == "x-custom-header" && h.Value == "test-value");
        Assert.Contains(decoded, h => h.Name == "server" && h.Value == "TestServer");
        Assert.DoesNotContain(decoded, h => h.Name == "X-Custom-Header");
        Assert.DoesNotContain(decoded, h => h.Name == "Server");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeHeaders_should_include_content_headers()
    {
        var content = new ByteArrayContent("data"u8.ToArray());
        content.Headers.ContentType = new("application/json");
        content.Headers.ContentLength = 4;

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = content,
        };

        var frame = _encoder.EncodeHeaders(response);

        var decoded = DecodeFrame(frame);

        Assert.Contains(decoded, h => h.Name == "content-type" && h.Value.Contains("application/json"));
        Assert.Contains(decoded, h => h.Name == "content-length" && h.Value == "4");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void EncodeHeaders_multiple_responses_should_not_cross_contaminate()
    {
        var response1 = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response1.Headers.Add("x-first", "first-value");

        var response2 = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response2.Headers.Add("x-second", "second-value");

        // Encode response1 with its own encoder/decoder pair
        var encoder1Sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
        var encoder1 = new Http3ServerEncoder(encoder1Sync);
        var frame1 = encoder1.EncodeHeaders(response1);

        var decoderSync1 = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
        if (!encoder1.EncoderInstructions.IsEmpty)
        {
            decoderSync1.ProcessEncoderInstructions(encoder1.EncoderInstructions.Span);
        }
        var decoded1 = decoderSync1.Decoder.Decode(frame1.HeaderBlock.Span, streamId: 1);

        // Encode response2 with its own encoder/decoder pair
        var encoder2Sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
        var encoder2 = new Http3ServerEncoder(encoder2Sync);
        var frame2 = encoder2.EncodeHeaders(response2);

        var decoderSync2 = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);
        if (!encoder2.EncoderInstructions.IsEmpty)
        {
            decoderSync2.ProcessEncoderInstructions(encoder2.EncoderInstructions.Span);
        }
        var decoded2 = decoderSync2.Decoder.Decode(frame2.HeaderBlock.Span, streamId: 3);

        // Verify each response has its own headers, not the other's
        var names1 = decoded1.Select(h => h.Name).ToList();
        var names2 = decoded2.Select(h => h.Name).ToList();

        Assert.Contains("x-first", names1);
        Assert.DoesNotContain("x-second", names1);

        Assert.Contains("x-second", names2);
        Assert.DoesNotContain("x-first", names2);
    }

    private IReadOnlyList<(string Name, string Value)> DecodeFrame(HeadersFrame frame)
    {
        var instructions = _encoder.EncoderInstructions;
        if (!instructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(instructions.Span);
        }

        return _decoderTableSync.Decoder.Decode(frame.HeaderBlock.Span, streamId: 1);
    }
}
