using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class RequestEnricherStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

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

    // ── URI enrichment ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "ENR-001: Null URI + BaseAddress → RequestUri becomes BaseAddress root")]
    public async Task ENR_001_NullUri_WithBaseAddress_BecomesBaseAddress()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var stage = new RequestEnricherStage(baseAddress, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(baseAddress, result.RequestUri);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-002: Relative URI \"/ping\" + BaseAddress \"http://a.test\" → \"http://a.test/ping\"")]
    public async Task ENR_002_RelativeUri_WithBaseAddress_Combined()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        var stage = new RequestEnricherStage(baseAddress, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(new Uri("http://a.test/ping"), result.RequestUri);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-003: Absolute URI → RequestUri unchanged even when BaseAddress is set")]
    public async Task ENR_003_AbsoluteUri_NotChanged()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var absoluteUri = new Uri("http://other.host/path");
        var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);

        var stage = new RequestEnricherStage(baseAddress, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(absoluteUri, result.RequestUri);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-004: Null URI, null BaseAddress → stage fails with InvalidOperationException")]
    public async Task ENR_004_NullUri_NullBaseAddress_Fails()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await RunAsync(stage, request));

        // Akka may wrap in AggregateException; unwrap to find InvalidOperationException
        var inner = ex is AggregateException agg ? agg.InnerException : ex;
        Assert.IsType<InvalidOperationException>(inner);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-005: Relative URI, null BaseAddress → stage fails with InvalidOperationException")]
    public async Task ENR_005_RelativeUri_NullBaseAddress_Fails()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await RunAsync(stage, request));

        var inner = ex is AggregateException agg ? agg.InnerException : ex;
        Assert.IsType<InvalidOperationException>(inner);
    }

    // ── Version enrichment ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "ENR-006: request.Version == 1.1 (default), defaultVersion == 2.0 → version becomes 2.0")]
    public async Task ENR_006_DefaultVersion_11_DefaultIs20_BecomesV20()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };

        var stage = new RequestEnricherStage(null, HttpVersion.Version20, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-007: request.Version == 1.1 (default), defaultVersion == 1.1 → version unchanged")]
    public async Task ENR_007_DefaultVersion_11_DefaultIs11_Unchanged()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version11, result.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-008: request.Version explicitly set to 1.0 → unchanged regardless of defaultVersion")]
    public async Task ENR_008_ExplicitV10_NotOverridden()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version10
        };

        var stage = new RequestEnricherStage(null, HttpVersion.Version20, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version10, result.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-009: request.Version explicitly set to 2.0 → unchanged regardless of defaultVersion")]
    public async Task ENR_009_ExplicitV20_NotOverridden()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version20
        };

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    // ── Header enrichment ──────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "ENR-010: DefaultRequestHeaders has X-Foo:bar → merged into request")]
    public async Task ENR_010_DefaultHeader_Merged()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "bar");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Foo"));
        Assert.Contains("bar", result.Headers.GetValues("X-Foo"));
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-011: Request already has X-Foo:existing → not overridden; existing value kept")]
    public async Task ENR_011_RequestHeaderNotOverridden()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "default");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-012: DefaultRequestHeaders has two headers → both merged")]
    public async Task ENR_012_TwoDefaultHeaders_BothMerged()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-One", "1");
        defaultHeaders.TryAddWithoutValidation("X-Two", "2");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-One"));
        Assert.True(result.Headers.Contains("X-Two"));
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-013: DefaultRequestHeaders empty → no headers added; request unchanged")]
    public async Task ENR_013_EmptyDefaults_NoHeadersAdded()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Empty(result.Headers);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-014: Same header name, different casing in request vs defaults → treated as same; not doubled")]
    public async Task ENR_014_HeaderCaseInsensitive_NotDoubled()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("x-foo", "default");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-015: DefaultRequestHeaders has multiple values for one name → all values added as one entry")]
    public async Task ENR_015_MultipleValuesForOneName_AllAdded()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Multi", new[] { "a", "b" });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(null, HttpVersion.Version11, defaultHeaders);
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Multi"));
        var values = new List<string>(result.Headers.GetValues("X-Multi"));
        Assert.Contains("a", values);
        Assert.Contains("b", values);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-016: 3 requests in sequence → all 3 enriched independently, order preserved")]
    public async Task ENR_016_ThreeRequests_AllEnriched_OrderPreserved()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Default", "yes");

        var req1 = new HttpRequestMessage(HttpMethod.Get, new Uri("/one", UriKind.Relative));
        var req2 = new HttpRequestMessage(HttpMethod.Get, new Uri("/two", UriKind.Relative));
        var req3 = new HttpRequestMessage(HttpMethod.Get, new Uri("/three", UriKind.Relative));

        var stage = new RequestEnricherStage(baseAddress, HttpVersion.Version11, defaultHeaders);
        var results = new List<HttpRequestMessage>(
            await RunAsync(stage, req1, req2, req3));

        Assert.Equal(3, results.Count);

        Assert.Equal(new Uri("http://a.test/one"),   results[0].RequestUri);
        Assert.Equal(new Uri("http://a.test/two"),   results[1].RequestUri);
        Assert.Equal(new Uri("http://a.test/three"), results[2].RequestUri);

        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Default"));
            Assert.Contains("yes", result.Headers.GetValues("X-Default"));
        }
    }
}
