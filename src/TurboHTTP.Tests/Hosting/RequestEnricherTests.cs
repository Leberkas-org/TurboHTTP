using System.Net;
using System.Net.Http.Headers;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.Tests.Hosting;

/// <summary>
/// Tests the <see cref="RequestEnricher"/> that applies base address, default version, and default headers.
/// Verifies RFC 9110-compliant header merging and URI resolution behavior.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RequestEnricher"/>.
/// Validates base-address combination, version override, and header merge/skip-if-present semantics.
/// </remarks>
public sealed class RequestEnricherTests
{
    private static RequestEnricher CreateEnricher(
        Uri? baseAddress = null,
        HttpRequestHeaders? defaultHeaders = null,
        Version? defaultVersion = null)
    {
        var holder = new HttpRequestMessage();
        var headers = defaultHeaders ?? holder.Headers;

        return new RequestEnricher(() => new TurboRequestOptions(
            baseAddress,
            headers,
            defaultVersion ?? HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
    }

    private static (HttpRequestMessage Holder, HttpRequestHeaders Headers) CreateDefaultHeaders()
    {
        var holder = new HttpRequestMessage();
        return (holder, holder.Headers);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-001: Null URI + BaseAddress → RequestUri becomes BaseAddress root")]
    public void Should_SetRequestUriToBaseAddress_When_RequestUriIsNull()
    {
        var baseAddress = new Uri("http://a.test/");
        var enricher = CreateEnricher(baseAddress: baseAddress);

        var request = new HttpRequestMessage { Method = HttpMethod.Get };
        var result = enricher.Enrich(request);

        Assert.Equal(baseAddress, result.RequestUri);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-002: Relative URI \"/ping\" + BaseAddress \"http://a.test\" → \"http://a.test/ping\"")]
    public void Should_CombineRelativeUriWithBaseAddress_When_BaseAddressSet()
    {
        var baseAddress = new Uri("http://a.test/");
        var enricher = CreateEnricher(baseAddress: baseAddress);

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));
        var result = enricher.Enrich(request);

        Assert.Equal(new Uri("http://a.test/ping"), result.RequestUri);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-003: Absolute URI → RequestUri unchanged even when BaseAddress is set")]
    public void Should_NotChangeAbsoluteUri_When_BaseAddressIsSet()
    {
        var absoluteUri = new Uri("http://other.host/path");
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        var result = enricher.Enrich(request);

        Assert.Equal(absoluteUri, result.RequestUri);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-004: Null URI + null BaseAddress → throws InvalidOperationException")]
    public void Should_Throw_When_UriAndBaseAddressAreNull()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-005: Relative URI + null BaseAddress → throws InvalidOperationException")]
    public void Should_Throw_When_RelativeUriAndBaseAddressIsNull()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-006: request.Version == 1.1 (default), defaultVersion == 2.0 → version becomes 2.0")]
    public void Should_SetVersionTo20_When_RequestVersionIs11AndDefaultIs20()
    {
        var enricher = CreateEnricher(defaultVersion: HttpVersion.Version20);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-007: request.Version == 1.1 (default), defaultVersion == 1.1 → version unchanged")]
    public void Should_NotChangeVersion_When_RequestVersionIs11AndDefaultIs11()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version11, result.Version);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-008: request.Version explicitly set to 1.0 → unchanged regardless of defaultVersion")]
    public void Should_NotOverrideVersion_When_ExplicitV10Set()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version10
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version10, result.Version);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-009: request.Version explicitly set to 2.0 → unchanged regardless of defaultVersion")]
    public void Should_NotOverrideVersion_When_ExplicitV20Set()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version20
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-010: DefaultRequestHeaders has X-Foo:bar → merged into request")]
    public void Should_MergeDefaultHeader_When_DefaultRequestHeadersContainsXFoo()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "bar");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-Foo"));
        Assert.Contains("bar", result.Headers.GetValues("X-Foo"));
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-011: Request already has X-Foo:existing → not overridden; existing value kept")]
    public void Should_PreserveExistingHeader_When_DefaultAndRequestHaveSameHeader()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "default");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");
        var result = enricher.Enrich(request);

        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-012: DefaultRequestHeaders has two headers → both merged")]
    public void Should_MergeBothHeaders_When_TwoDefaultHeadersPresent()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-One", "1");
        defaultHeaders.TryAddWithoutValidation("X-Two", "2");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-One"));
        Assert.True(result.Headers.Contains("X-Two"));
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-013: DefaultRequestHeaders empty → no headers added; request unchanged")]
    public void Should_AddNoHeaders_When_DefaultRequestHeadersEmpty()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        // RFC 9110 §6.6.1 — clients SHOULD NOT send Date; no headers auto-added.
        Assert.Empty(result.Headers);
    }

    [Fact(Timeout = 5_000,
        DisplayName =
            "ENR-014: Same header name, different casing in request vs defaults → treated as same; not doubled")]
    public void Should_NotDoubleHeader_When_HeaderNameDiffersOnlyInCase()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("x-foo", "default");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");
        var result = enricher.Enrich(request);

        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 5_000,
        DisplayName =
            "ENR-015: DefaultRequestHeaders has multiple values for one name → all values added as one entry")]
    public void Should_AddAllValues_When_DefaultHeaderHasMultipleValues()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Multi", ["a", "b"]);

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-Multi"));
        var values = new List<string>(result.Headers.GetValues("X-Multi"));
        Assert.Contains("a", values);
        Assert.Contains("b", values);
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-016: 3 requests in sequence → all 3 enriched independently, order preserved")]
    public void Should_EnrichAllRequestsInOrder_When_ThreeRequestsInSequence()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Default", "yes");

        var enricher = CreateEnricher(baseAddress: baseAddress, defaultHeaders: defaultHeaders);

        var req1 = new HttpRequestMessage(HttpMethod.Get, new Uri("/one", UriKind.Relative));
        var req2 = new HttpRequestMessage(HttpMethod.Get, new Uri("/two", UriKind.Relative));
        var req3 = new HttpRequestMessage(HttpMethod.Get, new Uri("/three", UriKind.Relative));

        var results = new List<HttpRequestMessage>
        {
            enricher.Enrich(req1),
            enricher.Enrich(req2),
            enricher.Enrich(req3)
        };

        Assert.Equal(3, results.Count);

        Assert.Equal(new Uri("http://a.test/one"), results[0].RequestUri);
        Assert.Equal(new Uri("http://a.test/two"), results[1].RequestUri);
        Assert.Equal(new Uri("http://a.test/three"), results[2].RequestUri);

        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Default"));
            Assert.Contains("yes", result.Headers.GetValues("X-Default"));
        }
    }

    [Fact(Timeout = 5_000,
        DisplayName = "ENR-031: Date header not auto-generated (RFC 9110 §6.6.1: clients SHOULD NOT send Date)")]
    public void Should_NotAddDate_When_NoDateHeader()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.False(result.Headers.Date.HasValue);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-032: Existing Date header preserved")]
    public void Should_PreserveDate_When_AlreadyPresent()
    {
        var enricher = CreateEnricher();

        var existingDate = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.Date = existingDate;
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Date.HasValue);
        Assert.Equal(existingDate, result.Headers.Date!.Value);
    }

    [Fact(Timeout = 5_000, DisplayName = "ENR-033: Explicit Date header set by caller is preserved as-is")]
    public void Should_PreserveExplicitDate_When_CallerSetsIt()
    {
        var enricher = CreateEnricher();

        var expectedDate = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.Date = expectedDate;
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Date.HasValue);
        Assert.Equal(expectedDate, result.Headers.Date!.Value);
    }
}
