using System.Net;
using Akka;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using Servus.Akka.Transport;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.MicroBenchmarks.Pipeline;

[Config(typeof(MicroBenchmarkConfig))]
public class FeedbackBufferBenchmark : EngineTestBase
{
    private static byte[] Redirect301(string location) =>
        System.Text.Encoding.Latin1.GetBytes(
            $"HTTP/1.1 301 Moved Permanently\r\nLocation: {location}\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"u8.ToArray();

    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _directFlow = null!;
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _redirectFlow = null!;

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> SequentialFlow(params byte[][] responses)
    {
        var index = 0;
        return CreateFakeConnectionFlow(() =>
        {
            var i = Interlocked.Increment(ref index) - 1;
            return i < responses.Length ? responses[i] : responses[^1];
        });
    }

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(() => Array.Empty<byte>());

    [GlobalSetup]
    public void Setup()
    {
        var engine = new Engine();

        var directTransports = new TransportRegistry()
            .Register(new Version(1, 0), SequentialFlow(Ok200()))
            .Register(new Version(1, 1), SequentialFlow(Ok200()))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        _directFlow = engine.CreateFlow(directTransports, PipelineDescriptor.Empty);

        var redirectTransports = new TransportRegistry()
            .Register(new Version(1, 0), SequentialFlow(Ok200()))
            .Register(new Version(1, 1), SequentialFlow(
                Redirect301("http://example.com/step2"),
                Ok200()))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var redirectDescriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);
        _redirectFlow = engine.CreateFlow(redirectTransports, redirectDescriptor);
    }

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task;
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> DirectResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        var response = await RunSingleAsync(_directFlow, request);
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> SingleRedirect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };
        var response = await RunSingleAsync(_redirectFlow, request);
        return response.StatusCode;
    }
}
