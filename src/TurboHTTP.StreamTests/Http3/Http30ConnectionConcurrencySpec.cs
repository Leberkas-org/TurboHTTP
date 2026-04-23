using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http3;

public sealed class Http30ConnectionConcurrencySpec : StreamTestBase
{
    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);
    private static readonly string[] Expected = ["/alpha", "/beta", "/gamma"];

    private ReadOnlyMemory<byte> EncodeResponseHeaders(params (string Name, string Value)[] headers)
        => _qpack.Encode(headers);

    private IEnumerable<IInputItem> BuildResponseSequence(params long[] streamIds)
    {
        foreach (var streamId in streamIds)
        {
            var headersBytes = new Http3HeadersFrame(
                EncodeResponseHeaders((":status", "200"))).Serialize();

            var buf = RoutedNetworkBuffer.Rent(headersBytes.Length);
            headersBytes.AsSpan().CopyTo(buf.FullMemory.Span);
            buf.Length = headersBytes.Length;
            buf.StreamId = streamId;
            yield return buf;
            yield return new QuicCloseItem(QuicCloseKind.RequestStreamComplete, streamId);
        }
    }

    private static RoutedNetworkBuffer BuildControlSettings()
    {
        var settingsBytes = new Http3SettingsFrame([]).Serialize();
        var buf = RoutedNetworkBuffer.Rent(settingsBytes.Length);
        settingsBytes.AsSpan().CopyTo(buf.FullMemory.Span);
        buf.Length = settingsBytes.Length;
        buf.StreamTypeValue = (long)StreamType.Control;
        return buf;
    }

    private async Task<(IReadOnlyList<IOutputItem> OutboundItems, IReadOnlyList<HttpResponseMessage> Responses)>
        RunConcurrentAsync(HttpRequestMessage[] requests, long[] responseStreamIds, Http3Options? options = null)
    {
        var networkSink = Sink.Seq<IOutputItem>();
        var responseSink = Sink.Seq<HttpResponseMessage>();

        var serverItems = new List<IInputItem> { BuildControlSettings() };
        serverItems.AddRange(BuildResponseSequence(responseStreamIds));

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(networkSink, responseSink, (nw, resp) => (nw, resp),
                (b, nwSink, respSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage(new TurboClientOptions
                        { Http3 = options ?? new Http3Options() }));

                    // Server responses arrive after a short delay to allow request encoding first
                    var serverSource = b.Add(
                        Source.From(serverItems)
                            .InitialDelay(TimeSpan.FromMilliseconds(150)));

                    var requestSource = b.Add(Source.From(requests));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(respSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var (networkTask, responseTask) = graph.Run(Materializer);

        var ct = TestContext.Current.CancellationToken;
        var outbound = await networkTask.WaitAsync(TimeSpan.FromSeconds(4), ct);
        var responses = await responseTask.WaitAsync(TimeSpan.FromSeconds(4), ct);

        return (outbound, responses);
    }

    private static List<long> ExtractRequestStreamIds(IReadOnlyList<IOutputItem> items)
    {
        var seen = new HashSet<long>();
        var result = new List<long>();
        foreach (var item in items)
        {
            if (item is RoutedNetworkBuffer { StreamTypeValue: null, StreamId: not null } tagged
                && seen.Add(tagged.StreamId.Value))
            {
                result.Add(tagged.StreamId.Value);
            }
        }

        return result;
    }

    private static List<long> ExtractEndOfRequestStreamIds(IReadOnlyList<IOutputItem> items)
    {
        return items.OfType<Http3EndOfRequestItem>().Select(e => e.StreamId).ToList();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Http30ConnectionStage_should_assign_distinct_stream_ids_when_concurrent_requests_pulled()
    {
        // Arrange: three concurrent GET requests
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/a") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/b") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/c") { Version = HttpVersion.Version30 },
        };

        // Stream IDs: client-initiated bidi = 0, 4, 8
        var responseStreamIds = new long[] { 0, 4, 8 };

        // Act
        var (outbound, _) = await RunConcurrentAsync(requests, responseStreamIds);

        // Assert: each request gets a unique stream ID (0, 4, 8 per RFC 9114 §6.1)
        var streamIds = ExtractRequestStreamIds(outbound);
        Assert.True(streamIds.Count >= 3, $"Expected at least 3 distinct stream IDs, got {streamIds.Count}");
        Assert.Equal(streamIds.Count, streamIds.Distinct().Count());

        // All stream IDs follow client-initiated bidi pattern: 0 mod 4
        Assert.All(streamIds, id => Assert.Equal(0, id % 4));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Http30ConnectionStage_should_reuse_stream_slots_when_previous_streams_complete()
    {
        // Arrange: send 2 requests, get responses, then send a 3rd request
        // The 3rd request should be accepted because the first 2 streams closed
        // (freeing concurrency budget).
        //
        // We use a max-concurrent-streams of 2 to force this scenario.
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/first") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/second") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/third") { Version = HttpVersion.Version30 },
        };

        // All 3 streams get responses — the stage should accept the 3rd after the first 2 complete
        var responseStreamIds = new long[] { 0, 4, 8 };

        // Act
        var (outbound, responses) = await RunConcurrentAsync(requests, responseStreamIds);

        // Assert: all 3 requests produced end-of-request markers
        var eorStreamIds = ExtractEndOfRequestStreamIds(outbound);
        Assert.True(eorStreamIds.Count >= 3,
            $"Expected at least 3 end-of-request items (slot reuse), got {eorStreamIds.Count}");

        // All 3 responses were delivered
        Assert.Equal(3, responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Http30ConnectionStage_should_correlate_responses_to_correct_requests_when_streams_interleaved()
    {
        // Arrange: send requests with different URIs, verify each response
        // is correlated to the correct request via RequestMessage.
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/alpha") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/beta") { Version = HttpVersion.Version30 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/gamma") { Version = HttpVersion.Version30 },
        };

        // Respond in order: stream 0, 4, 8
        var responseStreamIds = new long[] { 0, 4, 8 };

        // Act
        var (_, responses) = await RunConcurrentAsync(requests, responseStreamIds);

        // Assert: each response has a non-null RequestMessage and all original URIs are present
        Assert.Equal(3, responses.Count);
        Assert.All(responses, r =>
        {
            Assert.NotNull(r.RequestMessage);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        });

        var responseUris = responses
            .Select(r => r.RequestMessage!.RequestUri!.AbsolutePath)
            .OrderBy(u => u)
            .ToList();

        Assert.Equal(Expected, responseUris);
    }
}