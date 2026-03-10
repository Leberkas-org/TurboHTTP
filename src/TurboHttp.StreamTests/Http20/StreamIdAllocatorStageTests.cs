using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class StreamIdAllocatorStageTests : StreamTestBase
{
    private async Task<IReadOnlyList<(HttpRequestMessage Request, int StreamId)>> RunAsync(
        params HttpRequestMessage[] requests)
    {
        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocator()))
            .RunWith(Sink.Seq<(HttpRequestMessage, int)>(), Materializer);
    }

    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}");

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-001: First stream ID is 1")]
    public async Task SID_001_First_Stream_Id_Is_1()
    {
        var results = await RunAsync(MakeRequest());

        Assert.Single(results);
        Assert.Equal(1, results[0].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-002: Consecutive IDs are 1, 3, 5, 7")]
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-003: 10 requests produce 10 distinct monotonically increasing IDs")]
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-004: Stream ID is always odd")]
    public async Task SID_004_Stream_Id_Is_Always_Odd()
    {
        var requests = Enumerable.Range(0, 10).Select(i => MakeRequest($"/{i}")).ToArray();

        var results = await RunAsync(requests);

        foreach (var (_, streamId) in results)
        {
            Assert.True(streamId % 2 == 1, $"Stream ID {streamId} must be odd");
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-005: Request object passed through unchanged (reference equality)")]
    public async Task SID_005_Request_Object_Reference_Equality()
    {
        var original = MakeRequest();

        var results = await RunAsync(original);

        Assert.Single(results);
        Assert.Same(original, results[0].Request);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1-SID-006: Stage terminates cleanly on UpstreamFinish")]
    public async Task SID_006_Stage_Terminates_Cleanly_On_UpstreamFinish()
    {
        var results = await RunAsync(MakeRequest());

        // If we get here without timeout or exception, the stage completed cleanly
        Assert.Single(results);
    }
}
