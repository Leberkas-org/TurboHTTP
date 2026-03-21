using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

/// <summary>
/// Builds island 3 of the pipeline: post-processing stages.
/// <para><b>Stage ordering (invariants verified in StageOrderingTests):</b></para>
/// <list type="number">
///   <item><description>CookieStorageStage — RFC 6265 §5.3. Stores Set-Cookie headers in the shared CookieJar.
///         Must run before CacheStorage (INV-3).</description></item>
///   <item><description>CacheStorageStage — RFC 9111 §3. Stores cacheable 2xx responses. Must run before
///         RetryStage (INV-4).</description></item>
///   <item><description>RetryStage — RFC 9110 §9.2. Evaluates idempotent retry for 408/503 responses.
///         Must run before RedirectStage (INV-5).</description></item>
///   <item><description>Merge(cache hits) — merges RetryStage.Out0 with cache hits from CacheLookupStage.
///         Cache hits bypass RetryStage entirely (INV-10).</description></item>
///   <item><description>RedirectStage — RFC 9110 §15.4. Evaluates 301/302/303/307/308 redirects.</description></item>
///   <item><description>User response middleware stages (FIFO).</description></item>
/// </list>
/// <para>Exposed ports: 2 inlets (response from engine, cache hits from lookup),
/// 3 outlets (final response, retry feedback, redirect feedback).</para>
/// </summary>
internal static class PostProcessingGraphBuilder
{
    public static IGraph<PostProcessShape, NotUsed> Build(PipelineDescriptor descriptor)
    {
        return GraphDsl.Create(builder =>
        {
            var cookieStorage = builder.Add(new CookieStorageStage(descriptor.CookieJar));
            var cacheStorage = builder.Add(new CacheStorageStage(descriptor.CacheStore));
            var retry = builder.Add(new RetryStage(descriptor.RetryPolicy));
            var cacheMerge = builder.Add(new Merge<HttpResponseMessage>(2));
            var redirect = builder.Add(new RedirectStage(descriptor.RedirectPolicy));

            // CookieStorage → CacheStorage → Retry
            builder.From(cookieStorage.Outlet).To(cacheStorage.Inlet);
            builder.From(cacheStorage.Outlet).To(retry.In);

            // Retry.Out0 (pass-through) → CacheMerge.In(0), CacheMerge → Redirect
            builder.From(retry.Out0).To(cacheMerge.In(0));
            builder.From(cacheMerge.Out).To(redirect.In);

            // Response middleware stages (FIFO, final responses only — after redirect resolution)
            var responseTip = redirect.Out0;
            foreach (var mw in descriptor.Middlewares)
            {
                var middlewareStage = builder.Add(new MiddlewareResponseStage(mw));
                builder.From(responseTip).To(middlewareStage.Inlet);
                responseTip = middlewareStage.Outlet;
            }

            return new PostProcessShape(
                cookieStorage.Inlet, // response input from engine+decompression
                cacheMerge.In(1), // cache hit input from cache lookup
                responseTip, // final response output (last middleware stage or redirect.Out0 if none)
                retry.Out1, // retry feedback → pre-processing
                redirect.Out1); // redirect feedback → pre-processing
        });
    }
}
