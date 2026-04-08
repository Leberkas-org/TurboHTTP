using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Streams.Stages.Internal;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.Streams;

/// <summary>
/// Composes the BidiFlow feature stack on top of a protocol engine flow.
/// <para><b>Stacking order (outermost → innermost):</b></para>
/// <list type="number">
///   <item><description>TracingBidiStage — root "TurboHTTP.Request" activity lifecycle</description></item>
///   <item><description>User Handlers — HandlerBidiStage per TurboHandler (FIFO: [0] outermost)</description></item>
///   <item><description>RedirectBidiStage — RFC 9110 §15.4, internal feedback loop</description></item>
///   <item><description>CookieBidiStage — RFC 6265 §5.3–§5.4</description></item>
///   <item><description>RetryBidiStage — RFC 9110 §9.2, internal feedback loop</description></item>
///   <item><description>ExpectContinueBidiStage — RFC 9110 §10.1.1, Expect: 100-continue</description></item>
///   <item><description>CacheBidiStage — RFC 9111, internal short-circuit</description></item>
///   <item><description>ContentEncodingBidiStage — RFC 9110 §8.4 (request compression + response decompression)</description></item>
/// </list>
/// <para>Request direction: Handler[0] → … → Handler[N] → Redirect → Cookie → Retry → Expect100 → Cache → ContentEncoding → Engine</para>
/// <para>Response direction: Engine → ContentEncoding → Cache → Expect100 → Retry → Cookie → Redirect → Handler[N] → … → Handler[0]</para>
/// <para>Stages are collected into a list (innermost first) and wired together with the engine
/// in a single <see cref="GraphDsl"/> call — no intermediate <c>BidiFlow</c> wrapper or
/// <c>Join</c> call, avoiding the object that iterative <c>Atop</c> stacking or a separate
/// <c>BidiFlow.Join(engineFlow)</c> would produce.</para>
/// </summary>
internal static class FeaturePipelineBuilder
{
    internal static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Build(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> engineFlow,
        PipelineDescriptor descriptor,
        Func<TurboRequestOptions> requestOptionsFactory)
    {
        // Collect active feature stages innermost-first.
        // Index 0 connects directly to the engine; the last index is the outermost layer.
        // Capacity: ContentEncoding + Cache + Expect100 + Retry + Cookie + Redirect + Handlers + Tracing
        var maxLayers = 6 + descriptor.Handlers.Count + 1;
        var layers = new List<IGraph<BidiShape<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage>, NotUsed>>(maxLayers);

        if (descriptor.AutomaticDecompression || descriptor.CompressionPolicy is not null)
        {
            layers.Add(new ContentEncodingBidiStage(descriptor.AutomaticDecompression, descriptor.CompressionPolicy));
        }

        if (descriptor.CacheStore is not null)
        {
            layers.Add(new CacheBidiStage(descriptor.CacheStore, descriptor.CachePolicy));
        }

        if (descriptor.Expect100Policy is not null)
        {
            layers.Add(new ExpectContinueBidiStage(descriptor.Expect100Policy));
        }

        if (descriptor.RetryPolicy is not null)
        {
            layers.Add(new RetryBidiStage(descriptor.RetryPolicy));
        }

        if (descriptor.CookieJar is not null)
        {
            layers.Add(new CookieBidiStage(descriptor.CookieJar));
        }

        if (descriptor.RedirectPolicy is not null)
        {
            layers.Add(new RedirectBidiStage(descriptor.RedirectPolicy));
        }

        // Handlers added outermost-first: reverse iteration so Handlers[0] is outermost.
        for (var i = descriptor.Handlers.Count - 1; i >= 0; i--)
        {
            layers.Add(new HandlerBidiStage(descriptor.Handlers[i], i));
        }

        // Tracing is the absolute outermost layer — only when a listener is active.
        if (TurboHttpInstrumentation.IsTracingActive)
        {
            layers.Add(new TracingBidiStage());
        }

        // Inline enrichment as a Select() — avoids a separate GraphStage instance.
        // Enrichment applies BaseAddress, default version, default headers, Referer
        // sanitization, and If-Range validation per RFC 9110.
        var enricher = new RequestEnricher(requestOptionsFactory);
        var enriched = Flow.Create<HttpRequestMessage>().Select(enricher.Enrich);

        if (layers.Count == 0)
        {
            return enriched.Via(engineFlow);
        }

        // Build a single Flow via GraphDsl — feature BidiLayers and engine wired in one graph,
        // eliminating the intermediate BidiFlow wrapper that BidiFlow.Join(engineFlow) would create.
        var compositeFlow = Flow.FromGraph(
            GraphDsl.Create(engineFlow, (b, engine) =>
            {
                // Add each layer to the graph builder — plain loop avoids LINQ enumerator + extra List.
                var stages = new BidiShape<HttpRequestMessage, HttpRequestMessage,
                    HttpResponseMessage, HttpResponseMessage>[layers.Count];
                for (var i = 0; i < layers.Count; i++)
                {
                    stages[i] = b.Add(layers[i]);
                }

                // Wire request direction (outer→inner) and response direction (inner→outer).
                // stages[i+1] is outer relative to stages[i].
                for (var i = 0; i < stages.Length - 1; i++)
                {
                    b.From(stages[i + 1].Outlet1).To(stages[i].Inlet1);
                    b.From(stages[i].Outlet2).To(stages[i + 1].Inlet2);
                }

                // Connect innermost feature layer directly to the engine flow.
                b.From(stages[0].Outlet1).To(engine.Inlet);
                b.From(engine.Outlet).To(stages[0].Inlet2);

                return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                    stages[^1].Inlet1,      // outermost request inlet
                    stages[^1].Outlet2);    // outermost response outlet
            }));

        return enriched.Via(compositeFlow);
    }
}
