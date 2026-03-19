using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class StreamIdAllocatorStageTests : StreamTestBase
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
    public async Task SID_001_First_Stream_Id_Is_1()
    {
        var results = await RunAsync(MakeRequest());

        Assert.Single(results);
        Assert.Equal(1, results[0].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-002: Consecutive IDs are 1, 3, 5, 7")]
    public async Task SID_002_Consecutive_Ids_Ascending_By_Two()
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
    public async Task SID_003_Ten_Requests_Produce_Ten_Distinct_Ids()
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
    public async Task SID_004_Stream_Id_Is_Always_Odd()
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
    public async Task SID_007_After_N_Requests_Next_Id_Is_2N_Plus_1(int n)
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
    public async Task SID_008_Overflow_At_Max_Stream_Id()
    {
        // Start at int.MaxValue (2^31-1 = 2147483647), which is the last valid odd stream ID.
        // The stage should emit this ID, then the next allocation wraps to a negative value.
        var stage = new StreamIdAllocatorStage(startStreamId: int.MaxValue);

        var results = await Source.From(new[] { MakeRequest("/last"), MakeRequest("/overflow") })
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<(HttpRequestMessage, int)>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.Equal(int.MaxValue, results[0].Item2);
        // After int.MaxValue, unchecked int overflow wraps to int.MinValue + 1
        Assert.Equal(unchecked(int.MaxValue + 2), results[1].Item2);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-005: Request object passed through unchanged (reference equality)")]
    public async Task SID_005_Request_Object_Reference_Equality()
    {
        var original = MakeRequest();

        var results = await RunAsync(original);

        Assert.Single(results);
        Assert.Same(original, results[0].Request);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-SID-006: Stage terminates cleanly on UpstreamFinish")]
    public async Task SID_006_Stage_Terminates_Cleanly_On_UpstreamFinish()
    {
        var results = await RunAsync(MakeRequest());

        // If we get here without timeout or exception, the stage completed cleanly
        Assert.Single(results);
    }
}
