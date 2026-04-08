using System.Text;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Integration tests verifying Http3RequestEncoder QPACK encoding and
/// QpackEncoder/QpackDecoder dynamic table instruction exchange.
/// </summary>
public sealed class QpackIntegrationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_should_produce_headers_frame_with_qpack()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");

        var frames = encoder.Encode(request);

        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.True(headersFrame.HeaderBlock.Length > 0, "Header block should not be empty");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_should_produce_output_decodable_by_qpack_decoder()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");
        request.Headers.TryAddWithoutValidation("accept", "text/html");
        request.Headers.TryAddWithoutValidation("user-agent", "TurboHttp/1.0");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        // Verify pseudo-headers
        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/path?q=1" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com" });

        // Verify regular headers
        Assert.Contains(headers, h => h is { Name: "accept", Value: "text/html" });
        Assert.Contains(headers, h => h is { Name: "user-agent", Value: "TurboHttp/1.0" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_should_emit_data_frame_for_body()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/data")
        {
            Content = new StringContent("hello world", Encoding.UTF8, "text/plain"),
        };

        var frames = encoder.Encode(request);

        Assert.True(frames.Count >= 2, "Should have at least HEADERS + DATA frames");
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[1]);
        var body = Encoding.UTF8.GetString(dataFrame.Data.Span);
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encoder_should_filter_forbidden_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("accept", "application/json");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h is { Name: "accept", Value: "application/json" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_should_emit_qpack_encoder_instructions()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom", "custom-value");

        encoder.Encode(request);

        // With a non-zero table capacity, the encoder should emit instructions
        // for inserting new entries into the dynamic table
        Assert.True(encoder.EncoderInstructions.Length > 0,
            "Encoder should emit instructions when dynamic table is enabled");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Decoder_should_emit_qpack_decoder_instructions()
    {
        // Use dynamic table: encoder inserts entries, decoder should emit section ack
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 4096);
        var qpackDecoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "dynamic-value"),
        };

        var headerBlock = qpackEncoder.Encode(headers);

        // Synchronize decoder's dynamic table with encoder's instructions
        var instructions = qpackEncoder.EncoderInstructions;
        if (instructions.Length > 0)
        {
            var instructionDecoder = new QpackInstructionDecoder();
            var remaining = instructions.Span;
            while (remaining.Length > 0)
            {
                var status = instructionDecoder.TryDecodeEncoderInstruction(remaining, out var instruction);
                if (status != QpackDecodeStatus.Success || instruction is null)
                {
                    break;
                }

                if (instruction.Type == EncoderInstructionType.InsertWithLiteralName)
                {
                    qpackDecoder.DynamicTable.Insert(instruction.NameString, instruction.ValueString);
                }
                else if (instruction.Type == EncoderInstructionType.InsertWithNameReference)
                {
                    string name;
                    if (instruction.IsStatic)
                    {
                        name = QpackStaticTable.Entries[instruction.NameIndex].Name;
                    }
                    else
                    {
                        var entry = qpackDecoder.DynamicTable.GetEntry(
                            qpackDecoder.DynamicTable.InsertCount - 1 - instruction.NameIndex);
                        name = entry!.Value.Name;
                    }
                    qpackDecoder.DynamicTable.Insert(name, instruction.ValueString);
                }

                // The instruction decoder consumes data internally; for single-shot
                // parsing we break after each instruction to avoid double-processing
                remaining = ReadOnlySpan<byte>.Empty;
            }
        }

        var decoded = qpackDecoder.Decode(headerBlock.Span, streamId: 4);

        // Verify headers decoded correctly through dynamic table
        Assert.Contains(decoded, h => h is { Name: ":status", Value: "200" });
        Assert.Contains(decoded, h => h is { Name: "x-custom", Value: "dynamic-value" });

        // Decoder should emit section acknowledgment when dynamic table was used
        if (qpackDecoder.DynamicTable.InsertCount > 0)
        {
            Assert.True(qpackDecoder.DecoderInstructions.Length > 0,
                "Decoder should emit section acknowledgment after using dynamic table entries");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encoder_should_reject_null_uri()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request));
    }
}
