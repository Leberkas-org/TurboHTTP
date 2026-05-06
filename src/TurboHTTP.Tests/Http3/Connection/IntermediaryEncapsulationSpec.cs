using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class IntermediaryEncapsulationSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    [InlineData("invalid name")]
    [InlineData("content type")]
    public void Field_name_with_space_rejected(string name)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (name, "value"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
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

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
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

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Empty_field_name_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
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

        FieldValidator.Validate(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Field_name_with_high_byte_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("field\x80name", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("§10.3", ex.Message);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Field_value_with_nul_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\x00injected"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("NUL", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Field_value_with_cr_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\rinjected"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("CR", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Field_value_with_lf_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\ninjected"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("LF", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Field_value_with_crlf_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("x-custom", "value\r\ninjected: evil"),
        };

        var ex = Assert.Throws<Http3Exception>(() => FieldValidator.Validate(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Normal_field_values_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html; charset=utf-8"),
            ("x-custom", "some value with spaces and tabs\t"),
            ("date", "Sat, 22 Mar 2026 00:00:00 GMT"),
        };

        FieldValidator.Validate(headers);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Uri_with_userinfo_rejected()
    {
        var uri = new Uri("https://user:password@example.com/path");

        var ex = Assert.Throws<Http3Exception>(() => OriginValidator.Validate(uri));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Uri_with_user_only_rejected()
    {
        var uri = new Uri("https://user@example.com/path");

        var ex = Assert.Throws<Http3Exception>(() => OriginValidator.Validate(uri));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Uri_with_fragment_rejected()
    {
        var uri = new Uri("https://example.com/path#fragment");

        var ex = Assert.Throws<Http3Exception>(() => OriginValidator.Validate(uri));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("fragment", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Normal_https_uri_accepted()
    {
        var uri = new Uri("https://example.com/path?query=1");

        OriginValidator.Validate(uri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Connect_uri_skips_scheme_path_validation()
    {
        var uri = new Uri("https://example.com:443/");

        OriginValidator.Validate(uri, isConnect: true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Connect_uri_with_userinfo_rejected()
    {
        var uri = new Uri("https://user@example.com:443/");

        var ex = Assert.Throws<Http3Exception>(() => OriginValidator.Validate(uri, isConnect: true));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Encoder_rejects_userinfo_uri()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@example.com/");

        var ex = Assert.Throws<Http3Exception>(() => encoder.Encode(request));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("userinfo", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Encoder_rejects_fragment_uri()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");

        var ex = Assert.Throws<Http3Exception>(() => encoder.Encode(request));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("fragment", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Encoder_accepts_normal_request()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");

        var frames = encoder.Encode(request);
        Assert.NotEmpty(frames);
    }
}