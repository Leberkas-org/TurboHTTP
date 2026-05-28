using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Security;

public sealed class Http3FieldValidationFuzzSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_uppercase_field_name()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("Content-Type", "text/html"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        Assert.Contains("uppercase", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_fully_uppercase_field_name()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("CONTENT-TYPE", "text/html"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_accept_all_lowercase_field_names()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
            ("accept-encoding", "gzip"),
            ("x-custom-header", "value"),
        };

        // Should not throw
        FieldValidator.Validate(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_cr_in_field_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("x-inject", "value\rinjection"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        Assert.Contains("CR", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_lf_in_field_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("x-inject", "value\ninjection"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        Assert.Contains("LF", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_nul_in_field_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("x-inject", "value\0injection"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        Assert.Contains("NUL", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_crlf_sequence_in_field_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("x-inject", "value\r\ninjected-header: evil"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_non_token_characters_in_field_name()
    {
        string[] invalidNames = ["field name", "field{name}", "field[0]", "field@host", "field,name"];

        foreach (var name in invalidNames)
        {
            var headers = new List<(string Name, string Value)> { (name, "value") };

            Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_empty_field_name()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("", "value"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_connection_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("connection", "keep-alive"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        Assert.Contains("Connection", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_transfer_encoding_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("transfer-encoding", "chunked"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_upgrade_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("upgrade", "websocket"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_keep_alive_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("keep-alive", "timeout=5"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_proxy_connection_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("proxy-connection", "keep-alive"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_reject_te_header_with_non_trailers_value()
    {
        string[] badValues = ["gzip", "chunked", "", "trailers, gzip"];

        foreach (var badValue in badValues)
        {
            var headers = new List<(string Name, string Value)> { ("te", badValue) };

            Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_accept_te_header_with_trailers_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("te", "trailers"),
        };

        // Should not throw
        FieldValidator.Validate(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void FieldValidator_should_reject_duplicate_status_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":status", "304"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.ValidateResponsePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void FieldValidator_should_reject_unknown_response_pseudo_headers()
    {
        string[] badPseudos = [":method", ":path", ":scheme", ":authority", ":custom"];

        foreach (var pseudo in badPseudos)
        {
            var headers = new List<(string Name, string Value)>
            {
                (":status", "200"),
                (pseudo, "value"),
            };

            Assert.Throws<HttpProtocolException>(() => FieldValidator.ValidateResponsePseudoHeaders(headers));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void FieldValidator_should_reject_pseudo_header_after_regular_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
            (":status", "304"), // pseudo after regular — forbidden
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.ValidateResponsePseudoHeaders(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void FieldValidator_should_accept_valid_response_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
            ("content-length", "42"),
        };

        // Should not throw
        FieldValidator.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_reject_high_ascii_in_field_name()
    {
        // Characters >= 128 are not valid token characters
        var headers = new List<(string Name, string Value)>
        {
            ("caf\u00E9", "value"),
        };

        Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void FieldValidator_should_skip_pseudo_headers_during_regular_validation()
    {
        // Pseudo-headers are validated separately; Validate() should skip them
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        // Should not throw (pseudo-header ":status" is skipped by Validate)
        FieldValidator.Validate(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void FieldValidator_should_report_exact_position_of_invalid_character()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("valid-prefix-Then-bad", "value"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => FieldValidator.Validate(headers));
        // "T" is at index 13
        Assert.Contains("position 13", ex.Message);
    }
}