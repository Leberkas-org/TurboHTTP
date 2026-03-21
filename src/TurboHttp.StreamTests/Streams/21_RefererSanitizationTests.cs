using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests Referer header sanitization in <see cref="RequestEnricherStage"/> per RFC 9110 §10.5:
/// - Fragment MUST NOT be included
/// - Userinfo MUST NOT be included
/// - Referer MUST NOT be sent in unsecured HTTP if referring page was from secure protocol
/// </summary>
public sealed class RefererSanitizationTests : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunAsync(
        RequestEnricherStage stage,
        params HttpRequestMessage[] requests)
    {
        return Source.From(requests)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);
    }

    private static (HttpRequestMessage Holder, HttpRequestHeaders Headers) CreateDefaultHeaders()
    {
        var holder = new HttpRequestMessage();
        return (holder, holder.Headers);
    }

    private RequestEnricherStage CreateStage()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        return new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-001: Fragment stripped from Referer")]
    public async Task Should_StripFragment_When_RefererHasFragment()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://origin.test/page#section");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Referer"));
        var referer = result.Headers.GetValues("Referer").Single();
        Assert.DoesNotContain("#", referer);
        Assert.Equal("http://origin.test/page", referer);
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-002: Userinfo stripped from Referer")]
    public async Task Should_StripUserinfo_When_RefererHasUserinfo()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://user:pass@origin.test/page");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Referer"));
        var referer = result.Headers.GetValues("Referer").Single();
        Assert.DoesNotContain("user:pass", referer);
        Assert.DoesNotContain("@", referer);
        Assert.Equal("http://origin.test/page", referer);
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-003: Referer removed on HTTPS to HTTP downgrade")]
    public async Task Should_RemoveReferer_When_HttpsToHttp()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "https://secure.test/secret");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Referer"));
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-004: Referer preserved on same-scheme")]
    public async Task Should_PreserveReferer_When_SameScheme()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "https://origin.test/other");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Referer"));
        Assert.Equal("https://origin.test/other", result.Headers.GetValues("Referer").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-005: Referer preserved when no downgrade")]
    public async Task Should_PreserveReferer_When_HttpToHttp()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");
        request.Headers.TryAddWithoutValidation("Referer", "http://origin.test/other");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Referer"));
        Assert.Equal("http://origin.test/other", result.Headers.GetValues("Referer").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "REF-006: No Referer passes through unchanged")]
    public async Task Should_NotAdd_When_NoRefererPresent()
    {
        var stage = CreateStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://target.test/page");

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Referer"));
    }
}
