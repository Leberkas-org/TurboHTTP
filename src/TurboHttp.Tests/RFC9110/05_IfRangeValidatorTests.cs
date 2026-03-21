using System;
using System.Net.Http;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests for RFC 9110 §13.1.5 — If-Range header validation.
/// Verifies that <see cref="IfRangeValidator"/> enforces correct usage of If-Range
/// with Range headers, strong entity-tags, and HTTP-dates.
/// </summary>
public sealed class IfRangeValidatorTests
{
    [Fact(DisplayName = "RFC9110-13.1.5-IR-001: If-Range without Range throws")]
    public void Should_Throw_When_IfRangeWithoutRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("If-Range", "\"etag-123\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("Range header", ex.Message);
    }

    [Fact(DisplayName = "RFC9110-13.1.5-IR-002: If-Range with weak ETag throws")]
    public void Should_Throw_When_IfRangeWithWeakETag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "W/\"weak-etag\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("weak entity-tag", ex.Message);
    }

    [Fact(DisplayName = "RFC9110-13.1.5-IR-003: If-Range with HTTP-date when ETag available throws")]
    public void Should_Throw_When_IfRangeDateAndETagAvailable()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "Sat, 29 Oct 1994 19:43:31 GMT");
        request.Headers.TryAddWithoutValidation("ETag", "\"etag-available\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("strong entity-tag", ex.Message);
    }

    [Fact(DisplayName = "RFC9110-13.1.5-IR-004: If-Range with strong ETag and Range passes")]
    public void Should_NotThrow_When_StrongETagAndRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "\"strong-etag\"");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "RFC9110-13.1.5-IR-005: If-Range with HTTP-date without ETag passes")]
    public void Should_NotThrow_When_DateAndNoETag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "Sat, 29 Oct 1994 19:43:31 GMT");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "RFC9110-13.1.5-IR-006: No If-Range passes")]
    public void Should_NotThrow_When_NoIfRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }
}
