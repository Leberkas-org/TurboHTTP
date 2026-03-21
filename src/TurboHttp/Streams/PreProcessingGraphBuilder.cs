using System;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

/// <summary>
/// Builds island 1 of the pipeline: pre-processing stages.
/// <para><b>Stage ordering (invariants verified in StageOrderingTests):</b></para>
/// <list type="number">
///   <item><description>RequestEnricherStage — applies BaseAddress, DefaultVersion, DefaultHeaders.</description></item>
///   <item><description>User request middleware stages (FIFO).</description></item>
///   <item><description>MergePreferred(redirect) — redirect feedback enters HERE so redirected requests
///         get fresh cookies (INV-7) but skip re-enrichment (INV-9).</description></item>
///   <item><description>CookieInjectionStage — RFC 6265 §5.4. Before CacheLookup because cookies
///         may be part of the Vary key (INV-2).</description></item>
///   <item><description>MergePreferred(retry) — retry feedback enters HERE so retried requests reuse the
///         same cookies from the original pass (INV-8).</description></item>
///   <item><description>CacheLookupStage — RFC 9111 §4. Cache hits bypass engine entirely (INV-10).</description></item>
/// </list>
/// </summary>
internal static class PreProcessingGraphBuilder
{
    public static IGraph<PreProcessShape, NotUsed> Build(
        PipelineDescriptor descriptor,
        Func<TurboRequestOptions> requestOptionsFactory)
    {
        return GraphDsl.Create(builder =>
        {
            var enricher = builder.Add(new RequestEnricherStage(requestOptionsFactory));
            var requestTip = enricher.Outlet;

            // Request middleware stages (FIFO, initial requests only — redirect feedback bypasses these)
            foreach (var mw in descriptor.Middlewares)
            {
                var middlewareStage = builder.Add(new MiddlewareRequestStage(mw));
                builder.From(requestTip).To(middlewareStage.Inlet);
                requestTip = middlewareStage.Outlet;
            }

            // Redirect merge (feedback from redirect stage in post-processing island)
            var redirectMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(redirectMerge.In(0));
            requestTip = redirectMerge.Out;

            // Cookie injection
            var cookieInject = builder.Add(new CookieInjectionStage(descriptor.CookieJar));
            builder.From(requestTip).To(cookieInject.Inlet);
            requestTip = cookieInject.Outlet;

            // Retry merge (feedback from retry stage in post-processing island)
            var retryMerge = builder.Add(new MergePreferred<HttpRequestMessage>(1));
            builder.From(requestTip).To(retryMerge.In(0));
            requestTip = retryMerge.Out;

            // Cache lookup
            var cacheLookup = builder.Add(new CacheLookupStage(descriptor.CacheStore, descriptor.CachePolicy));
            builder.From(requestTip).To(cacheLookup.In);

            return new PreProcessShape(
                enricher.Inlet,
                redirectMerge.Preferred,
                retryMerge.Preferred,
                cacheLookup.Out0, // cache miss → engine
                cacheLookup.Out1); // cache hit → post-processing
        });
    }
}
