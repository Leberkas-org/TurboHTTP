using System.Net;
using System.Text;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// Integration tests verifying that Http3RequestEncoder uses QpackEncoder
/// and Http3ResponseDecoder uses QpackDecoder for header compression/decompression.
/// </summary>
public sealed class QpackIntegrationTests
{
    // ───────────────────────── Encoder Integration ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-qi-001: Http3RequestEncoder produces HEADERS frame with QPACK-compressed headers")]
    public void Encoder_produces_headers_frame_with_qpack()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");

        var frames = encoder.Encode(request);

        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.True(headersFrame.HeaderBlock.Length > 0, "Header block should not be empty");
    }

    [Fact(DisplayName = "RFC-9114-4.1-qi-002: Encoder roundtrip — encoded request headers decodable by QpackDecoder")]
    public void Encoder_output_decodable_by_qpack_decoder()
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
        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/path?q=1");
        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");

        // Verify regular headers
        Assert.Contains(headers, h => h.Name == "accept" && h.Value == "text/html");
        Assert.Contains(headers, h => h.Name == "user-agent" && h.Value == "TurboHttp/1.0");
    }

    [Fact(DisplayName = "RFC-9114-4.1-qi-003: Encoder emits DATA frame for request body")]
    public void Encoder_emits_data_frame_for_body()
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

    [Fact(DisplayName = "RFC-9114-4.2-qi-004: Encoder filters connection-specific headers")]
    public void Encoder_filters_forbidden_headers()
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
        Assert.Contains(headers, h => h.Name == "accept" && h.Value == "application/json");
    }

    // ───────────────────────── Decoder Integration ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-qi-005: Http3ResponseDecoder decodes HEADERS frame via QPACK")]
    public void Decoder_decodes_headers_frame_via_qpack()
    {
        // Encode a response header block using QpackEncoder
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var responseHeaders = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
            ("server", "TurboHttp"),
        };

        var headerBlock = qpackEncoder.Encode(responseHeaders);
        var headersFrame = new Http3HeadersFrame(headerBlock);
        var frames = new List<Http3Frame> { headersFrame };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var response = decoder.Decode(frames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("TurboHttp", response.Headers.GetValues("server"));
    }

    [Fact(DisplayName = "RFC-9114-4.1-qi-006: Full encode-decode roundtrip with body")]
    public async Task Full_encode_decode_roundtrip_with_body()
    {
        // Step 1: Encode a request
        var requestEncoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/submit")
        {
            Content = new StringContent("{\"key\":\"value\"}", Encoding.UTF8, "application/json"),
        };
        var requestFrames = requestEncoder.Encode(request);
        Assert.True(requestFrames.Count >= 2);

        // Step 2: Encode a response
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var responseHeaders = new List<(string Name, string Value)>
        {
            (":status", "201"),
            ("content-type", "application/json"),
            ("location", "https://api.example.com/submit/42"),
        };
        var responseHeaderBlock = qpackEncoder.Encode(responseHeaders);
        var responseBody = Encoding.UTF8.GetBytes("{\"id\":42}");

        var responseFrames = new List<Http3Frame>
        {
            new Http3HeadersFrame(responseHeaderBlock),
            new Http3DataFrame(responseBody),
        };

        // Step 3: Decode the response
        var responseDecoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var response = responseDecoder.Decode(responseFrames);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("https://api.example.com/submit/42",
            response.Headers.GetValues("location").First());
        Assert.NotNull(response.Content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"id\":42}", body);
    }

    // ───────────────────────── Dynamic Table Integration ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-qi-007: Encoder emits QPACK encoder instructions with dynamic table")]
    public void Encoder_emits_qpack_encoder_instructions()
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

    [Fact(DisplayName = "RFC-9114-4.1-qi-008: Decoder emits QPACK decoder instructions after dynamic table usage")]
    public void Decoder_emits_qpack_decoder_instructions()
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
        Assert.Contains(decoded, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(decoded, h => h.Name == "x-custom" && h.Value == "dynamic-value");

        // Decoder should emit section acknowledgment when dynamic table was used
        if (qpackDecoder.DynamicTable.InsertCount > 0)
        {
            Assert.True(qpackDecoder.DecoderInstructions.Length > 0,
                "Decoder should emit section acknowledgment after using dynamic table entries");
        }
    }

    // ───────────────────────── Error Handling ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3-qi-009: Decoder rejects missing :status pseudo-header")]
    public void Decoder_rejects_missing_status()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.1-qi-010: Decoder rejects empty frame list")]
    public void Decoder_rejects_empty_frames()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(new List<Http3Frame>()));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.1-qi-011: Decoder rejects DATA frame before HEADERS")]
    public void Decoder_rejects_data_before_headers()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var frames = new List<Http3Frame> { new Http3DataFrame(new byte[] { 0x01 }) };
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.3-qi-012: Encoder rejects null request URI")]
    public void Encoder_rejects_null_uri()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request));
    }
}
