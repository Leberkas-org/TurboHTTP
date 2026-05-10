using System.Net;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.MicroBenchmarks.Pipeline;

[Config(typeof(MicroBenchmarkConfig))]
public class VersionDispatchBenchmark : EngineTestBase
{
    private static readonly byte[] OkResponse =
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private ISourceQueueWithComplete<HttpRequestMessage> _queue = null!;
    private Channel<HttpResponseMessage> _responses = null!;

    [Params("1.0", "1.1")]
    public string HttpVersion { get; set; } = "1.1";

    private Version VersionValue => HttpVersion switch
    {
        "1.0" => new Version(1, 0),
        _ => new Version(1, 1)
    };

    [GlobalSetup]
    public void Setup()
    {
        _responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(2, 0), CreateFakeConnectionFlow(() => OkResponse))
            .Register(new Version(3, 0), CreateFakeConnectionFlow(() => OkResponse));
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var (queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => _responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        _queue = queue;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _queue.Complete();
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> DispatchRequest()
    {
        await _queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = VersionValue
        });

        var response = await _responses.Reader.ReadAsync();
        return response.StatusCode;
    }
}
