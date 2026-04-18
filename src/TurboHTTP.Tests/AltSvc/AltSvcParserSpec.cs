using TurboHTTP.Protocol.AltSvc;

namespace TurboHTTP.Tests.AltSvc;

public sealed class AltSvcParserSpec
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_return_h3_entry_for_standard_alt_svc_header()
    {
        var entries = AltSvcParser.Parse("h3=\":443\"", out var isClear, FixedNow);

        Assert.False(isClear);
        var entry = Assert.Single(entries);
        Assert.Equal("h3", entry.Protocol);
        Assert.Equal(string.Empty, entry.Host);
        Assert.Equal(443, entry.Port);
        Assert.Equal(86400, entry.MaxAge); // RFC default
        Assert.False(entry.Persist);
        Assert.True(entry.IsHttp3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_custom_max_age()
    {
        var entries = AltSvcParser.Parse("h3=\":443\"; ma=3600", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.Equal(3600, entry.MaxAge);
        Assert.Equal(FixedNow.AddSeconds(3600), entry.ExpiresAt);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_persist_flag()
    {
        var entries = AltSvcParser.Parse("h3=\":443\"; persist=1", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.True(entry.Persist);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_persist_flag_set_to_zero()
    {
        var entries = AltSvcParser.Parse("h3=\":443\"; persist=0", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.False(entry.Persist);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_multiple_alternatives()
    {
        var entries = AltSvcParser.Parse("h3=\":443\", h2=\":443\"", out var isClear, FixedNow);

        Assert.False(isClear);
        Assert.Equal(2, entries.Count);
        Assert.Equal("h3", entries[0].Protocol);
        Assert.Equal("h2", entries[1].Protocol);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_alternative_host()
    {
        var entries = AltSvcParser.Parse("h3=\"alt.example.com:8443\"", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.Equal("alt.example.com", entry.Host);
        Assert.Equal(8443, entry.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_return_clear()
    {
        var entries = AltSvcParser.Parse("clear", out var isClear, FixedNow);

        Assert.True(isClear);
        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_return_clear_case_insensitive()
    {
        var entries = AltSvcParser.Parse("CLEAR", out var isClear, FixedNow);

        Assert.True(isClear);
        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_return_empty_for_null_input()
    {
        var entries = AltSvcParser.Parse(null!, out var isClear, FixedNow);

        Assert.False(isClear);
        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_return_empty_for_empty_string()
    {
        var entries = AltSvcParser.Parse("", out var isClear, FixedNow);

        Assert.False(isClear);
        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_skip_malformed_alternatives()
    {
        var entries = AltSvcParser.Parse("h3=\":443\", badformat, h2=\":443\"", out _, FixedNow);

        Assert.Equal(2, entries.Count);
        Assert.Equal("h3", entries[0].Protocol);
        Assert.Equal("h2", entries[1].Protocol);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_draft_h3_protocol()
    {
        var entries = AltSvcParser.Parse("h3-29=\":443\"; ma=3600; persist=1", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.Equal("h3-29", entry.Protocol);
        Assert.Equal(3600, entry.MaxAge);
        Assert.True(entry.Persist);
        Assert.False(entry.IsHttp3); // h3-29 is not "h3"
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_combined_parameters()
    {
        var entries = AltSvcParser.Parse("h3=\":443\"; ma=7200; persist=1", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.Equal(7200, entry.MaxAge);
        Assert.True(entry.Persist);
        Assert.Equal(FixedNow.AddSeconds(7200), entry.ExpiresAt);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void Parse_should_handle_unquoted_authority()
    {
        // While RFC 7838 uses quotes, be lenient with unquoted values.
        var entries = AltSvcParser.Parse("h3=:443", out _, FixedNow);

        var entry = Assert.Single(entries);
        Assert.Equal(443, entry.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void AltSvcEntry_should_report_valid_when_not_expired()
    {
        var entry = new AltSvcEntry("h3", "", 443, 3600, false, FixedNow.AddSeconds(3600));

        Assert.True(entry.IsValid(FixedNow));
        Assert.True(entry.IsValid(FixedNow.AddSeconds(3599)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7838-3")]
    public void AltSvcEntry_should_report_invalid_when_expired()
    {
        var entry = new AltSvcEntry("h3", "", 443, 3600, false, FixedNow.AddSeconds(3600));

        Assert.False(entry.IsValid(FixedNow.AddSeconds(3601)));
    }
}
