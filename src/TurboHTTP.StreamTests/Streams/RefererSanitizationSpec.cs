using System.Net;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Tests Referer header sanitization in <see cref="RequestEnricher"/> per RFC 9110 §10.5:
/// - Fragment MUST NOT be included
/// - Userinfo MUST NOT be included
/// - Referer MUST NOT be sent in unsecured HTTP if referring page was from secure protocol
/// </summary>
public sealed class RefererSanitizationSpec
{
    private static RequestEnricher CreateEnricher()
    {
        var holder = new HttpRequestMessage();
        return new RequestEnricher(() => new TurboRequestOptions(
            null,
            holder.Headers,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_strip_fragment_when_referer_has_fragment()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://origin.test/page#section");

        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("Referer"));
        var referer = result.Headers.GetValues("Referer").Single();
        Assert.DoesNotContain("#", referer);
        Assert.Equal("http://origin.test/page", referer);
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_strip_userinfo_when_referer_has_userinfo()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://user:pass@origin.test/page");

        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("Referer"));
        var referer = result.Headers.GetValues("Referer").Single();
        Assert.DoesNotContain("user:pass", referer);
        Assert.DoesNotContain("@", referer);
        Assert.Equal("http://origin.test/page", referer);
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_remove_referer_when_https_to_http()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "https://secure.test/secret");

        var result = enricher.Enrich(request);

        Assert.False(result.Headers.Contains("Referer"));
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_preserve_referer_when_same_scheme()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "https://origin.test/other");

        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("Referer"));
        Assert.Equal("https://origin.test/other", result.Headers.GetValues("Referer").Single());
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_preserve_referer_when_http_to_http()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://origin.test/other");

        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("Referer"));
        Assert.Equal("http://origin.test/other", result.Headers.GetValues("Referer").Single());
    }

    [Fact(Timeout = 5_000)]
    public void RefererSanitization_should_not_add_referer_when_no_referer_present()
    {
        var enricher = CreateEnricher();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");

        var result = enricher.Enrich(request);

        Assert.False(result.Headers.Contains("Referer"));
    }
}
