using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §10.3 — Intermediary Encapsulation Attack Prevention.
/// Validates that field names containing invalid characters and requests
/// targeting prohibited origins are rejected.
/// </summary>
public sealed class IntermediaryEncapsulationTests
{
    // ───────────────────────── Invalid Token Characters in Field Names ─────────────────────────

    [Theory(DisplayName = "RFC-9114-10.3-ie-001: Field name with space rejected")]
    [InlineData("invalid name")]
    [InlineData("content type")]
    public void Field_name_with_space_rejected(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(DisplayName = "RFC-9114-10.3-ie-002: Field name with control characters rejected")]
    [InlineData("field\x00name")]
    [InlineData("field\x01name")]
    [InlineData("field\x7fname")]
    [InlineData("field\tname")]
    public void Field_name_with_control_chars_rejected(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(DisplayName = "RFC-9114-10.3-ie-003: Field name with separator characters rejected")]
    [InlineData("field(name")]
    [InlineData("field)name")]
    [InlineData("field<name")]
    [InlineData("field>name")]
    [InlineData("field@name")]
    [InlineData("field,name")]
    [InlineData("field;name")]
    [InlineData("field:name")]
    [InlineData("field\\name")]
    [InlineData("field\"name")]
    [InlineData("field/name")]
    [InlineData("field[name")]
    [InlineData("field]name")]
    [InlineData("field?name")]
    [InlineData("field=name")]
    [InlineData("field{name")]
    [InlineData("field}name")]
    public void Field_name_with_separator_chars_rejected(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-004: Empty field name rejected")]
    public void Empty_field_name_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(DisplayName = "RFC-9114-10.3-ie-005: Valid token characters in field names accepted")]
    [InlineData("x-custom-header")]
    [InlineData("content-type")]
    [InlineData("x!header")]
    [InlineData("x#header")]
    [InlineData("x$header")]
    [InlineData("x%header")]
    [InlineData("x&header")]
    [InlineData("x'header")]
    [InlineData("x*header")]
    [InlineData("x+header")]
    [InlineData("x.header")]
    [InlineData("x^header")]
    [InlineData("x_header")]
    [InlineData("x`header")]
    [InlineData("x|header")]
    [InlineData("x~header")]
    [InlineData("x-123")]
    public void Valid_token_characters_accepted(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-006: Field name with high-byte character rejected")]
    public void Field_name_with_high_byte_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("field\x80name", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    // ───────────────────────── Invalid Characters in Field Values ─────────────────────────

    [Fact(DisplayName = "RFC-9114-10.3-ie-007: Field value with NUL rejected")]
    public void Field_value_with_nul_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\x00injected"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("NUL", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-008: Field value with bare CR rejected")]
    public void Field_value_with_cr_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\rinjected"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("CR", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-009: Field value with bare LF rejected")]
    public void Field_value_with_lf_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\ninjected"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("LF", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-010: Field value with CRLF injection rejected")]
    public void Field_value_with_crlf_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\r\ninjected: evil"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.Validate(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-011: Normal field values accepted")]
    public void Normal_field_values_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html; charset=utf-8"),
            ("x-custom", "some value with spaces and tabs\t"),
            ("date", "Sat, 22 Mar 2026 00:00:00 GMT"),
        };

        Http3FieldValidator.Validate(headers);
    }

    // ───────────────────────── Prohibited Origins ─────────────────────────

    [Fact(DisplayName = "RFC-9114-10.3-ie-012: URI with userinfo rejected")]
    public void Uri_with_userinfo_rejected()
    {
        var uri = new Uri("https://user:password@example.com/path");

        var ex = Assert.Throws<Http3Exception>(
            () => Http3OriginValidator.Validate(uri));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-013: URI with user-only userinfo rejected")]
    public void Uri_with_user_only_rejected()
    {
        var uri = new Uri("https://user@example.com/path");

        var ex = Assert.Throws<Http3Exception>(
            () => Http3OriginValidator.Validate(uri));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-014: URI with fragment rejected")]
    public void Uri_with_fragment_rejected()
    {
        var uri = new Uri("https://example.com/path#fragment");

        var ex = Assert.Throws<Http3Exception>(
            () => Http3OriginValidator.Validate(uri));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("fragment", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-015: Normal HTTPS URI accepted")]
    public void Normal_https_uri_accepted()
    {
        var uri = new Uri("https://example.com/path?query=1");

        Http3OriginValidator.Validate(uri);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-016: CONNECT URI skips scheme and path validation")]
    public void Connect_uri_skips_scheme_path_validation()
    {
        var uri = new Uri("https://example.com:443/");

        Http3OriginValidator.Validate(uri, isConnect: true);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-017: CONNECT URI with userinfo still rejected")]
    public void Connect_uri_with_userinfo_rejected()
    {
        var uri = new Uri("https://user@example.com:443/");

        var ex = Assert.Throws<Http3Exception>(
            () => Http3OriginValidator.Validate(uri, isConnect: true));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    // ───────────────────────── Integration: Encoder Rejects Prohibited Origins ─────────────────────────

    [Fact(DisplayName = "RFC-9114-10.3-ie-018: Encoder rejects request with userinfo in URI")]
    public void Encoder_rejects_userinfo_uri()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@example.com/");

        var ex = Assert.Throws<Http3Exception>(() => encoder.Encode(request));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-019: Encoder rejects request with fragment in URI")]
    public void Encoder_rejects_fragment_uri()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");

        var ex = Assert.Throws<Http3Exception>(() => encoder.Encode(request));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("fragment", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-10.3-ie-020: Encoder accepts normal request")]
    public void Encoder_accepts_normal_request()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");

        var frames = encoder.Encode(request);
        Assert.NotEmpty(frames);
    }
}
