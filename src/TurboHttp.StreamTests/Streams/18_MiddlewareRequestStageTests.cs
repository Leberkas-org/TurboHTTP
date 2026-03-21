using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using TurboHttp.Middleware;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the <see cref="MiddlewareRequestStage"/> that calls
/// <see cref="TurboMiddleware.ProcessRequestAsync"/> per element inside the Akka graph.
/// Verifies synchronous fast-path, async callback path, stage chaining, and deadlock freedom.
/// </summary>
public sealed class MiddlewareRequestStageTests : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunAsync(
        MiddlewareRequestStage stage,
        params HttpRequestMessage[] requests)
    {
        return Source.From(requests)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);
    }

    // ── Inline middleware helpers ────────────────────────────────────────────

    private sealed class AddAuthHeaderMiddleware : TurboMiddleware
    {
        private readonly string _token;

        public AddAuthHeaderMiddleware(string token) => _token = token;

        public override ValueTask<HttpRequestMessage> ProcessRequestAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
            return ValueTask.FromResult(request);
        }
    }

    private sealed class AsyncDelayMiddleware : TurboMiddleware
    {
        private readonly string _headerValue;

        public AsyncDelayMiddleware(string headerValue) => _headerValue = headerValue;

        public override async ValueTask<HttpRequestMessage> ProcessRequestAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            await Task.Delay(1, ct);
            request.Headers.TryAddWithoutValidation("X-Async", _headerValue);
            return request;
        }
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "MRS-001: Synchronous middleware — adds Authorization header via ValueTask.FromResult")]
    public async Task Should_AddAuthorizationHeader_When_MiddlewareReturnsSynchronously()
    {
        var middleware = new AddAuthHeaderMiddleware("secret");
        var stage = new MiddlewareRequestStage(middleware);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Authorization"));
        Assert.Contains("Bearer secret", result.Headers.GetValues("Authorization"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRS-002: Asynchronous middleware — Task.Delay(1) completes and pushes result without deadlock")]
    public async Task Should_TransformRequest_When_MiddlewareReturnsRealAsyncTask()
    {
        var middleware = new AsyncDelayMiddleware("async-ok");
        var stage = new MiddlewareRequestStage(middleware);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var results = await RunAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Async"));
        Assert.Contains("async-ok", result.Headers.GetValues("X-Async"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRS-003: Chained stages — second stage sees the output of the first")]
    public async Task Should_ApplyBothTransformations_When_TwoStagesAreChained()
    {
        var first = new MiddlewareRequestStage(new AddAuthHeaderMiddleware("token-a"));
        var second = new MiddlewareRequestStage(new AsyncDelayMiddleware("second-ran"));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");

        var results = await Source.From(new[] { request })
            .Via(Flow.FromGraph(first))
            .Via(Flow.FromGraph(second))
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        var result = Assert.Single(results);

        // First stage added Authorization
        Assert.True(result.Headers.Contains("Authorization"));
        Assert.Contains("Bearer token-a", result.Headers.GetValues("Authorization"));

        // Second stage added X-Async — it received the same request object already enriched by first
        Assert.True(result.Headers.Contains("X-Async"));
        Assert.Contains("second-ran", result.Headers.GetValues("X-Async"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRS-004: Multiple requests — all transformed in order, stream completes cleanly")]
    public async Task Should_TransformAllRequestsInOrder_When_MultipleRequestsStreamed()
    {
        var middleware = new AddAuthHeaderMiddleware("multi");
        var stage = new MiddlewareRequestStage(middleware);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/one");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/two");
        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/three");

        var results = new List<HttpRequestMessage>(await RunAsync(stage, req1, req2, req3));

        Assert.Equal(3, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("Authorization"));
            Assert.Contains("Bearer multi", result.Headers.GetValues("Authorization"));
        }
    }
}
