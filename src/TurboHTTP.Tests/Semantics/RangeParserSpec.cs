using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// Tests Content-Range header parsing per RFC 9110 §14.1.1.
/// Recipients MUST anticipate potentially large decimal numerals —
/// all byte positions use <c>long</c> to support ranges beyond 4 GB.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RangeParser"/>.
/// </remarks>
public sealed class RangeParserSpec
{
    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_SmallRange()
    {
        var result = RangeParser.Parse("bytes 0-499/1234");

        Assert.NotNull(result);
        Assert.Equal("bytes", result.Unit);
        Assert.Equal(0L, result.First);
        Assert.Equal(499L, result.Last);
        Assert.Equal(1234L, result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_LargeRange()
    {
        var result = RangeParser.Parse("bytes 0-5368709119/5368709120");

        Assert.NotNull(result);
        Assert.Equal("bytes", result.Unit);
        Assert.Equal(0L, result.First);
        Assert.Equal(5_368_709_119L, result.Last);
        Assert.Equal(5_368_709_120L, result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_ReturnNull_When_InvalidFormat()
    {
        Assert.Null(RangeParser.Parse(null));
        Assert.Null(RangeParser.Parse(""));
        Assert.Null(RangeParser.Parse("   "));
        Assert.Null(RangeParser.Parse("garbage"));
        Assert.Null(RangeParser.Parse("bytes"));
        Assert.Null(RangeParser.Parse("bytes abc-def/ghi"));
        Assert.Null(RangeParser.Parse("bytes 0-499"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_SuffixRange()
    {
        var result = RangeParser.Parse("bytes -500/1234");

        Assert.NotNull(result);
        Assert.Equal("bytes", result.Unit);
        Assert.Null(result.First);
        Assert.Equal(500L, result.Last);
        Assert.Equal(1234L, result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_UnknownLength()
    {
        var result = RangeParser.Parse("bytes 0-499/*");

        Assert.NotNull(result);
        Assert.Equal("bytes", result.Unit);
        Assert.Equal(0L, result.First);
        Assert.Equal(499L, result.Last);
        Assert.Null(result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_UnsatisfiedRange()
    {
        var result = RangeParser.Parse("bytes */1234");

        Assert.NotNull(result);
        Assert.Equal("bytes", result.Unit);
        Assert.Null(result.First);
        Assert.Null(result.Last);
        Assert.Equal(1234L, result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_NearMaxLong()
    {
        var result = RangeParser.Parse("bytes 0-9223372036854775806/9223372036854775807");

        Assert.NotNull(result);
        Assert.Equal(0L, result.First);
        Assert.Equal(9_223_372_036_854_775_806L, result.Last);
        Assert.Equal(long.MaxValue, result.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Should_Parse_When_CustomUnit()
    {
        var result = RangeParser.Parse("items 0-9/100");

        Assert.NotNull(result);
        Assert.Equal("items", result.Unit);
        Assert.Equal(0L, result.First);
        Assert.Equal(9L, result.Last);
        Assert.Equal(100L, result.Length);
    }
}
