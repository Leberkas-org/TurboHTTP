using System;
using System.Net;
using System.Net.Http;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef poolRouter,
        TurboClientOptions? options,
        Func<TurboRequestOptions>? requestOptionsFactory,
        PipelineDescriptor descriptor)
    {
        options ??= new TurboClientOptions();
        var requestOptions = BuildRequestOptions(options);
        requestOptionsFactory ??= () => requestOptions;

        return BuildExtendedPipeline(poolRouter, options, requestOptionsFactory, descriptor);
    }

    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http10Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http11Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http20Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>> http30Factory,
        PipelineDescriptor descriptor)
    {
        var holder = new HttpRequestMessage();
        var defaultOptions = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: holder.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);

        return BuildExtendedPipeline(ActorRefs.Nobody, new TurboClientOptions(), () => defaultOptions,
            descriptor,
            http10Factory, http11Factory, http20Factory);
    }

    private static TurboRequestOptions BuildRequestOptions(TurboClientOptions options)
    {
        var holder = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestVersion: holder.Version,
            DefaultRequestHeaders: holder.Headers,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);
    }

    /// <summary>
    /// Composes the pipeline by stacking feature and middleware BidiFlows via <c>Atop</c>
    /// around the protocol engine core, with <see cref="RequestEnricherStage"/> prepended
    /// outside the BidiFlow chain.
    /// <para><b>Stacking order (outermost → innermost):</b></para>
    /// <list type="number">
    ///   <item><description>User Middlewares — MiddlewareBidiStage per TurboMiddleware (FIFO: [0] outermost)</description></item>
    ///   <item><description>RedirectBidiStage — RFC 9110 §15.4, internal feedback loop</description></item>
    ///   <item><description>CookieBidiStage — RFC 6265 §5.3–§5.4</description></item>
    ///   <item><description>RetryBidiStage — RFC 9110 §9.2, internal feedback loop</description></item>
    ///   <item><description>CacheBidiStage — RFC 9111, internal short-circuit</description></item>
    ///   <item><description>DecompressionBidiStage — RFC 9110 §8.4</description></item>
    /// </list>
    /// <para>Request direction: Middleware[0] → … → Middleware[N] → Redirect → Cookie → Retry → Cache → Decomp → Engine</para>
    /// <para>Response direction: Engine → Decomp → Cache → Retry → Cookie → Redirect → Middleware[N] → … → Middleware[0]</para>
    /// <para>Only BidiFlows for non-null policies are included. When all policies are null,
    /// no middlewares exist, and <see cref="PipelineDescriptor.AutomaticDecompression"/> is true,
    /// the graph is: Enricher → DecompressionBidi(Engine) → Output.</para>
    /// </summary>
    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildExtendedPipeline(
        IActorRef poolRouter,
        TurboClientOptions options,
        Func<TurboRequestOptions> requestOptionsFactory,
        PipelineDescriptor descriptor,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory = null,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory = null)
    {
        // Protocol engine core (version demux + encode/decode) with async boundary.
        var engineFlow = Flow.FromGraph(
                ProtocolCoreGraphBuilder.Build(poolRouter, options,
                    http10Factory, http11Factory, http20Factory))
            .WithAttributes(Attributes.CreateAsyncBoundary());

        // Build feature BidiFlow chain via conditional Atop stacking.
        // Build from innermost to outermost so that each new layer wraps the previous.
        BidiFlow<HttpRequestMessage, HttpRequestMessage,
            HttpResponseMessage, HttpResponseMessage, NotUsed>? features = null;

        if (descriptor.AutomaticDecompression)
        {
            features = BidiFlow.FromGraph(new DecompressionBidiStage());
        }

        if (descriptor.CacheStore is not null)
        {
            var cache = BidiFlow.FromGraph(new CacheBidiStage(descriptor.CacheStore, descriptor.CachePolicy));
            features = features is not null ? cache.Atop(features) : cache;
        }

        if (descriptor.RetryPolicy is not null)
        {
            var retry = BidiFlow.FromGraph(new RetryBidiStage(descriptor.RetryPolicy));
            features = features is not null ? retry.Atop(features) : retry;
        }

        if (descriptor.CookieJar is not null)
        {
            var cookie = BidiFlow.FromGraph(new CookieBidiStage(descriptor.CookieJar));
            features = features is not null ? cookie.Atop(features) : cookie;
        }

        if (descriptor.RedirectPolicy is not null)
        {
            var redirect = BidiFlow.FromGraph(new RedirectBidiStage(descriptor.RedirectPolicy));
            features = features is not null ? redirect.Atop(features) : redirect;
        }

        // Stack user middlewares outermost via Atop. Iterate in reverse so that
        // Middlewares[0] ends up outermost (sees initial request first, final response last).
        for (var i = descriptor.Middlewares.Count - 1; i >= 0; i--)
        {
            var mw = BidiFlow.FromGraph(new HandlerBidiStage(descriptor.Middlewares[i], i));
            features = features is not null ? mw.Atop(features) : mw;
        }

        // Join features with engine, or use engine directly if no features.
        var pipelineFlow = features is not null
            ? features.Join(engineFlow)
            : engineFlow;

        // Prepend enricher (initial requests only — redirects/retries are handled
        // internally by their BidiStages and bypass the enricher).
        var requestPrep = Flow.Create<HttpRequestMessage>()
            .Via(Flow.FromGraph(new RequestEnricherStage(requestOptionsFactory)));

        return requestPrep.Via(pipelineFlow);
    }
}
