using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests for DL-009 (RetryBidi race) and DL-010 (CacheBidi async read)
/// deadlock fixes. Verifies the fixes remain intact and includes a stress test
/// combining retry + cache operations sequentially on HTTP/1.0-style single-use connections.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="RetryBidiStage"/>, <see cref="CacheBidiStage"/>.
/// These tests are the DLH10-001, DLH10-002, and DLH10-003 regression guards.
/// </remarks>
public sealed class DeadlockRegressionTests : StreamTestBase
{
    // ============================================================
    // DLH10-001: RetryBidi race — upstream finished + retry pending
    // Regression guard for DL-009 fix (TASK-030-001)
    // ============================================================

    [Fact(Timeout = 10_000,
        DisplayName = "DLH10-001: RetryBidi completes retry after upstream finishes without race hang")]
    public void RetryBidi_Should_CompleteRetry_When_UpstreamFinished_And_RetryPending()
    {
        // DL-009 scenario: upstream sends one request and completes.
        // Server returns 503 → retry triggered. The race was between
        // _inFlightCount decrement and retry enqueue — TryCompleteIfDone()
        // could fire in the window and close OutRequest prematurely.
        // After the fix (atomic transaction guard), the retry must succeed.

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dl009");
        var stage = new RetryBidiStage(new RetryPolicy());

        var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPublisher.ExpectSubscription();
        var respPubSub = responsePublisher.ExpectSubscription();
        var reqOutSub = requestOutProbe.ExpectSubscription();
        var respOutSub = responseOutProbe.ExpectSubscription();

        reqOutSub.Request(10);
        respOutSub.Request(10);

        // Push request then immediately complete upstream (simulates HTTP/1.0 single request)
        reqPubSub.SendNext(request);
        Assert.Same(request, requestOutProbe.ExpectNext());
        reqPubSub.SendComplete();

        // Server returns 503 → triggers retry decision
        var retryableResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request
        };
        respPubSub.SendNext(retryableResponse);

        // The retry request MUST appear on OutRequest (DL-009 fix prevents premature close)
        var retryReq = requestOutProbe.ExpectNext(TimeSpan.FromSeconds(5));
        Assert.Same(request, retryReq);

        // Server returns 200 for the retry
        var successResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request
        };
        respPubSub.SendNext(successResponse);

        // Final response passes through on Out2
        Assert.Same(successResponse, responseOutProbe.ExpectNext());

        // OutRequest should complete (upstream done, retry resolved, in-flight = 0)
        requestOutProbe.ExpectComplete();
    }

    // ============================================================
    // DLH10-002: CacheBidi async read — stage stays alive during body read
    // Regression guard for DL-010 fix (TASK-030-003)
    // ============================================================

    /// <summary>
    /// Custom HttpContent that delays body delivery, forcing the async body read path.
    /// </summary>
    private sealed class DelayedContent : HttpContent
    {
        private readonly TaskCompletionSource<byte[]> _tcs = new();

        public void Complete(byte[] data) => _tcs.SetResult(data);

        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context)
        {
            var data = await _tcs.Task;
            await stream.WriteAsync(data);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task<System.IO.Stream> CreateContentReadStreamAsync()
        {
            return _tcs.Task.ContinueWith(t =>
            {
                var ms = new System.IO.MemoryStream(t.Result);
                return (System.IO.Stream)ms;
            }, TaskScheduler.Default);
        }
    }

    [Fact(Timeout = 10_000,
        DisplayName = "DLH10-002: CacheBidi async body read does not hang when body is delayed")]
    public async Task CacheBidi_Should_CompleteAsyncBodyRead_Without_Deadlock()
    {
        // DL-010 scenario: CacheBidiStage initiates ReadAsByteArrayAsync which
        // previously blocked the stage actor scope. GroupByHostKeyStage would
        // see the substream as idle and complete it prematurely.
        // After the fix (GetAsyncCallback backpressure guard), the stage
        // stays alive during the async read and completes normally.

        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var delayedContent = new DelayedContent();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = delayedContent,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dl010")
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        // Build graph: push one response through CacheBidi and collect output
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var reqSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var reqSink = builder.Add(Sink.Ignore<HttpRequestMessage>());
                var respSource = builder.Add(Source.From(new[] { response }));

                builder.From(reqSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(reqSink);
                builder.From(respSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        var resultTask = RunnableGraph.FromGraph(graph).Run(Materializer);

        // Simulate delayed body (HTTP/1.0 — body read blocks until connection closes)
        await Task.Delay(300);

        // Stage must NOT have completed prematurely
        Assert.False(resultTask.IsCompleted, "CacheBidiStage completed before async body read finished — DL-010 regression");

        // Complete the body read
        delayedContent.Complete("dl010-test-body"u8.ToArray());

        // Stage should now push the response and complete
        var results = await resultTask;
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Verify the body was cached
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dl010");
        var entry = store.Get(lookup);
        Assert.NotNull(entry);
        Assert.Equal("dl010-test-body", System.Text.Encoding.UTF8.GetString(entry.Body));
    }

    // ============================================================
    // DLH10-003: Stress test — 10 sequential retry+cache operations
    // Verifies combined DL-009 + DL-010 fixes under repeated load
    // ============================================================

    [Fact(Timeout = 60_000,
        DisplayName = "DLH10-003: 10 sequential retry+cache ops complete without deadlock on HTTP/1.0")]
    public async Task Sequential_RetryCacheOps_Should_Complete_WithinTimeout()
    {
        // Stress test: runs 10 sequential retry+cache operations.
        // Each operation simulates the HTTP/1.0 pattern:
        //   1. Send request → get 503 → retry → get 200 with cacheable body
        //   2. CacheBidiStage stores the response body asynchronously
        // Previously, this would hang within 2-3 iterations due to DL-009/DL-010.

        for (var i = 0; i < 10; i++)
        {
            var store = new CacheStore();
            var retryStage = new RetryBidiStage(new RetryPolicy());
            var cacheStage = new CacheBidiStage(store);

            // Stack retry atop cache: request → retry → cache → (server) → cache → retry → response
            var stacked = BidiFlow.FromGraph(retryStage).Atop(BidiFlow.FromGraph(cacheStage));

            var requestPublisher = this.CreateManualPublisherProbe<HttpRequestMessage>();
            var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
            var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
            var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var bidi = b.Add(stacked);
                b.From(Source.FromPublisher(requestPublisher)).To(bidi.Inlet1);
                b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
                b.From(Source.FromPublisher(responsePublisher)).To(bidi.Inlet2);
                b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var reqPubSub = requestPublisher.ExpectSubscription();
            var respPubSub = responsePublisher.ExpectSubscription();
            var reqOutSub = requestOutProbe.ExpectSubscription();
            var respOutSub = responseOutProbe.ExpectSubscription();

            reqOutSub.Request(10);
            respOutSub.Request(10);

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/stress/{i}");

            // Push request, then immediately complete upstream (HTTP/1.0: single request)
            reqPubSub.SendNext(request);
            Assert.Same(request, requestOutProbe.ExpectNext(TimeSpan.FromSeconds(3)));
            reqPubSub.SendComplete();

            // Server returns 503 → retry
            var retryableResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request
            };
            respPubSub.SendNext(retryableResponse);

            // Retry request appears on OutRequest
            var retryReq = requestOutProbe.ExpectNext(TimeSpan.FromSeconds(3));
            Assert.Same(request, retryReq);

            // Server returns 200 with cacheable body for the retry
            var body = $"stress-response-{i}";
            var successResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request
            };
            successResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            successResponse.Headers.Date = DateTimeOffset.UtcNow;
            respPubSub.SendNext(successResponse);

            // Final response passes through — no hang means DL-009/DL-010 are fixed
            var finalResponse = responseOutProbe.ExpectNext(TimeSpan.FromSeconds(3));
            Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        }
    }
}
