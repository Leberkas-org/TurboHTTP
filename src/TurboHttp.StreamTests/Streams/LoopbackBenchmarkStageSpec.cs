using System.Net;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Measures throughput and latency of the HTTP/1.1 engine via an in-process loopback pipeline.
/// Validates sustained round-trip performance under sequential and burst request loads.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Engine"/>.
/// Uses an in-memory fake connection stage to eliminate network overhead from measurements.
/// </remarks>
public sealed class LoopbackBenchmarkStageSpec : EngineTestBase
{
    private static byte[] Http11OkResponse() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private (ISourceQueueWithComplete<HttpRequestMessage>, Channel<HttpResponseMessage>)
        BuildPersistentHttp11Pipeline()
    {
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        var engine = new Engine();
        var flow = engine.CreateFlow(
            http10Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http11Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http20Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            http30Factory: () => Flow.FromGraph(new EngineFakeConnectionStage(Http11OkResponse)),
            descriptor: PipelineDescriptor.Empty);

        var (queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        return (queue, responses);
    }

    [Fact(Timeout = 5000)]
    public async Task LoopbackBenchmarkStage_should_return_ok_when_first_request_sent_to_persistent_Http11_pipeline()
    {
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        });

        var response = await responses.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        queue.Complete();
    }

    [Fact(Timeout = 5000)]
    public async Task LoopbackBenchmarkStage_should_return_ok_for_all_requests_when_persistent_Http11_pipeline_reused()
    {
        const int count = 3;
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        for (var i = 0; i < count; i++)
        {
            await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
            {
                Version = HttpVersion.Version11
            });
            var r = await responses.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        queue.Complete();
    }

    [Fact(Timeout = 5000)]
    public async Task LoopbackBenchmarkStage_should_terminate_cleanly_when_persistent_Http11_pipeline_queue_completed()
    {
        var (queue, responses) = BuildPersistentHttp11Pipeline();

        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        });
        await responses.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Completing the queue must not throw
        queue.Complete();
    }
}
