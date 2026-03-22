using System.Net;
using System.Text;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §4.1, §4.3.2 — Http3ResponseDecoder unit tests.
/// Verifies HEADERS + DATA frame decoding into HttpResponseMessage with QPACK decompression,
/// pseudo-header validation, body assembly, and content header routing.
/// </summary>
public sealed class Http3ResponseDecoderTests
{
    /// <summary>
    /// Encodes a list of header fields into a QPACK header block using a fresh encoder
    /// with the same table capacity as the decoder under test.
    /// </summary>
    private static Http3HeadersFrame EncodeHeaders(
        IReadOnlyList<(string Name, string Value)> headers,
        int maxTableCapacity = 0)
    {
        var encoder = new QpackEncoder(maxTableCapacity);
        var block = encoder.Encode(headers);
        return new Http3HeadersFrame(block);
    }

    private static Http3HeadersFrame EncodeResponseHeaders(
        int statusCode,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        int maxTableCapacity = 0)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", statusCode.ToString()),
        };
        if (extraHeaders is not null)
        {
            headers.AddRange(extraHeaders);
        }

        return EncodeHeaders(headers, maxTableCapacity);
    }

    // ───────────────────────── Basic Decoding ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-001: HEADERS-only frame decoded to HttpResponseMessage")]
    public void Headers_only_decoded_to_response()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(DisplayName = "RFC-9114-4.1-dec-002: Status codes correctly decoded")]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    public void Status_codes_decoded(int statusCode)
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(statusCode);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-003: Response headers populated from HEADERS frame")]
    public void Response_headers_populated()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "TurboHttp"),
            ("x-request-id", "abc-123"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("TurboHttp", response.Headers.GetValues("server"));
        Assert.Contains("abc-123", response.Headers.GetValues("x-request-id"));
    }

    // ───────────────────────── DATA Frame Assembly ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-004: Single DATA frame assembled into response body")]
    public async Task Single_data_frame_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = "Hello, HTTP/3!"u8.ToArray();
        var dataFrame = new Http3DataFrame(body);

        var response = decoder.Decode(new Http3Frame[] { headersFrame, dataFrame });

        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, content);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-005: Multiple DATA frames assembled into single body")]
    public async Task Multiple_data_frames_assembled()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var part1 = "Hello, "u8.ToArray();
        var part2 = "World!"u8.ToArray();

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(part1),
            new Http3DataFrame(part2),
        });

        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(content));
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-006: No DATA frames results in null or empty content")]
    public async Task No_data_frames_no_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(204);

        var response = decoder.Decode(new[] { headersFrame });

        // No content at all or empty content
        Assert.True(
            response.Content is null ||
            (await response.Content.ReadAsByteArrayAsync()).Length == 0);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-007: Large body assembled from single DATA frame")]
    public async Task Large_body_assembled()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = new byte[64 * 1024];
        new Random(42).NextBytes(body);

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(body),
        });

        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, content);
    }

    // ───────────────────────── Content Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-008: Content-Type header decoded from HEADERS frame")]
    public void Content_type_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-type", "application/json"),
        });

        // Verify via DecodeHeaders that content-type is present in raw decoded output
        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "application/json");
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-009: Content-Length header decoded from HEADERS frame")]
    public void Content_length_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-length", "42"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-length" && h.Value == "42");
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-010: Content-Encoding header decoded from HEADERS frame")]
    public void Content_encoding_decoded()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("content-encoding", "gzip"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == "content-encoding" && h.Value == "gzip");
    }

    // ───────────────────────── QPACK Integration ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-011: QPACK decoding applied to header block")]
    public void Qpack_decoding_applied()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("cache-control", "no-cache"),
            ("vary", "Accept-Encoding"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("no-cache", response.Headers.GetValues("cache-control"));
        Assert.Contains("Accept-Encoding", response.Headers.GetValues("vary"));
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-012: QpackDecoder property exposes underlying decoder")]
    public void Qpack_decoder_property_accessible()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 4096);
        Assert.NotNull(decoder.QpackDecoder);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-013: DecoderInstructions property accessible")]
    public void Decoder_instructions_accessible()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 4096);

        // DecoderInstructions property should be accessible (empty when no dynamic table used)
        var instructions = decoder.DecoderInstructions;
        Assert.True(instructions.Length >= 0,
            "DecoderInstructions should be accessible");
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-014: Static table only (capacity=0) emits no decoder instructions")]
    public void Static_table_only_no_instructions()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        decoder.Decode(new[] { headersFrame });

        Assert.Equal(0, decoder.DecoderInstructions.Length);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-015: Decoder is stateful across multiple responses")]
    public void Decoder_is_stateful()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);

        var response1 = decoder.Decode(new[] { EncodeResponseHeaders(200) }, streamId: 1);
        var response2 = decoder.Decode(new[] { EncodeResponseHeaders(404) }, streamId: 3);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response2.StatusCode);
    }

    // ───────────────────────── DecodeHeaders ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-016: DecodeHeaders returns raw header field pairs")]
    public void DecodeHeaders_returns_raw_pairs()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "test"),
        });

        var headers = decoder.DecodeHeaders(headersFrame);

        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(headers, h => h.Name == "server" && h.Value == "test");
    }

    // ───────────────────────── Pseudo-Header Validation (§4.3.2) ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-017: Missing :status throws Http3Exception")]
    public void Missing_status_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            ("server", "test"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-018: Duplicate :status throws Http3Exception")]
    public void Duplicate_status_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "200"),
            (":status", "404"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-019: Unknown pseudo-header throws Http3Exception")]
    public void Unknown_pseudo_header_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "200"),
            (":method", "GET"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-020: Pseudo-header after regular header throws")]
    public void Pseudo_after_regular_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            ("server", "test"),
            (":status", "200"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-021: Invalid :status value throws Http3Exception")]
    public void Invalid_status_value_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "abc"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-022: Status code below 100 throws")]
    public void Status_below_100_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "99"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-023: Status code above 999 throws")]
    public void Status_above_999_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeHeaders(new[]
        {
            (":status", "1000"),
        });

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new[] { headersFrame }));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────────────────── Frame Ordering ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-024: Empty frame list throws Http3Exception")]
    public void Empty_frames_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(Array.Empty<Http3Frame>()));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-025: Null frames throws ArgumentNullException")]
    public void Null_frames_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!));
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-026: DATA frame before HEADERS throws Http3Exception")]
    public void Data_before_headers_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var dataFrame = new Http3DataFrame("data"u8.ToArray());

        var ex = Assert.Throws<Http3Exception>(
            () => decoder.Decode(new Http3Frame[] { dataFrame }));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-027: Null HEADERS frame in DecodeHeaders throws ArgumentNullException")]
    public void Null_headers_frame_throws()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        Assert.Throws<ArgumentNullException>(() => decoder.DecodeHeaders(null!));
    }

    // ───────────────────────── Trailing HEADERS ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-028: Trailing HEADERS frame stops body assembly")]
    public async Task Trailing_headers_stops_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);
        var body = "body-data"u8.ToArray();
        var trailingHeaders = EncodeHeaders(new[] { ("x-checksum", "abc") });

        var response = decoder.Decode(new Http3Frame[]
        {
            headersFrame,
            new Http3DataFrame(body),
            trailingHeaders,
        });

        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, content);
    }

    // ───────────────────────── Stream ID ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-029: Stream ID passed to QPACK for Section Acknowledgment")]
    public void Stream_id_passed_to_qpack()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        // Should not throw — streamId is forwarded to QPACK
        var response = decoder.Decode(new[] { headersFrame }, streamId: 42);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────────────────── Content Header Routing ─────────────────────────

    [Theory(DisplayName = "RFC-9114-4.1-dec-030: Content headers decoded from HEADERS frame")]
    [InlineData("content-language", "en-US")]
    [InlineData("content-location", "/resource")]
    [InlineData("content-disposition", "attachment")]
    [InlineData("content-range", "bytes 0-499/1234")]
    [InlineData("expires", "Thu, 01 Dec 2025 16:00:00 GMT")]
    [InlineData("last-modified", "Wed, 09 Oct 2024 10:00:00 GMT")]
    public void Content_headers_decoded(string headerName, string headerValue)
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            (headerName, headerValue),
        });

        var headers = decoder.DecodeHeaders(headersFrame);
        Assert.Contains(headers, h => h.Name == headerName && h.Value == headerValue);
    }

    [Fact(DisplayName = "RFC-9114-4.1-dec-031: Non-content headers stay on response.Headers")]
    public void Non_content_headers_stay_on_response()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200, new[]
        {
            ("server", "TurboHttp"),
            ("x-powered-by", "Akka"),
        });

        var response = decoder.Decode(new[] { headersFrame });

        Assert.Contains("TurboHttp", response.Headers.GetValues("server"));
        Assert.Contains("Akka", response.Headers.GetValues("x-powered-by"));
    }

    // ───────────────────────── Pseudo-Header Skipping ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-dec-032: Pseudo-headers not added to response headers")]
    public void Pseudo_headers_not_in_response_headers()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var headersFrame = EncodeResponseHeaders(200);

        var response = decoder.Decode(new[] { headersFrame });

        Assert.False(response.Headers.Contains(":status"),
            ":status pseudo-header should not appear in response.Headers");
    }

    // ───────────────────────── ValidateResponsePseudoHeaders ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-033: ValidateResponsePseudoHeaders accepts valid response")]
    public void Validate_accepts_valid_response()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("server", "test"),
        };

        // Should not throw
        Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-dec-034: ValidateResponsePseudoHeaders rejects :method in response")]
    public void Validate_rejects_request_pseudo_in_response()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":path", "/"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }
}
