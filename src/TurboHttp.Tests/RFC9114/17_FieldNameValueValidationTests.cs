using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §4.2 — Field name/value validation for HTTP/3.
/// Covers uppercase rejection, connection-specific header rejection, and TE header rules.
/// </summary>
public sealed class FieldNameValueValidationTests
{
    // ───────────────────────── Uppercase Field Names ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-001: Lowercase field name accepted")]
    public void Lowercase_field_name_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
            ("x-custom", "value"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-002: Uppercase field name rejected")]
    public void Uppercase_field_name_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("Content-Type", "text/html"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("uppercase", ex.Message);
        Assert.Contains("Content-Type", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-003: Mixed-case field name rejected")]
    public void Mixed_case_field_name_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("Accept-Encoding", "gzip"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Accept-Encoding", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-004: All-uppercase field name rejected")]
    public void All_uppercase_field_name_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("HOST", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("HOST", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-005: Pseudo-headers skip uppercase validation")]
    public void Pseudo_headers_skip_uppercase_validation()
    {
        // Pseudo-headers start with ':' and are validated elsewhere
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Theory(DisplayName = "RFC-9114-4.2-fv-006: Various lowercase names accepted")]
    [InlineData("accept")]
    [InlineData("content-length")]
    [InlineData("x-request-id")]
    [InlineData("cache-control")]
    [InlineData("user-agent")]
    public void Various_lowercase_names_accepted(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Theory(DisplayName = "RFC-9114-4.2-fv-007: Various uppercase names rejected")]
    [InlineData("Accept")]
    [InlineData("Content-Length")]
    [InlineData("X-Request-ID")]
    [InlineData("Cache-Control")]
    [InlineData("User-Agent")]
    public void Various_uppercase_names_rejected(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────────────────── Connection-Specific Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-008: Connection header rejected")]
    public void Connection_header_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("connection", "keep-alive"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Connection", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-009: Transfer-Encoding header rejected")]
    public void Transfer_encoding_header_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("transfer-encoding", "chunked"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Transfer-Encoding", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-010: Upgrade header rejected")]
    public void Upgrade_header_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("upgrade", "h2c"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Upgrade", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-011: Proxy-Connection header rejected")]
    public void Proxy_connection_header_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("proxy-connection", "keep-alive"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Proxy-Connection", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-012: Keep-Alive header rejected")]
    public void Keep_alive_header_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("keep-alive", "timeout=5"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Keep-Alive", ex.Message);
    }

    // ───────────────────────── TE Header Special Case ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-013: TE header with value 'trailers' accepted")]
    public void Te_header_with_trailers_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("te", "trailers"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-014: TE header with value 'gzip' rejected")]
    public void Te_header_with_gzip_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("te", "gzip"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("TE", ex.Message);
        Assert.Contains("trailers", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-015: TE header with value 'chunked' rejected")]
    public void Te_header_with_chunked_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("te", "chunked"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-016: TE header with value 'trailers, gzip' rejected")]
    public void Te_header_with_trailers_and_gzip_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("te", "trailers, gzip"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-017: TE header with empty value rejected")]
    public void Te_header_with_empty_value_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("te", ""),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────────────────── Integration: Request Encoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-018: Encoder strips connection-specific headers from request")]
    public void Encoder_strips_connection_specific_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "text/html");
        // Connection and Keep-Alive are silently stripped by BuildHeaderList
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        // Verify connection-specific headers were stripped
        Assert.DoesNotContain(headers, h =>
            string.Equals(h.Name, "connection", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headers, h =>
            string.Equals(h.Name, "keep-alive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-019: Encoder allows TE: trailers on request")]
    public void Encoder_allows_te_trailers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("te", "trailers");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "te" && h.Value == "trailers");
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-020: Encoder lowercases header names from HttpRequestMessage")]
    public void Encoder_lowercases_header_names()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        // Should not throw — BuildHeaderList lowercases via ToLowerInvariant()
        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "accept" && h.Value == "text/html");
    }

    // ───────────────────────── Integration: Response Decoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-021: Decoder rejects uppercase field name in response")]
    public void Decoder_rejects_uppercase_in_response()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("Content-Type", "text/html"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("uppercase", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-022: Decoder rejects connection header in response")]
    public void Decoder_rejects_connection_in_response()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("connection", "close"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Connection", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-023: Decoder rejects transfer-encoding in response")]
    public void Decoder_rejects_transfer_encoding_in_response()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("transfer-encoding", "chunked"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────────────────── ValidateFieldName Unit Tests ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2-fv-024: ValidateFieldName accepts numbers and hyphens")]
    public void ValidateFieldName_accepts_numbers_and_hyphens()
    {
        Http3FieldValidator.ValidateFieldName("x-request-123");
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-025: ValidateFieldName reports position of uppercase char")]
    public void ValidateFieldName_reports_position()
    {
        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateFieldName("content-Type"));
        Assert.Contains("position 8", ex.Message);
    }

    // ───────────────────────── ValidateConnectionSpecific Unit Tests ─────────────────────────

    [Theory(DisplayName = "RFC-9114-4.2-fv-026: All connection-specific headers rejected")]
    [InlineData("connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [InlineData("proxy-connection")]
    [InlineData("keep-alive")]
    public void All_connection_specific_headers_rejected(string name)
    {
        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateConnectionSpecific(name, "value"));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-027: Non-connection-specific header accepted")]
    public void Non_connection_specific_header_accepted()
    {
        Http3FieldValidator.ValidateConnectionSpecific("content-type", "text/html");
    }

    [Fact(DisplayName = "RFC-9114-4.2-fv-028: TE trailers case-insensitive match")]
    public void Te_trailers_case_insensitive()
    {
        Http3FieldValidator.ValidateConnectionSpecific("te", "Trailers");
    }
}
