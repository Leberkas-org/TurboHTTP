using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

/// <summary>
/// RFC 9111 §5.2 — Cache-Control header parsing tests.
/// Covers all standard directives: max-age, s-maxage, no-cache, no-store,
/// must-revalidate, proxy-revalidate, public, private, and unknown directives.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheControlParser"/>.
/// RFC 9111 §5.2: Cache-Control directives control cacheability and freshness behaviour.
/// </remarks>
public sealed class CacheControlParserSpec
{
    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_return_null_when_input_is_null()
    {
        var result = CacheControlParser.Parse(null);
        Assert.Null(result);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_return_null_when_input_is_empty()
    {
        var result = CacheControlParser.Parse("");
        Assert.Null(result);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_return_null_when_input_is_whitespace()
    {
        var result = CacheControlParser.Parse("   ");
        Assert.Null(result);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_correctly_when_no_cache_directive()
    {
        var result = CacheControlParser.Parse("no-cache");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.Null(result.NoCacheFields);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_no_store_when_no_store_directive()
    {
        var result = CacheControlParser.Parse("no-store");
        Assert.NotNull(result);
        Assert.True(result.NoStore);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_max_age_when_max_age_3600()
    {
        var result = CacheControlParser.Parse("max-age=3600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(3600), result.MaxAge);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_s_max_age_when_s_max_age_600()
    {
        var result = CacheControlParser.Parse("s-maxage=600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(600), result.SMaxAge);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_max_stale_when_max_stale_300()
    {
        var result = CacheControlParser.Parse("max-stale=300");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(300), result.MaxStale);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_min_fresh_when_min_fresh_60()
    {
        var result = CacheControlParser.Parse("min-fresh=60");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(60), result.MinFresh);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_must_revalidate_when_must_revalidate_directive()
    {
        var result = CacheControlParser.Parse("must-revalidate");
        Assert.NotNull(result);
        Assert.True(result.MustRevalidate);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_public_when_public_directive()
    {
        var result = CacheControlParser.Parse("public");
        Assert.NotNull(result);
        Assert.True(result.Public);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_private_when_private_directive()
    {
        var result = CacheControlParser.Parse("private");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.Null(result.PrivateFields);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_immutable_when_immutable_directive()
    {
        var result = CacheControlParser.Parse("immutable");
        Assert.NotNull(result);
        Assert.True(result.Immutable);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_only_if_cached_when_only_if_cached_directive()
    {
        var result = CacheControlParser.Parse("only-if-cached");
        Assert.NotNull(result);
        Assert.True(result.OnlyIfCached);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_all_directives_when_multiple_directives_in_header()
    {
        var result = CacheControlParser.Parse("max-age=60, must-revalidate, public");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(60), result.MaxAge);
        Assert.True(result.MustRevalidate);
        Assert.True(result.Public);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_no_cache_with_field_list_when_no_cache_has_quoted_fields()
    {
        var result = CacheControlParser.Parse("no-cache=\"Authorization\"");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.NotNull(result.NoCacheFields);
        Assert.Contains("Authorization", result.NoCacheFields);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_ignore_unknown_directive_when_unknown_directive_present()
    {
        var result = CacheControlParser.Parse("stale-while-revalidate=60, max-age=30");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(30), result.MaxAge);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_max_age_when_max_age_is_uppercase()
    {
        var result = CacheControlParser.Parse("MAX-AGE=3600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(3600), result.MaxAge);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_parse_no_transform_when_no_transform_directive()
    {
        var result = CacheControlParser.Parse("no-transform");
        Assert.NotNull(result);
        Assert.True(result.NoTransform);
    }

    [Trait("RFC", "RFC9111-5.2")]
    [Fact]
    public void CacheControlParser_should_accept_any_stale_when_max_stale_has_no_value()
    {
        var result = CacheControlParser.Parse("max-stale");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.MaxValue, result.MaxStale);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheControlParser_should_parse_field_list_when_no_cache_qualified()
    {
        var result = CacheControlParser.Parse("no-cache=\"Set-Cookie\"");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.NotNull(result.NoCacheFields);
        Assert.Single(result.NoCacheFields);
        Assert.Equal("Set-Cookie", result.NoCacheFields[0]);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheControlParser_should_parse_multiple_fields_when_no_cache_qualified()
    {
        var result = CacheControlParser.Parse("no-cache=\"A, B\"");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.NotNull(result.NoCacheFields);
        Assert.Equal(2, result.NoCacheFields.Count);
        Assert.Equal("A", result.NoCacheFields[0]);
        Assert.Equal("B", result.NoCacheFields[1]);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheControlParser_should_set_flag_when_unqualified_no_cache()
    {
        var result = CacheControlParser.Parse("no-cache");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.Null(result.NoCacheFields);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheControlParser_should_treat_as_unqualified_when_empty_quotes()
    {
        var result = CacheControlParser.Parse("no-cache=\"\"");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.Null(result.NoCacheFields);
    }

    [Trait("RFC", "RFC9111-5.2.2.3")]
    [Fact]
    public void CacheControlParser_should_parse_field_list_and_other_directives_when_no_cache_with_fields_and_max_age()
    {
        var result = CacheControlParser.Parse("no-cache=\"Set-Cookie, Authorization\", max-age=300");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.NotNull(result.NoCacheFields);
        Assert.Equal(2, result.NoCacheFields.Count);
        Assert.Contains("Set-Cookie", result.NoCacheFields);
        Assert.Contains("Authorization", result.NoCacheFields);
        Assert.Equal(TimeSpan.FromSeconds(300), result.MaxAge);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheControlParser_should_parse_field_when_private_qualified()
    {
        var result = CacheControlParser.Parse("private=\"Authorization\"");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.NotNull(result.PrivateFields);
        Assert.Single(result.PrivateFields);
        Assert.Equal("Authorization", result.PrivateFields[0]);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheControlParser_should_set_flag_when_unqualified_private()
    {
        var result = CacheControlParser.Parse("private");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.Null(result.PrivateFields);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheControlParser_should_parse_multiple_fields_when_private_qualified()
    {
        var result = CacheControlParser.Parse("private=\"A, B\"");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.NotNull(result.PrivateFields);
        Assert.Equal(2, result.PrivateFields.Count);
        Assert.Equal("A", result.PrivateFields[0]);
        Assert.Equal("B", result.PrivateFields[1]);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheControlParser_should_treat_as_unqualified_when_private_empty_quotes()
    {
        var result = CacheControlParser.Parse("private=\"\"");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.Null(result.PrivateFields);
    }

    [Trait("RFC", "RFC9111-5.2.2.7")]
    [Fact]
    public void CacheControlParser_should_parse_field_list_and_other_directives_when_private_with_fields_and_max_age()
    {
        var result = CacheControlParser.Parse("private=\"Set-Cookie, Authorization\", max-age=300");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.NotNull(result.PrivateFields);
        Assert.Equal(2, result.PrivateFields.Count);
        Assert.Contains("Set-Cookie", result.PrivateFields);
        Assert.Contains("Authorization", result.PrivateFields);
        Assert.Equal(TimeSpan.FromSeconds(300), result.MaxAge);
    }
}
