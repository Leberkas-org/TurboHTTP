using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Tests.RFC9111;

/// <summary>
/// RFC 9111 §5.2 — Cache-Control header parsing tests.
/// Covers all standard directives: max-age, s-maxage, no-cache, no-store,
/// must-revalidate, proxy-revalidate, public, private, and unknown directives.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheControlParser"/>.
/// RFC 9111 §5.2: Cache-Control directives control cacheability and freshness behaviour.
/// </remarks>
public sealed class CacheControlParserTests
{
    [Fact(DisplayName = "RFC9111-5.2-CC-001: null input returns null")]
    public void Should_ReturnNull_When_InputIsNull()
    {
        var result = CacheControlParser.Parse(null);
        Assert.Null(result);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-002: empty string returns null")]
    public void Should_ReturnNull_When_InputIsEmpty()
    {
        var result = CacheControlParser.Parse("");
        Assert.Null(result);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-003: whitespace-only input returns null")]
    public void Should_ReturnNull_When_InputIsWhitespace()
    {
        var result = CacheControlParser.Parse("   ");
        Assert.Null(result);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-004: no-cache directive parsed correctly")]
    public void Should_ParseCorrectly_When_NoCacheDirective()
    {
        var result = CacheControlParser.Parse("no-cache");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.Null(result.NoCacheFields);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-005: no-store directive parsed")]
    public void Should_ParseNoStore_When_NoStoreDirective()
    {
        var result = CacheControlParser.Parse("no-store");
        Assert.NotNull(result);
        Assert.True(result.NoStore);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-006: max-age=3600 parsed as TimeSpan")]
    public void Should_ParseMaxAge_When_MaxAge3600()
    {
        var result = CacheControlParser.Parse("max-age=3600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(3600), result.MaxAge);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-007: s-maxage=600 parsed correctly")]
    public void Should_ParseSMaxAge_When_SMaxAge600()
    {
        var result = CacheControlParser.Parse("s-maxage=600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(600), result.SMaxAge);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-008: max-stale=300 parsed correctly")]
    public void Should_ParseMaxStale_When_MaxStale300()
    {
        var result = CacheControlParser.Parse("max-stale=300");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(300), result.MaxStale);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-009: min-fresh=60 parsed correctly")]
    public void Should_ParseMinFresh_When_MinFresh60()
    {
        var result = CacheControlParser.Parse("min-fresh=60");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(60), result.MinFresh);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-010: must-revalidate flag parsed")]
    public void Should_ParseMustRevalidate_When_MustRevalidateDirective()
    {
        var result = CacheControlParser.Parse("must-revalidate");
        Assert.NotNull(result);
        Assert.True(result.MustRevalidate);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-011: public directive parsed")]
    public void Should_ParsePublic_When_PublicDirective()
    {
        var result = CacheControlParser.Parse("public");
        Assert.NotNull(result);
        Assert.True(result.Public);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-012: private directive parsed")]
    public void Should_ParsePrivate_When_PrivateDirective()
    {
        var result = CacheControlParser.Parse("private");
        Assert.NotNull(result);
        Assert.True(result.Private);
        Assert.Null(result.PrivateFields);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-013: immutable flag parsed")]
    public void Should_ParseImmutable_When_ImmutableDirective()
    {
        var result = CacheControlParser.Parse("immutable");
        Assert.NotNull(result);
        Assert.True(result.Immutable);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-014: only-if-cached parsed")]
    public void Should_ParseOnlyIfCached_When_OnlyIfCachedDirective()
    {
        var result = CacheControlParser.Parse("only-if-cached");
        Assert.NotNull(result);
        Assert.True(result.OnlyIfCached);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-015: multiple directives parsed in one header")]
    public void Should_ParseAllDirectives_When_MultipleDirectivesInHeader()
    {
        var result = CacheControlParser.Parse("max-age=60, must-revalidate, public");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(60), result.MaxAge);
        Assert.True(result.MustRevalidate);
        Assert.True(result.Public);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-016: no-cache with field list parsed")]
    public void Should_ParseNoCacheWithFieldList_When_NoCacheHasQuotedFields()
    {
        var result = CacheControlParser.Parse("no-cache=\"Authorization\"");
        Assert.NotNull(result);
        Assert.True(result.NoCache);
        Assert.NotNull(result.NoCacheFields);
        Assert.Contains("Authorization", result.NoCacheFields);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-017: unknown directive silently ignored")]
    public void Should_IgnoreUnknownDirective_When_UnknownDirectivePresent()
    {
        var result = CacheControlParser.Parse("stale-while-revalidate=60, max-age=30");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(30), result.MaxAge);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-018: case-insensitive parsing MAX-AGE=3600")]
    public void Should_ParseMaxAge_When_MaxAgeIsUppercase()
    {
        var result = CacheControlParser.Parse("MAX-AGE=3600");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(3600), result.MaxAge);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-019: no-transform directive parsed")]
    public void Should_ParseNoTransform_When_NoTransformDirective()
    {
        var result = CacheControlParser.Parse("no-transform");
        Assert.NotNull(result);
        Assert.True(result.NoTransform);
    }

    [Fact(DisplayName = "RFC9111-5.2-CC-020: max-stale without value accepted (any staleness)")]
    public void Should_AcceptAnyStale_When_MaxStaleHasNoValue()
    {
        var result = CacheControlParser.Parse("max-stale");
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.MaxValue, result.MaxStale);
    }
}
