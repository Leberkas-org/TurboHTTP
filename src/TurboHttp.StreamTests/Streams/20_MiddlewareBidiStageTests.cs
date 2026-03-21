using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Middleware;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the <see cref="MiddlewareBidiStage"/> covering both request and response directions,
/// synchronous and asynchronous middleware, chaining via <c>Atop</c>, and stream completion.
/// </summary>
public sealed class MiddlewareBidiStageTests : StreamTestBase
{
    // ── Test middleware implementations ───────────────────────────────

    /// <summary>Synchronous middleware that adds a custom header to requests.</summary>
    private sealed class RequestHeaderMiddleware : TurboMiddleware
    {
        private readonly string _name;
        private readonly string _value;

        public RequestHeaderMiddleware(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override ValueTask<HttpRequestMessage> ProcessRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.TryAddWithoutValidation(_name, _value);
            return ValueTask.FromResult(request);
        }
    }

    /// <summary>Async middleware that adds a custom header to requests after a delay.</summary>
    private sealed class AsyncRequestHeaderMiddleware : TurboMiddleware
    {
        private readonly string _name;
        private readonly string _value;

        public AsyncRequestHeaderMiddleware(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override async ValueTask<HttpRequestMessage> ProcessRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(10, ct);
            request.Headers.TryAddWithoutValidation(_name, _value);
            return request;
        }
    }

    /// <summary>Synchronous middleware that adds a custom header to responses.</summary>
    private sealed class ResponseHeaderMiddleware : TurboMiddleware
    {
        private readonly string _name;
        private readonly string _value;

        public ResponseHeaderMiddleware(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override ValueTask<HttpResponseMessage> ProcessResponseAsync(HttpRequestMessage original, HttpResponseMessage response, CancellationToken ct)
        {
            response.Headers.TryAddWithoutValidation(_name, _value);
            return ValueTask.FromResult(response);
        }
    }

    /// <summary>Async middleware that adds a custom header to responses after a delay.</summary>
    private sealed class AsyncResponseHeaderMiddleware : TurboMiddleware
    {
        private readonly string _name;
        private readonly string _value;

        public AsyncResponseHeaderMiddleware(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override async ValueTask<HttpResponseMessage> ProcessResponseAsync(HttpRequestMessage original, HttpResponseMessage response, CancellationToken ct)
        {
            await Task.Delay(10, ct);
            response.Headers.TryAddWithoutValidation(_name, _value);
            return response;
        }
    }

    // ── Graph helpers ────────────────────────────────────────────────

    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        MiddlewareBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        MiddlewareBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs requests through a composed BidiFlow (multiple stages chained via Atop).
    /// </summary>
    private Task<IImmutableList<HttpRequestMessage>> RunRequestThroughComposedAsync(
        BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> bidi,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var b = builder.Add(bidi);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(b.Inlet1);
                builder.From(b.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(b.Inlet2);
                builder.From(b.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    /// <summary>
    /// Runs responses through a composed BidiFlow (multiple stages chained via Atop).
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseThroughComposedAsync(
        BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> bidi,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var b = builder.Add(bidi);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(b.Inlet1);
                builder.From(b.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(b.Inlet2);
                builder.From(b.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private static HttpResponseMessage MakeResponse(HttpRequestMessage? originalRequest = null)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        if (originalRequest is not null)
        {
            response.RequestMessage = originalRequest;
        }
        return response;
    }

    // ============================
    // Sync request transformation
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-001: sync middleware adds header to request")]
    public async Task SyncRequestTransformation_Should_InjectHeader()
    {
        var middleware = new RequestHeaderMiddleware("X-Trace", "abc");
        var stage = new MiddlewareBidiStage(middleware, 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Trace"));
        Assert.Equal("abc", result.Headers.GetValues("X-Trace").Single());
    }

    // ============================
    // Async request transformation
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-002: async middleware with Task.Delay adds header to request")]
    public async Task AsyncRequestTransformation_Should_InjectHeader()
    {
        var middleware = new AsyncRequestHeaderMiddleware("X-Async", "delayed");
        var stage = new MiddlewareBidiStage(middleware, 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Async"));
        Assert.Equal("delayed", result.Headers.GetValues("X-Async").Single());
    }

    // ============================
    // Sync response transformation
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-003: sync middleware adds header to response")]
    public async Task SyncResponseTransformation_Should_InjectHeader()
    {
        var middleware = new ResponseHeaderMiddleware("X-Resp", "injected");
        var stage = new MiddlewareBidiStage(middleware, 0);
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = MakeResponse(originalRequest);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Resp"));
        Assert.Equal("injected", result.Headers.GetValues("X-Resp").Single());
    }

    // ============================
    // Async response transformation
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-004: async middleware with Task.Delay adds header to response")]
    public async Task AsyncResponseTransformation_Should_InjectHeader()
    {
        var middleware = new AsyncResponseHeaderMiddleware("X-AsyncResp", "async-val");
        var stage = new MiddlewareBidiStage(middleware, 0);
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = MakeResponse(originalRequest);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-AsyncResp"));
        Assert.Equal("async-val", result.Headers.GetValues("X-AsyncResp").Single());
    }

    // ============================
    // Original request access in response direction
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-005: response middleware receives original request via response.RequestMessage")]
    public async Task ResponseDirection_Should_ReceiveOriginalRequest()
    {
        var capturedOriginal = new TaskCompletionSource<HttpRequestMessage>();

        var middleware = new CapturingResponseMiddleware(capturedOriginal);
        var stage = new MiddlewareBidiStage(middleware, 0);
        var originalRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        originalRequest.Headers.TryAddWithoutValidation("X-OriginalMarker", "present");
        var response = MakeResponse(originalRequest);

        await RunResponseAsync(stage, response);

        var captured = await capturedOriginal.Task;
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://example.com/api", captured.RequestUri!.ToString());
        Assert.True(captured.Headers.Contains("X-OriginalMarker"));
    }

    /// <summary>Middleware that captures the original request passed to ProcessResponseAsync.</summary>
    private sealed class CapturingResponseMiddleware : TurboMiddleware
    {
        private readonly TaskCompletionSource<HttpRequestMessage> _tcs;

        public CapturingResponseMiddleware(TaskCompletionSource<HttpRequestMessage> tcs) => _tcs = tcs;

        public override ValueTask<HttpResponseMessage> ProcessResponseAsync(HttpRequestMessage original, HttpResponseMessage response, CancellationToken ct)
        {
            _tcs.TrySetResult(original);
            return ValueTask.FromResult(response);
        }
    }

    // ============================
    // Multiple BidiStages via Atop
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-006: Atop composes middlewares — request gets headers from both in FIFO order")]
    public async Task Atop_Should_ApplyCumulativeRequestHeaders()
    {
        var mw1 = new RequestHeaderMiddleware("X-First", "1");
        var mw2 = new RequestHeaderMiddleware("X-Second", "2");

        var bidi = BidiFlow.FromGraph(new MiddlewareBidiStage(mw1, 0))
            .Atop(BidiFlow.FromGraph(new MiddlewareBidiStage(mw2, 1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestThroughComposedAsync(bidi, request);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Headers.GetValues("X-First").Single());
        Assert.Equal("2", result.Headers.GetValues("X-Second").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-007: Atop composes middlewares — response gets headers from both")]
    public async Task Atop_Should_ApplyCumulativeResponseHeaders()
    {
        var mw1 = new ResponseHeaderMiddleware("X-RFirst", "r1");
        var mw2 = new ResponseHeaderMiddleware("X-RSecond", "r2");

        var bidi = BidiFlow.FromGraph(new MiddlewareBidiStage(mw1, 0))
            .Atop(BidiFlow.FromGraph(new MiddlewareBidiStage(mw2, 1)));

        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = MakeResponse(originalRequest);

        var results = await RunResponseThroughComposedAsync(bidi, response);

        var result = Assert.Single(results);
        Assert.Equal("r1", result.Headers.GetValues("X-RFirst").Single());
        Assert.Equal("r2", result.Headers.GetValues("X-RSecond").Single());
    }

    // ============================
    // Multiple requests/responses with completion
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-008: multiple requests flow through and stream completes")]
    public async Task MultipleRequests_Should_FlowThroughWithCompletion()
    {
        var middleware = new RequestHeaderMiddleware("X-Count", "yes");
        var stage = new MiddlewareBidiStage(middleware, 0);

        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}"))
            .ToArray();

        var results = await RunRequestAsync(stage, requests);

        Assert.Equal(5, results.Count);
        for (var i = 0; i < results.Count; i++)
        {
            Assert.True(results[i].Headers.Contains("X-Count"));
            Assert.Equal($"http://example.com/{i + 1}", results[i].RequestUri!.ToString());
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "MBIDI-009: multiple responses flow through and stream completes")]
    public async Task MultipleResponses_Should_FlowThroughWithCompletion()
    {
        var middleware = new ResponseHeaderMiddleware("X-Processed", "true");
        var stage = new MiddlewareBidiStage(middleware, 0);

        var responses = Enumerable.Range(1, 5)
            .Select(i =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}");
                return MakeResponse(req);
            })
            .ToArray();

        var results = await RunResponseAsync(stage, responses);

        Assert.Equal(5, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Processed"));
            Assert.Equal("true", result.Headers.GetValues("X-Processed").Single());
        }
    }
}
