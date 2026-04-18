using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Caching;

public sealed class CacheBidiAsyncBodySpec : StreamTestBase
{
    private sealed class DelayedContent : HttpContent
    {
        private readonly TaskCompletionSource<byte[]> _tcs = new();

        public void Complete(byte[] data) => _tcs.SetResult(data);

        public void Fail(Exception ex) => _tcs.SetException(ex);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var data = await _tcs.Task;
            await stream.WriteAsync(data, TestContext.Current.CancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return _tcs.Task.ContinueWith(t =>
            {
                var ms = new MemoryStream(t.Result);
                return (Stream)ms;
            }, TaskScheduler.Default);
        }
    }

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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_push_response_immediately_while_body_read_is_pending()
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

        var resultTask = RunResponseAsync(stage, response);

        // Response should be pushed immediately — don't complete body yet
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Cache should NOT be populated yet (body still pending)
        var lookup = new HttpRequestMessage(HttpMethod.Get, "http://example.com/slow");
        Assert.Null(store.Get(lookup));

        // Complete the body — triggers async PipeTo cache storage
        delayedContent.Complete("slow body data"u8.ToArray());

        var results = await resultTask;
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Allow the PipeTo callback to fire
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/slow"));
        Assert.NotNull(entry);
        Assert.Equal("slow body data", System.Text.Encoding.UTF8.GetString(entry.Body.Span));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-3")]
    public async Task CacheBidiStage_should_store_in_cache_after_async_body_completes()
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

        var resultTask = RunResponseAsync(stage, response);

        // Complete the body
        delayedContent.Complete("pending body"u8.ToArray());

        var results = await resultTask;
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Allow the PipeTo callback to fire
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var entry = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/pending"));
        Assert.NotNull(entry);
        Assert.Equal("pending body", System.Text.Encoding.UTF8.GetString(entry.Body.Span));
    }
}