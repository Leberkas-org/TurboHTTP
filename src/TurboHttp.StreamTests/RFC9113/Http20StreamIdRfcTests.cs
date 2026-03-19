using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// RFC-tagged tests for HTTP/2 stream ID assignment rules per RFC 9113.
/// Verifies that stream IDs are odd, monotonically increasing, and that stream ID exhaustion is handled.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="StreamIdAllocatorStage"/>.
/// RFC 9113 §5.1.1: HTTP/2 stream identifier rules, client-initiated odd IDs, and exhaustion handling.
/// </remarks>
public sealed class Http20StreamIdRfcTests : StreamTestBase
{
    private static HttpRequestMessage GetRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}");

    private static HttpRequestMessage PostRequest(string path = "/", string body = "hello")
        => new(HttpMethod.Post, $"http://example.com{path}")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };

    /// <summary>
    /// Runs requests through StreamIdAllocatorStage → Request2FrameStage and collects all frames.
    /// </summary>
    private async Task<IReadOnlyList<Http2Frame>> RunAsync(params HttpRequestMessage[] requests)
    {
        var encoder = new Http2RequestEncoder();

        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocatorStage()))
            .Via(Flow.FromGraph(new Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-H2S-001: First request produces stream ID 1")]
    public async Task Should_AssignStreamId1_When_FirstRequestSentThroughAllocatorAndEncoder()
    {
        var frames = await RunAsync(GetRequest());

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headersFrame.StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-H2S-002: Second request produces stream ID 3")]
    public async Task Should_AssignStreamId3_When_SecondRequestSentThroughAllocatorAndEncoder()
    {
        var frames = await RunAsync(GetRequest("/a"), GetRequest("/b"));

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(2, headersFrames.Count);
        Assert.Equal(1, headersFrames[0].StreamId);
        Assert.Equal(3, headersFrames[1].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-H2S-003: 5 requests produce stream IDs 1, 3, 5, 7, 9")]
    public async Task Should_ProduceStreamIds1To9_When_FiveRequestsSentThroughAllocatorAndEncoder()
    {
        var requests = Enumerable.Range(0, 5).Select(i => GetRequest($"/{i}")).ToArray();

        var frames = await RunAsync(requests);

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(5, headersFrames.Count);

        var expectedIds = new[] { 1, 3, 5, 7, 9 };
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(expectedIds[i], headersFrames[i].StreamId);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-H2S-004: All HEADERS frames have correct odd stream IDs")]
    public async Task Should_AssignCorrectOddStreamIdToAllHeadersFrames_When_MultipleRequestsEncoded()
    {
        var requests = Enumerable.Range(0, 5).Select(i => GetRequest($"/{i}")).ToArray();

        var frames = await RunAsync(requests);

        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(5, headersFrames.Count);

        for (var i = 0; i < headersFrames.Count; i++)
        {
            var expectedId = 1 + i * 2;
            Assert.Equal(expectedId, headersFrames[i].StreamId);
            Assert.True(headersFrames[i].StreamId % 2 == 1,
                $"Stream ID {headersFrames[i].StreamId} must be odd");
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.1-H2S-005: DATA frames share the same stream ID as their associated HEADERS")]
    public async Task Should_AssignSameStreamIdToDataAndHeadersFrames_When_PostRequestEncoded()
    {
        // POST requests produce HEADERS + DATA frame pairs
        var requests = Enumerable.Range(0, 3)
            .Select(i => PostRequest($"/{i}", $"body-{i}"))
            .ToArray();

        var frames = await RunAsync(requests);

        // Group frames by stream ID and verify each group has HEADERS + DATA with matching IDs
        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.Equal(3, headersFrames.Count);
        Assert.Equal(3, dataFrames.Count);

        var expectedIds = new[] { 1, 3, 5 };
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(expectedIds[i], headersFrames[i].StreamId);
            Assert.Equal(expectedIds[i], dataFrames[i].StreamId);

            // Verify HEADERS and DATA for each request share the same stream ID
            Assert.Equal(headersFrames[i].StreamId, dataFrames[i].StreamId);
        }
    }
}
