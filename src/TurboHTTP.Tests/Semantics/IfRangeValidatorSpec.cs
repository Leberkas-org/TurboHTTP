using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

public sealed class IfRangeValidatorSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_Throw_When_IfRangeWithoutRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("If-Range", "\"etag-123\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("Range header", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_Throw_When_IfRangeWithWeakETag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "W/\"weak-etag\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("weak entity-tag", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_Throw_When_IfRangeDateAndETagAvailable()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "Sat, 29 Oct 1994 19:43:31 GMT");
        request.Headers.TryAddWithoutValidation("ETag", "\"etag-available\"");

        var ex = Assert.Throws<InvalidOperationException>(() => IfRangeValidator.Validate(request));
        Assert.Contains("strong entity-tag", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_NotThrow_When_StrongETagAndRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "\"strong-etag\"");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_NotThrow_When_DateAndNoETag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");
        request.Headers.TryAddWithoutValidation("If-Range", "Sat, 29 Oct 1994 19:43:31 GMT");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void Should_NotThrow_When_NoIfRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/file");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-499");

        var exception = Record.Exception(() => IfRangeValidator.Validate(request));
        Assert.Null(exception);
    }
}