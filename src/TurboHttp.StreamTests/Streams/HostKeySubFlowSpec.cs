using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Internal;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the host-key subflow grouping used to multiplex multiple requests onto per-host connections.
/// Verifies that requests are partitioned by host key and each subflow completes independently.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="GroupByRequestEndpointStage{T}"/>.
/// Validates subflow isolation, completion propagation, and back-pressure per host partition.
/// </remarks>
public sealed class HostKeySubFlowSpec : StreamTestBase
{
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
            .GroupByRequestEndpoint(RequestEndpoint.FromRequest, maxSubstreams: 16);

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

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_pass_all_elements_through_when_group_by_host_key_and_merge_substreams_applied()
    {
        var requests = new[]
        {
            Req("http://host-a.example.com/1"),
            Req("http://host-b.example.com/1"),
            Req("http://host-a.example.com/2"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await RunAsync(flow, requests);

        Assert.Equal(3, results.Count);

        // Both hosts are present in output
        Assert.Contains(results, r => r.RequestUri!.Host == "host-a.example.com");
        Assert.Contains(results, r => r.RequestUri!.Host == "host-b.example.com");
    }

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_transform_each_element_when_select_applied_on_sub_flow()
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

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_filter_elements_when_where_applied_on_sub_flow()
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

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_limit_elements_per_substream_when_take_applied()
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

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_apply_chained_operations_when_select_and_where_chained()
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

    [Fact(Timeout = 10_000)]
    public async Task HostKeySubFlow_should_create_independent_substream_when_multiple_hosts_present()
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
