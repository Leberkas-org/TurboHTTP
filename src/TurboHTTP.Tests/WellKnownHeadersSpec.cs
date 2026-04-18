using TurboHTTP.Protocol;

namespace TurboHTTP.Tests;

public sealed class WellKnownHeadersSpec
{
    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_return_interned_string_for_known_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Host"u8);
        Assert.Equal("Host", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_allocate_string_for_unknown_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("X-Custom-Header"u8);
        Assert.Equal("X-Custom-Header", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_2_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("TE"u8);
        Assert.Equal("TE", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_3_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Age"u8);
        Assert.Equal("Age", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_4_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Date"u8);
        Assert.Equal("Date", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_10_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Connection"u8);
        Assert.Equal("Connection", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_13_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Authorization"u8);
        Assert.Equal("Authorization", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_25_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Strict-Transport-Security"u8);
        Assert.Equal("Strict-Transport-Security", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_return_interned_value_for_known_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("gzip"u8);
        Assert.Equal("gzip", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_allocate_string_for_unknown_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("x-custom-encoding"u8);
        Assert.Equal("x-custom-encoding", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_1_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("0"u8);
        Assert.Equal("0", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_2_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("br"u8);
        Assert.Equal("br", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_10_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("keep-alive"u8);
        Assert.Equal("keep-alive", result);
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_true_for_identical_case_insensitive_ascii()
    {
        var a = "Content-Type"u8;
        var b = "content-type"u8;
        Assert.True(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_false_for_different_lengths()
    {
        var a = "Host"u8;
        var b = "Content-Type"u8;
        Assert.False(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_false_for_different_content()
    {
        var a = "Host"u8;
        var b = "Date"u8;
        Assert.False(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_true_for_exact_match()
    {
        var a = "Host"u8;
        var b = "Host"u8;
        Assert.True(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_true_when_chunked_present()
    {
        var value = "chunked"u8;
        Assert.True(WellKnownHeaders.ContainsChunked(value));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_true_when_chunked_case_insensitive()
    {
        var value = "CHUNKED"u8;
        Assert.True(WellKnownHeaders.ContainsChunked(value));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_true_when_chunked_in_list()
    {
        var value = "deflate, chunked"u8;
        Assert.True(WellKnownHeaders.ContainsChunked(value));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_false_when_chunked_not_present()
    {
        var value = "gzip"u8;
        Assert.False(WellKnownHeaders.ContainsChunked(value));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_false_when_value_too_short()
    {
        var value = "ch"u8;
        Assert.False(WellKnownHeaders.ContainsChunked(value));
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_remove_leading_spaces()
    {
        var value = "  Host"u8;
        var result = WellKnownHeaders.TrimOws(value);
        Assert.Equal("Host"u8.ToArray(), result.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_remove_trailing_spaces()
    {
        var value = "Host  "u8;
        var result = WellKnownHeaders.TrimOws(value);
        Assert.Equal("Host"u8.ToArray(), result.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_remove_both_leading_and_trailing_spaces()
    {
        var value = "  Host  "u8;
        var result = WellKnownHeaders.TrimOws(value);
        Assert.Equal("Host"u8.ToArray(), result.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_remove_tabs()
    {
        var value = "\tHost\t"u8;
        var result = WellKnownHeaders.TrimOws(value);
        Assert.Equal("Host"u8.ToArray(), result.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_return_empty_when_only_whitespace()
    {
        var value = "   "u8;
        var result = WellKnownHeaders.TrimOws(value);
        Assert.Empty(result.ToArray());
    }
}