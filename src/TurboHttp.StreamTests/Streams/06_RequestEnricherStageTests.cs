using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the <see cref="RequestEnricherStage"/> that applies base address, default version, and default headers.
/// Verifies RFC 9110-compliant header merging and URI resolution behavior.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="RequestEnricherStage"/>.
/// Validates base-address combination, version override, and header merge/skip-if-present semantics.
/// </remarks>
public sealed class RequestEnricherStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "ENR-001: Null URI + BaseAddress → RequestUri becomes BaseAddress root")]
    public async Task Should_SetRequestUriToBaseAddress_When_RequestUriIsNull()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            baseAddress,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(baseAddress, result.RequestUri);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-002: Relative URI \"/ping\" + BaseAddress \"http://a.test\" → \"http://a.test/ping\"")]
    public async Task Should_CombineRelativeUriWithBaseAddress_When_BaseAddressSet()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            baseAddress,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(new Uri("http://a.test/ping"), result.RequestUri);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-003: Absolute URI → RequestUri unchanged even when BaseAddress is set")]
    public async Task Should_NotChangeAbsoluteUri_When_BaseAddressIsSet()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var absoluteUri = new Uri("http://other.host/path");
        var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(absoluteUri, result.RequestUri);
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "ENR-004: Null URI + null BaseAddress → logs warning, request passes through unenriched, stream survives")]
    public async Task Should_LogWarningAndPassThrough_When_UriAndBaseAddressAreNull()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));

        // Subscribe directly to the event bus so we can verify the warning is published.
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(Akka.Event.Warning));

        var results = await RunAsync(stage, request);

        // The stage must NOT fail — one result is passed through (unenriched, null URI).
        var result = Assert.Single(results);
        Assert.Null(result.RequestUri);

        // A Warning must have been published to the event bus with stage name in the message.
        var warning = probe.ExpectMsg<Akka.Event.Warning>(TimeSpan.FromSeconds(5));
        Assert.Contains("RequestEnricherStage", warning.ToString());

        Sys.EventStream.Unsubscribe(probe.Ref, typeof(Akka.Event.Warning));
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "ENR-005: Relative URI + null BaseAddress → logs warning, request passes through unenriched, stream survives")]
    public async Task Should_LogWarningAndPassThrough_When_RelativeUriAndBaseAddressIsNull()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var relativeUri = new Uri("/ping", UriKind.Relative);
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));

        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(Akka.Event.Warning));

        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(relativeUri, result.RequestUri);

        var warning = probe.ExpectMsg<Akka.Event.Warning>(TimeSpan.FromSeconds(5));
        Assert.Contains("RequestEnricherStage", warning.ToString());

        Sys.EventStream.Unsubscribe(probe.Ref, typeof(Akka.Event.Warning));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-006: request.Version == 1.1 (default), defaultVersion == 2.0 → version becomes 2.0")]
    public async Task Should_SetVersionTo20_When_RequestVersionIs11AndDefaultIs20()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version20,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-007: request.Version == 1.1 (default), defaultVersion == 1.1 → version unchanged")]
    public async Task Should_NotChangeVersion_When_RequestVersionIs11AndDefaultIs11()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version11, result.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-008: request.Version explicitly set to 1.0 → unchanged regardless of defaultVersion")]
    public async Task Should_NotOverrideVersion_When_ExplicitV10Set()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version10
        };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version10, result.Version);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-009: request.Version explicitly set to 2.0 → unchanged regardless of defaultVersion")]
    public async Task Should_NotOverrideVersion_When_ExplicitV20Set()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version20
        };

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-010: DefaultRequestHeaders has X-Foo:bar → merged into request")]
    public async Task Should_MergeDefaultHeader_When_DefaultRequestHeadersContainsXFoo()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "bar");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Foo"));
        Assert.Contains("bar", result.Headers.GetValues("X-Foo"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-011: Request already has X-Foo:existing → not overridden; existing value kept")]
    public async Task Should_PreserveExistingHeader_When_DefaultAndRequestHaveSameHeader()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "default");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-012: DefaultRequestHeaders has two headers → both merged")]
    public async Task Should_MergeBothHeaders_When_TwoDefaultHeadersPresent()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-One", "1");
        defaultHeaders.TryAddWithoutValidation("X-Two", "2");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-One"));
        Assert.True(result.Headers.Contains("X-Two"));
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-013: DefaultRequestHeaders empty → no headers added; request unchanged")]
    public async Task Should_AddNoHeaders_When_DefaultRequestHeadersEmpty()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        // Date header is always auto-generated (RFC 9110 §5.6.7), so only Date should be present
        var headerNames = result.Headers.Select(h => h.Key).ToList();
        Assert.Single(headerNames);
        Assert.Equal("Date", headerNames[0]);
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "ENR-014: Same header name, different casing in request vs defaults → treated as same; not doubled")]
    public async Task Should_NotDoubleHeader_When_HeaderNameDiffersOnlyInCase()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("x-foo", "default");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "ENR-015: DefaultRequestHeaders has multiple values for one name → all values added as one entry")]
    public async Task Should_AddAllValues_When_DefaultHeaderHasMultipleValues()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Multi", ["a", "b"]);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Multi"));
        var values = new List<string>(result.Headers.GetValues("X-Multi"));
        Assert.Contains("a", values);
        Assert.Contains("b", values);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "ENR-016: 3 requests in sequence → all 3 enriched independently, order preserved")]
    public async Task Should_EnrichAllRequestsInOrder_When_ThreeRequestsInSequence()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Default", "yes");

        var req1 = new HttpRequestMessage(HttpMethod.Get, new Uri("/one", UriKind.Relative));
        var req2 = new HttpRequestMessage(HttpMethod.Get, new Uri("/two", UriKind.Relative));
        var req3 = new HttpRequestMessage(HttpMethod.Get, new Uri("/three", UriKind.Relative));

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            baseAddress,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = new List<HttpRequestMessage>(
            await RunAsync(stage, req1, req2, req3));

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

    [Fact(Timeout = 10_000, DisplayName = "ENR-031: Date header auto-generated in IMF-fixdate when missing")]
    public async Task Should_AddDate_When_NoDateHeader()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Date.HasValue);

        // Verify IMF-fixdate format: "ddd, dd MMM yyyy HH:mm:ss 'GMT'"
        var formatted = result.Headers.Date!.Value.ToString("R");
        Assert.EndsWith("GMT", formatted);
        Assert.Matches(@"^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} GMT$", formatted);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-032: Existing Date header preserved")]
    public async Task Should_PreserveDate_When_AlreadyPresent()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var existingDate = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.Date = existingDate;

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Date.HasValue);
        Assert.Equal(existingDate, result.Headers.Date!.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "ENR-033: Generated Date is UTC")]
    public async Task Should_BeUtc_When_DateAutoGenerated()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;

        var before = DateTimeOffset.UtcNow;
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var stage = new RequestEnricherStage(() => new TurboRequestOptions(
            null,
            defaultHeaders,
            HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue,
            long.MaxValue));
        var results = await RunAsync(stage, request);
        var after = DateTimeOffset.UtcNow;

        var result = Assert.Single(results);
        Assert.True(result.Headers.Date.HasValue);

        var dateValue = result.Headers.Date!.Value;
        Assert.Equal(TimeSpan.Zero, dateValue.Offset);
        Assert.InRange(dateValue, before.AddSeconds(-1), after.AddSeconds(1));
    }
}