using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine error handling.
/// Verifies RST_STREAM isolation, protocol error reporting, GOAWAY recovery,
/// and automatic reconnection after connection failures (RFC 9113 §5.4, §6.4, §6.8).
/// </summary>
public sealed class Http20ErrorHandlingTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20ErrorHandlingTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline using Http20Engine (BidiFlow pattern).
    /// Each call materialises a fresh pipeline (new TCP connection + new stream IDs).
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow()
    {
        const int windowSize = 2 * 1024 * 1024;
        var engine = new Http20Engine(windowSize).CreateFlow();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)))
                .Via(new PrependPrefaceStage(windowSize))
                .Via(new ConnectionStage(_clientManager));

        return engine.Join(transport);
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the pipeline.
    /// Each call materialises a fresh pipeline (new TCP connection + new stream IDs).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var flow = BuildFlow();

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends multiple HTTP/2 requests through a single multiplexed pipeline
    /// and collects up to <paramref name="expectedCount"/> responses.
    /// </summary>
    private async Task<List<HttpResponseMessage>> SendManyAsync(
        List<HttpRequestMessage> requests,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var requestEncoder = new Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var flow = Flow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            const int windowSize = 2 * 1024 * 1024;
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            b.From(connectionStage.Outlet).To(frameDecoder.Inlet);
            b.From(frameDecoder.Outlet).To(h2Connection.Inlet1);
            b.From(h2Connection.Outlet1).To(streamDecoder.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                streamIdAllocator.Inlet, streamDecoder.Outlet);
        }));

        var responses = new ConcurrentBag<HttpResponseMessage>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (queue, _) = Source.Queue<HttpRequestMessage>(requests.Count + 1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(res =>
                {
                    responses.Add(res);
                    if (responses.Count >= expectedCount)
                    {
                        tcs.TrySetResult();
                    }
                }),
                Keep.Both)
            .Run(_materializer);

        foreach (var request in requests)
        {
            await queue.OfferAsync(request);
        }

        await tcs.Task.WaitAsync(effectiveTimeout);
        return responses.ToList();
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version20
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "20E-INT-055: RST_STREAM on one stream does not kill other multiplexed streams")]
    public async Task RstStream_SingleStream_DoesNotKillConnection()
    {
        // RFC 9113 §5.4.2: RST_STREAM affects only a single stream, not the connection.
        // Send 3 requests on the same multiplexed pipeline: one to /h2/abort (triggers RST_STREAM)
        // and two to normal endpoints. The two normal responses should arrive successfully.
        var requests = new List<HttpRequestMessage>
        {
            MakeGet("/h2/abort"),
            MakeGet("/hello"),
            MakeGet("/ping"),
        };

        // Expect only 2 responses — the aborted stream produces RST_STREAM, not a response.
        var responses = await SendManyAsync(requests, expectedCount: 2);

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = new List<string>();
        foreach (var r in responses)
        {
            bodies.Add(await r.Content.ReadAsStringAsync());
        }

        Assert.Contains("Hello World", bodies);
        Assert.Contains("pong", bodies);
    }

    [Fact(DisplayName = "20E-INT-056: Server 500 error surfaces as HttpResponseMessage status code, not exception")]
    public async Task ProtocolError_SurfacesAsStatusCode()
    {
        // RFC 9113 §8.1: Server error responses (5xx) are carried on the stream as normal
        // HEADERS frames, not RST_STREAM. The pipeline must return them as HttpResponseMessage.
        var response = await SendAsync(MakeGet("/status/500"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact(DisplayName = "20E-INT-057: Auto-reconnect — sequential requests on fresh pipelines all succeed")]
    public async Task AutoReconnect_SequentialRequests_AllSucceed()
    {
        // Each SendAsync materialises a fresh pipeline with a new TCP connection.
        // This verifies that after one connection completes, a new connection can be
        // established to the same server without issues.
        var response1 = await SendAsync(MakeGet("/hello"));
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body1);

        var response2 = await SendAsync(MakeGet("/ping"));
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("pong", body2);

        var response3 = await SendAsync(MakeGet("/status/201"));
        Assert.Equal(HttpStatusCode.Created, response3.StatusCode);
    }

    [Fact(DisplayName = "20E-INT-058: Recovery after stream abort — new pipeline succeeds")]
    public async Task Recovery_AfterAbort_NewPipelineSucceeds()
    {
        // After a connection experiences a stream abort (RST_STREAM from /h2/abort),
        // a fresh pipeline to the same server should work normally.
        // First, attempt a request to the aborting endpoint — expect failure.
        var abortRequest = MakeGet("/h2/abort");
        var abortTask = SendAsync(abortRequest);

        // The abort may throw or timeout; either outcome is acceptable.
        try
        {
            await abortTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Expected — stream was reset by server.
        }

        // Now send a normal request on a fresh pipeline — must succeed.
        var response = await SendAsync(MakeGet("/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }
}
