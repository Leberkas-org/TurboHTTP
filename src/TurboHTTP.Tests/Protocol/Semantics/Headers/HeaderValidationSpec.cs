using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Headers;

public sealed class HeaderValidationSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("GET", true)]
    [InlineData("X-Foo", true)]
    [InlineData("Bad Name", false)]
    [InlineData("Bad:Name", false)]
    [InlineData("Bad\tName", false)]
    [InlineData("", false)]
    [Trait("RFC", "RFC9110-5.6.2")]
    public void IsToken_should_match_RFC_tchar_rules(string value, bool expected)
    {
        Assert.Equal(expected, HeaderValidation.IsToken(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    [Theory(Timeout = 5000)]
    [InlineData("text/html", true)]
    [InlineData("value with spaces", true)]
    [InlineData("value\twith\ttabs", true)]
    [InlineData("", true)]
    [InlineData("bad\rvalue", false)]
    [InlineData("bad\nvalue", false)]
    [InlineData("bad\0value", false)]
    [Trait("RFC", "RFC9110-5.5")]
    public void IsValidFieldValue_should_match_RFC_field_value_rules(string value, bool expected)
    {
        Assert.Equal(expected, HeaderValidation.IsValidFieldValue(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    [Theory(Timeout = 5000)]
    [InlineData("text/html", "text/html")]
    [InlineData("  text/html  ", "text/html")]
    [InlineData("\tvalue\t", "value")]
    [InlineData(" \t value \t ", "value")]
    [InlineData("", "")]
    [Trait("RFC", "RFC9110-5.5")]
    public void TrimOws_should_strip_leading_and_trailing_OWS(string raw, string expected)
    {
        Assert.Equal(expected, HeaderValidation.TrimOws(raw));
    }
}