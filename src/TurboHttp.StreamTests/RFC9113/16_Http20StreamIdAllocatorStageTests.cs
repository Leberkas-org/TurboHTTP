using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the HTTP/2 stream ID allocator stage per RFC 9113.
/// Verifies that client-initiated streams are assigned odd, monotonically increasing IDs starting from 1.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="StreamIdAllocatorStage"/>.
/// RFC 9113 §5.1.1: HTTP/2 stream identifier allocation rules for client-initiated streams.
/// </remarks>
public sealed class Http20StreamIdAllocatorStageTests : StreamTestBase
{
    private async Task<IReadOnlyList<(HttpRequestMessage Request, int StreamId)>> RunAsync(
        params HttpRequestMessage[] requests)
    {
        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocatorStage()))
            .RunWith(Sink.Seq<(HttpRequestMessage, int)>(), Materializer);
    }

    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}");

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-001: First stream ID is 1")]
    public async Task Should_AssignStreamId1_When_FirstRequestArrives()
    {
        var results = await RunAsync(MakeRequest());

        Assert.Single(results);
        Assert.Equal(1, results[0].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-002: Consecutive IDs are 1, 3, 5, 7")]
    public async Task Should_AssignConsecutiveIdsAscendingByTwo_When_MultipleRequestsArrive()
    {
        var results = await RunAsync(
            MakeRequest("/a"), MakeRequest("/b"), MakeRequest("/c"), MakeRequest("/d"));

        Assert.Equal(4, results.Count);
        Assert.Equal(1, results[0].StreamId);
        Assert.Equal(3, results[1].StreamId);
        Assert.Equal(5, results[2].StreamId);
        Assert.Equal(7, results[3].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-003: 10 requests produce 10 distinct monotonically increasing IDs")]
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-004: Stream ID is always odd")]
    public async Task Should_AlwaysAssignOddStreamId_When_RequestArrives()
    {
        var requests = Enumerable.Range(0, 10).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        foreach (var (_, streamId) in results)
        {
            Assert.True(streamId % 2 == 1, $"Stream ID {streamId} must be odd");
        }
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-007: After N requests, next stream ID = 2N+1")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Should_AssignId2NPlus1_When_AfterNRequests(int n)
    {
        var requests = Enumerable.Range(0, n).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        Assert.Equal(n, results.Count);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(2 * i + 1, results[i].StreamId);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-008: Overflow when max stream ID (2^31-1) is reached")]
    public async Task Should_OverflowToNegativeValue_When_MaxStreamIdReached()
    {
        // Start at int.MaxValue (2^31-1 = 2147483647), which is the last valid odd stream ID.
        // The stage should emit this ID, then the next allocation wraps to a negative value.
        var stage = new StreamIdAllocatorStage(startStreamId: int.MaxValue);

        var results = await Source.From([MakeRequest("/last"), MakeRequest("/overflow")])
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<(HttpRequestMessage, int)>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.Equal(int.MaxValue, results[0].Item2);
        // After int.MaxValue, unchecked int overflow wraps to int.MinValue + 1
        Assert.Equal(unchecked(int.MaxValue + 2), results[1].Item2);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-005: Request object passed through unchanged (reference equality)")]
    public async Task Should_PassRequestObjectUnchanged_When_AllocatingStreamId()
    {
        var original = MakeRequest();

        var results = await RunAsync(original);

        Assert.Single(results);
        Assert.Same(original, results[0].Request);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-006: Stage terminates cleanly on UpstreamFinish")]
    public async Task Should_TerminateCleanly_When_UpstreamFinishes()
    {
        var results = await RunAsync(MakeRequest());

        // If we get here without timeout or exception, the stage completed cleanly
        Assert.Single(results);
    }
}
