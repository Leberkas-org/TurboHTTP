using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 stream ID allocator stage per RFC 9000 §2.1.
/// Verifies that client-initiated bidirectional QUIC streams are assigned IDs 0, 4, 8, 12, …
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30StreamIdAllocatorStage"/>.
/// RFC 9000 §2.1: QUIC stream identifier allocation rules for client-initiated bidirectional streams.
/// </remarks>
public sealed class Http30StreamIdAllocatorStageTests : StreamTestBase
{
    private async Task<IReadOnlyList<(HttpRequestMessage Request, long StreamId)>> RunAsync(
        params HttpRequestMessage[] requests)
    {
        return await Source.From(requests)
            .Via(Flow.FromGraph(new Http30StreamIdAllocatorStage()))
            .RunWith(Sink.Seq<(HttpRequestMessage, long)>(), Materializer);
    }

    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}");

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-001: First stream ID is 0")]
    public async Task Should_AssignStreamId0_When_FirstRequestArrives()
    {
        var results = await RunAsync(MakeRequest());

        Assert.Single(results);
        Assert.Equal(0L, results[0].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-002: Consecutive IDs are 0, 4, 8, 12")]
    public async Task Should_AssignConsecutiveIdsAscendingByFour_When_MultipleRequestsArrive()
    {
        var results = await RunAsync(
            MakeRequest("/a"), MakeRequest("/b"), MakeRequest("/c"), MakeRequest("/d"));

        Assert.Equal(4, results.Count);
        Assert.Equal(0L, results[0].StreamId);
        Assert.Equal(4L, results[1].StreamId);
        Assert.Equal(8L, results[2].StreamId);
        Assert.Equal(12L, results[3].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-003: 10 requests produce 10 distinct monotonically increasing IDs")]
    public async Task Should_ProduceTenDistinctMonotonicallyIncreasingIds_When_TenRequestsArrive()
    {
        var requests = Enumerable.Range(0, 10).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        Assert.Equal(10, results.Count);
        var ids = results.Select(r => r.StreamId).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(ids[i] > ids[i - 1], $"ID {ids[i]} should be greater than {ids[i - 1]}");
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-004: Stream ID is always divisible by 4")]
    public async Task Should_AlwaysAssignIdDivisibleByFour_When_RequestArrives()
    {
        var requests = Enumerable.Range(0, 10).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        foreach (var (_, streamId) in results)
        {
            Assert.True(streamId % 4 == 0, $"Stream ID {streamId} must be divisible by 4");
        }
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-005: After N requests, IDs are 0, 4, 8, … 4(N-1)")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Should_AssignId4N_When_AfterNRequests(int n)
    {
        var requests = Enumerable.Range(0, n).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        Assert.Equal(n, results.Count);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(4L * i, results[i].StreamId);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-006: Request object passed through unchanged (reference equality)")]
    public async Task Should_PassRequestObjectUnchanged_When_AllocatingStreamId()
    {
        var original = MakeRequest();

        var results = await RunAsync(original);

        Assert.Single(results);
        Assert.Same(original, results[0].Request);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9000-2.1-H3SID-007: Stage terminates cleanly on UpstreamFinish")]
    public async Task Should_TerminateCleanly_When_UpstreamFinishes()
    {
        var results = await RunAsync(MakeRequest());

        // If we get here without timeout or exception, the stage completed cleanly
        Assert.Single(results);
    }
}
