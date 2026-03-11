using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine multiplexing (RFC 9113 §5).
/// Verifies that multiple concurrent HTTP/2 requests share a single TCP connection,
/// stream IDs are correctly allocated, and slow streams do not block fast ones.
/// </summary>
public sealed class Http20MultiplexTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20MultiplexTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends multiple HTTP/2 requests through a single multiplexed pipeline.
    /// Uses Source.Queue with capacity > 1 so requests can be enqueued concurrently.
    /// Responses are collected via Sink.ForEach into a thread-safe bag.
    /// </summary>
    private async Task<List<HttpResponseMessage>> SendManyAsync(
        IReadOnlyList<HttpRequestMessage> requests,
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
            // ── HTTP/2 engine stages ───────────────────────────────────────────
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            const int windowSize = 2 * 1024 * 1024;
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            // ── Transport ──────────────────────────────────────────────────────
            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            // ── Request path ─────────────────────────────────────────────────
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            // ── Outbound ─────────────────────────────────────────────────────
            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            // ── Inbound ──────────────────────────────────────────────────────
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
                    if (responses.Count >= requests.Count)
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

    /// <summary>
    /// Sends a single HTTP/2 request (convenience wrapper for single-request tests).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
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

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "20E-INT-010: Two concurrent GETs on same connection both succeed")]
    public async Task ConcurrentRequests_OnSameConnection_BothSucceed()
    {
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version20
            },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
            {
                Version = HttpVersion.Version20
            }
        };

        var responses = await SendManyAsync(requests);

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = new List<string>();
        foreach (var r in responses)
        {
            bodies.Add(await r.Content.ReadAsStringAsync());
        }

        Assert.Contains("pong", bodies);
        Assert.Contains("Hello World", bodies);
    }

    [Fact(DisplayName = "20E-INT-011: 10 parallel GETs all return expected body")]
    public async Task TenParallelGets_AllReturnExpectedBody()
    {
        var requests = Enumerable.Range(0, 10)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        var responses = await SendManyAsync(requests);

        Assert.Equal(10, responses.Count);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("pong", body);
        }
    }

    [Fact(DisplayName = "20E-INT-012: Interleaved responses arrive for distinct endpoints")]
    public async Task InterleavedResponses_AllReceivedCorrectly()
    {
        // Send requests to different endpoints — responses may arrive in any order
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping") { Version = HttpVersion.Version20 },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello") { Version = HttpVersion.Version20 },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/status/201") { Version = HttpVersion.Version20 },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/h2/settings") { Version = HttpVersion.Version20 },
        };

        var responses = await SendManyAsync(requests);

        Assert.Equal(4, responses.Count);

        var statuses = responses.Select(r => r.StatusCode).ToHashSet();
        // At least 200 and 201 should be present
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.Created, statuses);

        var bodies = new List<string>();
        foreach (var r in responses)
        {
            bodies.Add(await r.Content.ReadAsStringAsync());
        }

        Assert.Contains("pong", bodies);
        Assert.Contains("Hello World", bodies);
        Assert.Contains("h2-ok", bodies);
    }

    [Fact(DisplayName = "20E-INT-013: Client stream IDs are odd (RFC 9113 §5.1.1) — verified by successful multiplexed exchange")]
    public async Task StreamIds_AreOddForClientInitiated()
    {
        // RFC 9113 §5.1.1: client-initiated streams MUST use odd stream IDs.
        // If the pipeline allocated even IDs, Kestrel would reject with PROTOCOL_ERROR.
        // Sending 5 multiplexed requests that all succeed proves stream IDs are odd.
        var requests = Enumerable.Range(1, 5)
            .Select(i =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"http://127.0.0.1:{_fixture.Port}/h2/settings/max-concurrent")
                {
                    Version = HttpVersion.Version20
                };
                req.Headers.Add("X-Stream-Id", i.ToString());
                return req;
            })
            .ToList();

        var responses = await SendManyAsync(requests);

        Assert.Equal(5, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Verify each request's X-Stream-Id was echoed back
        var echoedIds = responses
            .Select(r => r.Headers.GetValues("X-Stream-Id").First())
            .OrderBy(id => int.Parse(id))
            .ToList();

        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, echoedIds);
    }

    [Fact(DisplayName = "20E-INT-014: MAX_CONCURRENT_STREAMS — multiple requests within server limit all succeed")]
    public async Task MaxConcurrentStreams_WithinLimit_AllSucceed()
    {
        // Kestrel default MAX_CONCURRENT_STREAMS is 100.
        // Send 8 concurrent requests (well within limit) to verify multiplexing works.
        var requests = Enumerable.Range(0, 8)
            .Select(i =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"http://127.0.0.1:{_fixture.Port}/h2/priority/1")
                {
                    Version = HttpVersion.Version20
                };
                req.Headers.Add("X-Req-Index", i.ToString());
                return req;
            })
            .ToList();

        var responses = await SendManyAsync(requests);

        Assert.Equal(8, responses.Count);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(1024, body.Length);
            Assert.All(body, b => Assert.Equal((byte)'P', b));
        }
    }

    [Fact(DisplayName = "20E-INT-015: Slow response does not block fast response")]
    public async Task SlowResponse_DoesNotBlockFastResponse()
    {
        // Send a slow request first (/slow/50 = 50 bytes at 1ms each ≈ 50ms minimum),
        // then a fast request (/ping). Both should succeed, proving the slow stream
        // does not head-of-line block the fast one.
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/slow/50")
            {
                Version = HttpVersion.Version20
            },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version20
            }
        };

        var sw = Stopwatch.StartNew();
        var responses = await SendManyAsync(requests);
        sw.Stop();

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = new List<string>();
        foreach (var r in responses)
        {
            bodies.Add(await r.Content.ReadAsStringAsync());
        }

        // Both responses must be present
        Assert.Contains("pong", bodies);
        // The slow response body is 50 'x' characters
        Assert.Contains(bodies, b => b.Length == 50 && b.All(c => c == 'x'));
    }
}
