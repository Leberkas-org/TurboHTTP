using Akka;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests for <see cref="FlowHostKeyGroupByExtensions.GroupBy{T,TMat}"/>.
///
/// Verifies that GroupByHostKey returns a real Akka SubFlow so that standard
/// SubFlowOperations extension methods (Select, Where, Take, SelectMany) are
/// available on the result and that the fan-out / fan-in mechanics work correctly.
/// </summary>
public sealed class HostKeySubFlowTests : StreamTestBase
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static HttpRequestMessage Req(string url)
        => new(HttpMethod.Get, url);

    /// <summary>
    /// Builds a flow that groups by host, applies <paramref name="configure"/>
    /// to the SubFlow, then merges substreams back into a single flow.
    /// </summary>
    private static Flow<HttpRequestMessage, TOut, NotUsed> BuildFlow<TOut>(
        Func<
            SubFlow<HttpRequestMessage, NotUsed, Sink<HttpRequestMessage, NotUsed>>,
            SubFlow<TOut, NotUsed, Sink<HttpRequestMessage, NotUsed>>> configure)
    {
        var subflow = Flow.Create<HttpRequestMessage>()
            .GroupBy(HostKey.FromRequest, maxSubstreams: 16);

        return (Flow<HttpRequestMessage, TOut, NotUsed>)
            configure(subflow).MergeSubstreams();
    }

    private async Task<IReadOnlyList<TOut>> RunAsync<TOut>(
        Flow<HttpRequestMessage, TOut, NotUsed> flow,
        IEnumerable<HttpRequestMessage> requests)
    {
        var result = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<TOut>(), Materializer);

        return result;
    }

    // ── HKSF-001: identity pass-through ─────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-001: GroupByHostKey + MergeSubstreams passes all elements through unchanged")]
    public async Task HKSF_001_IdentityPassThrough()
    {
        var requests = new[]
        {
            Req("http://host-a.example.com/1"),
            Req("http://host-b.example.com/1"),
            Req("http://host-a.example.com/2"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupBy(HostKey.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await RunAsync(flow, requests);

        Assert.Equal(3, results.Count);

        // Both hosts are present in output
        Assert.Contains(results, r => r.RequestUri!.Host == "host-a.example.com");
        Assert.Contains(results, r => r.RequestUri!.Host == "host-b.example.com");
    }

    // ── HKSF-002: SubFlowOperations.Select ──────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-002: Select on SubFlow transforms each element per-substream")]
    public async Task HKSF_002_SelectTransformsElements()
    {
        var requests = new[]
        {
            Req("http://alpha.example.com/ping"),
            Req("http://beta.example.com/ping"),
            Req("http://alpha.example.com/health"),
        };

        var flow = BuildFlow<string>(sf => sf.Select(r => r.RequestUri!.Host));

        var results = await RunAsync(flow, requests);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, results.Count(h => h == "alpha.example.com"));
        Assert.Equal(1, results.Count(h => h == "beta.example.com"));
    }

    // ── HKSF-003: SubFlowOperations.Where ───────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-003: Where on SubFlow filters elements within each substream")]
    public async Task HKSF_003_WhereFiltersElements()
    {
        var requests = new[]
        {
            Req("http://example.com/api/data"),
            Req("http://example.com/health"),
            Req("http://example.com/api/users"),
            Req("http://other.example.com/health"),
        };

        // Keep only requests whose path starts with /api
        var flow = BuildFlow<HttpRequestMessage>(
            sf => sf.Where(r => r.RequestUri!.AbsolutePath.StartsWith("/api")));

        var results = await RunAsync(flow, requests);

        // 2 of the 3 requests to example.com match; the other.example.com health doesn't
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.StartsWith("/api", r.RequestUri!.AbsolutePath));
    }

    // ── HKSF-004: SubFlowOperations.Take ────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-004: Take on SubFlow limits elements per-substream")]
    public async Task HKSF_004_TakeLimitsPerSubstream()
    {
        // 3 requests to host-a, 2 requests to host-b
        var requests = new[]
        {
            Req("http://host-a.example.com/1"),
            Req("http://host-a.example.com/2"),
            Req("http://host-a.example.com/3"),
            Req("http://host-b.example.com/1"),
            Req("http://host-b.example.com/2"),
        };

        // Take(2) per substream: host-a keeps 2, host-b keeps 2
        var flow = BuildFlow<HttpRequestMessage>(sf => sf.Take(2));

        var results = await RunAsync(flow, requests);

        // 2 from host-a + 2 from host-b = 4 total
        Assert.Equal(4, results.Count);
        Assert.Equal(2, results.Count(r => r.RequestUri!.Host == "host-a.example.com"));
        Assert.Equal(2, results.Count(r => r.RequestUri!.Host == "host-b.example.com"));
    }

    // ── HKSF-005: SubFlowOperations.Select + Where chained ──────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-005: Select and Where can be chained on the SubFlow")]
    public async Task HKSF_005_SelectAndWhereChained()
    {
        var requests = new[]
        {
            Req("http://example.com/api"),
            Req("http://example.com/health"),
            Req("http://example.com/api/v2"),
        };

        // Extract path, then keep only those starting with /api
        var flow = BuildFlow<string>(sf =>
            sf.Select(r => r.RequestUri!.AbsolutePath)
              .Where(path => path.StartsWith("/api")));

        var results = await RunAsync(flow, requests);

        Assert.Equal(2, results.Count);
        Assert.All(results, path => Assert.StartsWith("/api", path));
    }

    // ── HKSF-006: multiple hosts produce independent substreams ──────────────

    [Fact(Timeout = 10_000,
        DisplayName = "HKSF-006: Each host gets its own independent substream")]
    public async Task HKSF_006_EachHostGetsOwnSubstream()
    {
        // Interleave requests across 3 hosts
        var requests = new[]
        {
            Req("http://a.example.com/x"),
            Req("http://b.example.com/x"),
            Req("http://c.example.com/x"),
            Req("http://a.example.com/y"),
            Req("http://b.example.com/y"),
        };

        // Select host name to verify per-host fan-out
        var flow = BuildFlow<string>(sf => sf.Select(r => r.RequestUri!.Host));

        var results = await RunAsync(flow, requests);

        Assert.Equal(5, results.Count);
        Assert.Equal(2, results.Count(h => h == "a.example.com"));
        Assert.Equal(2, results.Count(h => h == "b.example.com"));
        Assert.Equal(1, results.Count(h => h == "c.example.com"));
    }
}
