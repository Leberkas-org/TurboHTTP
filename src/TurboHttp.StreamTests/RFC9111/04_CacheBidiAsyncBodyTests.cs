using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC9111;

/// <summary>
/// Tests the CacheBidiStage async body read path (DL-010 fix).
/// Verifies that GetAsyncCallback keeps the stage scope alive during slow body reads,
/// preventing GroupByHostKeyStage from completing the substream prematurely.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CacheBidiStage"/>.
/// RFC 9111 §3: Storing responses in a cache — async body read for cache storage.
/// </remarks>
public sealed class CacheBidiAsyncBodyTests : StreamTestBase
{
    /// <summary>
    /// Custom HttpContent that delays body delivery via a TaskCompletionSource,
    /// forcing the CacheBidiStage into the async body read path.
    /// </summary>
    private sealed class DelayedContent : HttpContent
    {
        private readonly TaskCompletionSource<byte[]> _tcs = new();

        public void Complete(byte[] data) => _tcs.SetResult(data);

        public void Fail(Exception ex) => _tcs.SetException(ex);

        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context)
        {
            var data = await _tcs.Task;
            await stream.WriteAsync(data, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// Runs a response with delayed content through the bidi stage and completes
    /// the body asynchronously after a short delay.
    /// Uses a custom graph that feeds a single response and collects the output.
    /// </summary>
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        CacheBidiStage stage,
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

    // ============================
    // DL-010: Async body read tests
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-018: async body read completes and response is pushed even when body is slow")]
    public async Task AsyncBodyRead_Should_PushResponse_When_BodyIsDelayed()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var delayedContent = new DelayedContent();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = delayedContent,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/slow")
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        // Start the graph — the stage will initiate ReadAsByteArrayAsync which blocks
        var resultTask = RunResponseAsync(stage, response);

        // Simulate the slow body arriving after a delay
        await Task.Delay(200, TestContext.Current.CancellationToken);
        delayedContent.Complete("slow body data"u8.ToArray());

        // The stage should push the response after the async callback fires
        var results = await resultTask;
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Verify the response was stored in cache
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/slow");
        var entry = store.Get(lookup);
        Assert.NotNull(entry);
        Assert.Equal("slow body data", System.Text.Encoding.UTF8.GetString(entry.Body));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9111-3-CACHE-019: stage does not complete while async body read is in progress")]
    public async Task Stage_Should_NotComplete_While_AsyncBodyReadInProgress()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var delayedContent = new DelayedContent();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = delayedContent,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/pending")
        };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        response.Headers.Date = DateTimeOffset.UtcNow;

        // Start the graph — the source will complete upstream immediately after pushing
        // the single response, but the stage must NOT complete its outlet while the
        // async body read is pending.
        var resultTask = RunResponseAsync(stage, response);

        // Give the graph time to process the push and upstream completion
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The result task should NOT be completed yet — async read is still pending
        Assert.False(resultTask.IsCompleted, "Stage completed prematurely while async body read was in progress");

        // Now complete the body read
        delayedContent.Complete("pending body"u8.ToArray());

        // The stage should now push the response and complete
        var results = await resultTask;
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Verify cache storage
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/pending");
        var entry = store.Get(lookup);
        Assert.NotNull(entry);
    }
}
