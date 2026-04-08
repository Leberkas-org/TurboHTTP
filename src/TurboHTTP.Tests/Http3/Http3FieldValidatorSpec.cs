using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3;

/// <summary>
/// Direct tests on <see cref="Http3FieldValidator"/> static validation methods.
/// Covers field name/value validation (RFC 9114 §4.2, §10.3), connection-specific
/// header rejection (§4.2), TE header rules, and response pseudo-header validation (§4.3.2).
/// </summary>
public sealed class Http3FieldValidatorSpec
{
    // ── Validate() — field name casing ──────────────────────────────────

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_accept_lowercase_field_names()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
            ("x-custom", "value"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_uppercase_field_name()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_mixed_case_field_name()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_all_uppercase_field_name()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_skip_pseudo_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    [InlineData("accept")]
    [InlineData("content-length")]
    [InlineData("x-request-id")]
    [InlineData("cache-control")]
    [InlineData("user-agent")]
    public void Validate_should_accept_various_lowercase_names(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        Http3FieldValidator.Validate(headers);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    [InlineData("Accept")]
    [InlineData("Content-Length")]
    [InlineData("X-Request-ID")]
    [InlineData("Cache-Control")]
    [InlineData("User-Agent")]
    public void Validate_should_reject_various_uppercase_names(string name)
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

    // ── Validate() — connection-specific headers ────────────────────────

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_connection_header()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_transfer_encoding_header()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_upgrade_header()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_proxy_connection_header()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_keep_alive_header()
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

    // ── Validate() — TE header rules ────────────────────────────────────

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_accept_te_header_with_trailers()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_te_header_with_gzip()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_te_header_with_chunked()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_te_header_with_trailers_and_gzip()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Validate_should_reject_te_header_with_empty_value()
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

    // ── ValidateFieldName() — internal method ───────────────────────────

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void ValidateFieldName_should_accept_numbers_and_hyphens()
    {
        Http3FieldValidator.ValidateFieldName("x-request-123");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void ValidateFieldName_should_report_position_of_invalid_character()
    {
        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateFieldName("content-Type"));
        Assert.Contains("position 8", ex.Message);
    }

    // ── ValidateConnectionSpecific() — internal method ──────────────────

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    [InlineData("connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [InlineData("proxy-connection")]
    [InlineData("keep-alive")]
    public void ValidateConnectionSpecific_should_reject_all_forbidden_headers(string name)
    {
        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateConnectionSpecific(name, "value"));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void ValidateConnectionSpecific_should_accept_regular_header()
    {
        Http3FieldValidator.ValidateConnectionSpecific("content-type", "text/html");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void ValidateConnectionSpecific_should_accept_te_trailers_case_insensitive()
    {
        Http3FieldValidator.ValidateConnectionSpecific("te", "Trailers");
    }

    // ── ValidateResponsePseudoHeaders() ─────────────────────────────────

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_accept_valid_status()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        Http3FieldValidator.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_accept_valid_response_with_regular_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("server", "test"),
        };

        Http3FieldValidator.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_reject_duplicate_status()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":status", "301"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_reject_unknown_pseudo_header_method()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":method", "GET"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_reject_unknown_pseudo_header_path()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":path", "/"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_reject_unknown_custom_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":custom", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void ValidateResponsePseudoHeaders_should_reject_pseudo_after_regular_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
            (":status", "200"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }
}
