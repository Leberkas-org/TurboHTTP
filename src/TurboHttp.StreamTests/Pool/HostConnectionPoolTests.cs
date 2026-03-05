using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace TurboHttp.StreamTests.Pool;

public sealed class HostConnectionPoolTests : EngineTestBase
{
    // ── helpers ──────────────────────────────────────────────────────────

    private sealed class RequestCounts
    {
        public int Http10;
        public int Http11;
        public int Http20;
    }

    /// <summary>
    /// Builds a queue-backed routing flow that mirrors the HostConnectionPool architecture
    /// (Source.Queue → version-routing flow → Sink.ForEach), using fake echo engines
    /// that reflect each request's version and x-correlation-id header back as a response.
    /// </summary>
    private ISourceQueueWithComplete<HttpRequestMessage> BuildFakePool(Action<HttpResponseMessage> onResponse)
    {
        var flow = BuildFakeRoutingFlow();

        return Source
            .Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.ForEach<HttpResponseMessage>(onResponse), Keep.Left)
            .Run(Materializer);
    }

    /// <summary>
    /// Builds a queue-backed routing flow with per-version request tracking via RequestCounts.
    /// </summary>
    private (ISourceQueueWithComplete<HttpRequestMessage>, RequestCounts) BuildFakePoolWithTracking(
        Action<HttpResponseMessage> onResponse)
    {
        var counts = new RequestCounts();
        var flow = BuildFakeRoutingFlowWithTracking(counts);

        var queue = Source
            .Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.ForEach<HttpResponseMessage>(onResponse), Keep.Left)
            .Run(Materializer);

        return (queue, counts);
    }

    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFakeRoutingFlow()
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(4, msg => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            }));
            var merge = builder.Add(new Merge<HttpResponseMessage>(4));

            var fake10 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fake11 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fake20 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fake30 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));

            builder.From(partition.Out(0)).Via(fake10).To(merge);
            builder.From(partition.Out(1)).Via(fake11).To(merge);
            builder.From(partition.Out(2)).Via(fake20).To(merge);
            builder.From(partition.Out(3)).Via(fake30).To(merge);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
        }));
    }

    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFakeRoutingFlowWithTracking(
        RequestCounts counts)
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(4, msg => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            }));
            var merge = builder.Add(new Merge<HttpResponseMessage>(4));

            var fake10 = builder.Add(Flow.Create<HttpRequestMessage>().Select(msg =>
            {
                System.Threading.Interlocked.Increment(ref counts.Http10);
                return EchoResponse(msg);
            }));

            var fake11 = builder.Add(Flow.Create<HttpRequestMessage>().Select(msg =>
            {
                System.Threading.Interlocked.Increment(ref counts.Http11);
                return EchoResponse(msg);
            }));

            var fake20 = builder.Add(Flow.Create<HttpRequestMessage>().Select(msg =>
            {
                System.Threading.Interlocked.Increment(ref counts.Http20);
                return EchoResponse(msg);
            }));

            var fake30 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));

            builder.From(partition.Out(0)).Via(fake10).To(merge);
            builder.From(partition.Out(1)).Via(fake11).To(merge);
            builder.From(partition.Out(2)).Via(fake20).To(merge);
            builder.From(partition.Out(3)).Via(fake30).To(merge);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
        }));
    }

    private static HttpResponseMessage EchoResponse(HttpRequestMessage req)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version };
        if (req.Headers.TryGetValues("x-correlation-id", out var corrIds))
        {
            response.Headers.TryAddWithoutValidation("x-correlation-id", string.Join(",", corrIds));
        }

        return response;
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-001: HTTP/1.0 request through pool returns correct status and version")]
    public async Task Http10_Request_Through_Pool_Returns_Correct_Status_And_Version()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var queue = BuildFakePool(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        await queue.OfferAsync(request);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-002: HTTP/1.1 request through pool returns correct status and version")]
    public async Task Http11_Request_Through_Pool_Returns_Correct_Status_And_Version()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var queue = BuildFakePool(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        await queue.OfferAsync(request);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-003: HTTP/2.0 request through pool returns correct status and version")]
    public async Task Http20_Request_Through_Pool_Returns_Correct_Status_And_Version()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var queue = BuildFakePool(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version20
        };
        await queue.OfferAsync(request);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-004: Mixed-version batch via pool: each response version matches request")]
    public async Task Mixed_Version_Batch_Via_Pool_Each_Response_Version_Matches_Request()
    {
        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();
        var queue = BuildFakePool(res =>
        {
            lock (results)
            {
                results.Add(res);
                if (results.Count == 3)
                {
                    tcs.TrySetResult();
                }
            }
        });

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version20 }
        };

        foreach (var req in requests)
        {
            await queue.OfferAsync(req);
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Version == HttpVersion.Version10);
        Assert.Contains(results, r => r.Version == HttpVersion.Version11);
        Assert.Contains(results, r => r.Version == HttpVersion.Version20);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-005: HTTP/1.0 bytes only reach HTTP/1.0 fake connection")]
    public async Task Http10_Bytes_Only_Reach_Http10_Fake_Connection()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var (queue, counts) = BuildFakePoolWithTracking(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        await queue.OfferAsync(request);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, counts.Http10);
        Assert.Equal(0, counts.Http11);
        Assert.Equal(0, counts.Http20);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-006: HTTP/1.1 bytes only reach HTTP/1.1 fake connection")]
    public async Task Http11_Bytes_Only_Reach_Http11_Fake_Connection()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var (queue, counts) = BuildFakePoolWithTracking(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        await queue.OfferAsync(request);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, counts.Http10);
        Assert.Equal(1, counts.Http11);
        Assert.Equal(0, counts.Http20);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-007: HTTP/2.0 bytes only reach HTTP/2.0 fake connection")]
    public async Task Http20_Bytes_Only_Reach_Http20_Fake_Connection()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var (queue, counts) = BuildFakePoolWithTracking(res => tcs.TrySetResult(res));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version20
        };
        await queue.OfferAsync(request);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, counts.Http10);
        Assert.Equal(0, counts.Http11);
        Assert.Equal(1, counts.Http20);
    }

    [Fact(Timeout = 10_000, DisplayName = "ST-POOL-008: Backpressure: queue of 256 requests does not deadlock")]
    public async Task Backpressure_Queue_Of_256_Requests_Does_Not_Deadlock()
    {
        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();
        var queue = BuildFakePool(res =>
        {
            lock (results)
            {
                results.Add(res);
                if (results.Count == 256)
                {
                    tcs.TrySetResult();
                }
            }
        });

        for (int i = 0; i < 256; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
            {
                Version = HttpVersion.Version11
            };
            await queue.OfferAsync(request);
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(256, results.Count);
    }
}
