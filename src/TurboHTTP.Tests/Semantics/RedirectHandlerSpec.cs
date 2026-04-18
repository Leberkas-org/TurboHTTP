using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

public sealed class RedirectHandlerSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void Should_ReturnTrue_When_StatusCodeIsRedirect(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.True(RedirectHandler.IsRedirect(response));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void Should_ReturnFalse_When_StatusCodeIsNotRedirect(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.False(RedirectHandler.IsRedirect(response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_RewritePostToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = new StringContent("body data")
        };
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new-location");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_RewritePutToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = new StringContent("body data")
        };
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_RewriteDeleteToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Should_PreservePostMethodAndBody_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("request body");
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Post, redirected.Method);
        Assert.NotNull(redirected.Content);
        var expectedBytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var actualBytes = await redirected.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
        Assert.Equal("text/plain", redirected.Content.Headers.ContentType!.MediaType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Should_PreservePutMethodAndBody_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("request body");
        var original = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Put, redirected.Method);
        Assert.NotNull(redirected.Content);
        var expectedBytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var actualBytes = await redirected.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
        Assert.Equal("text/plain", redirected.Content.Headers.ContentType!.MediaType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveDeleteMethod_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Delete, redirected.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Should_PreservePostMethodAndBody_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("body");
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Post, redirected.Method);
        Assert.NotNull(redirected.Content);
        var expectedBytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var actualBytes = await redirected.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
        Assert.Equal("text/plain", redirected.Content.Headers.ContentType!.MediaType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Should_PreservePatchMethodAndBody_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("patch body");
        var original = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Patch, redirected.Method);
        Assert.NotNull(redirected.Content);
        var expectedBytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var actualBytes = await redirected.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
        Assert.Equal("text/plain", redirected.Content.Headers.ContentType!.MediaType);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_RewritePostToGet_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = new StringContent("body")
        };
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_PreserveGetMethod_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_PreserveHeadMethod_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Head, redirected.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_UseAbsoluteLocation_When_LocationIsAbsolute()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://other.com/new-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://other.com/new-page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ResolveRelativeLocation_When_LocationIsRelative()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/v1/resource");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "/api/v2/resource");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/api/v2/resource", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ResolveRelativePath_When_LocationIsRelativePath()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dir/page");
        var response = BuildRedirect(HttpStatusCode.Found, "other-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected.RequestUri);
        Assert.Equal("example.com", redirected.RequestUri.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_When_MaxRedirectsExceeded()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 3 });

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/page{i}");
            var res = BuildRedirect(HttpStatusCode.MovedPermanently, $"http://example.com/page{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        // 4th redirect should throw
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page3");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/page4");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_When_DefaultMaxRedirectsExceeded()
    {
        var handler = new RedirectHandler(); // default: 10

        for (var i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/page{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/page{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page10");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page11");

        Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_TrackRedirectCount_When_RedirectsFollow()
    {
        var handler = new RedirectHandler();
        Assert.Equal(0, handler.RedirectCount);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);
        Assert.Equal(1, handler.RedirectCount);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/c");
        handler.BuildRedirectRequest(req2, res2);
        Assert.Equal(2, handler.RedirectCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_When_DirectRedirectLoop()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Now try to redirect back to /a — loop detected
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_When_SelfRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_When_LocationHeaderMissing()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.MissingLocationHeader, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveGetMethod_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}