using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Validates the persistent-queue pipeline pattern used by
/// <c>EnginePipelineBenchmarks</c>: a single materialized stream stays alive
/// across multiple offer/read cycles (rather than materializing a fresh graph
/// per request as the unit tests do).
///
/// HTTP/2 round-trip coverage is in Http20/ — these tests focus on the
/// benchmark infrastructure pattern (Source.Queue → persistent flow → Channel sink).
/// </summary>
public sealed class LoopbackBenchmarkStageTests : EngineTestBase
{
    private static byte[] Http11OkResponse() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private (ISourceQueueWithComplete<HttpRequestMessage>, Channel<HttpResponseMessage>)
        BuildPersistentHttp11Pipeline()
    {
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            http10Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http11Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http20Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http30Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            options: null);

        var (queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        return (queue, responses);
    }

    [Fact]
    public async Task PersistentHttp11Pipeline_FirstRequest_ReturnsOk()
    {
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        });

        var response = await responses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        queue.Complete();
    }

    [Fact]
    public async Task PersistentHttp11Pipeline_ReusedAcrossMultipleRequests_AllReturnOk()
    {
        const int count = 3;
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        for (var i = 0; i < count; i++)
        {
            await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
            {
                Version = HttpVersion.Version11
            });
            var r = await responses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        queue.Complete();
    }

    [Fact]
    public async Task PersistentHttp11Pipeline_QueueComplete_StreamTerminatesCleanly()
    {
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        });
        await responses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // Completing the queue must not throw
        queue.Complete();
    }
}
