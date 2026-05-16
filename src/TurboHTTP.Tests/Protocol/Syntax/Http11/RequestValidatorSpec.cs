using System.Net.Http.Headers;
using TurboHTTP.Protocol.Syntax.Http11.Client;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

[Trait("RFC", "RFC9112")]
public sealed class RequestValidatorSpec
{
    [Fact(Timeout = 5000)]
    public void ValidGet_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        request.Headers.Add("User-Agent", "Test");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void ValidPostWithBody_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com")
        {
            Content = new StringContent("test body")
        };
        request.Headers.Add("User-Agent", "Test");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void LowercaseMethod_ShouldThrow()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "http://example.com");

        var exception = Assert.Throws<ArgumentException>(() => RequestValidator.Validate(request));
        Assert.Contains("uppercase", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void ValidRangeHeader_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        request.Headers.Add("Range", "bytes=0-99");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void ValidMultipleRanges_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        request.Headers.Add("Range", "bytes=0-100,200-300");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void ValidSuffixRange_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        request.Headers.Add("Range", "bytes=-100");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void ValidPostWithContentHeaders_ShouldPass()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com")
        {
            Content = new StringContent("test body")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        RequestValidator.Validate(request);
    }

    [Fact(Timeout = 5000)]
    public void ValidMixedCaseMethod_ShouldThrow()
    {
        var request = new HttpRequestMessage(new HttpMethod("Get"), "http://example.com");

        var exception = Assert.Throws<ArgumentException>(() => RequestValidator.Validate(request));
        Assert.Contains("uppercase", exception.Message);
    }
}