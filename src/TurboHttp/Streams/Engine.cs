using System;
using System.Net;
using System.Net.Http;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Streams;

internal sealed class Engine
{
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef poolRouter,
        TurboClientOptions? options,
        Func<TurboRequestOptions>? requestOptionsFactory)
    {
        options ??= new TurboClientOptions();
        var requestOptions = BuildRequestOptions(options);
        requestOptionsFactory ??= () => requestOptions;

#pragma warning disable CS0618 // TurboClientOptions.RedirectPolicy/RetryPolicy/CachePolicy obsolete — backward-compat read
        var descriptor = new PipelineDescriptor(
            options.RedirectPolicy,
            options.RetryPolicy,
            new CookieJar(),
            new HttpCacheStore(options.CachePolicy),
            options.CachePolicy,
            []);
#pragma warning restore CS0618

        return BuildExtendedPipeline(poolRouter, options, requestOptionsFactory, descriptor);
    }

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
        TurboClientOptions? options = null)
    {
        options ??= new TurboClientOptions();

        var holder = new HttpRequestMessage();
        var defaultOptions = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: holder.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize: 1024 * 1024);

        return BuildExtendedPipeline(ActorRefs.Nobody, options, () => defaultOptions,
            PipelineDescriptor.Empty,
            http10Factory, http11Factory, http20Factory);
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
    /// Orchestrates the three-island pipeline that processes HTTP requests end-to-end.
    /// Delegates construction of each island to its dedicated builder and wires their
    /// outputs together with async boundaries and feedback loops.
    /// <para><b>Island 1 (pre-processing):</b> <see cref="PreProcessingGraphBuilder"/></para>
    /// <para><b>Island 2 (protocol engine):</b> <see cref="ProtocolCoreGraphBuilder"/></para>
    /// <para><b>Island 3 (post-processing):</b> <see cref="PostProcessingGraphBuilder"/></para>
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
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            // ---- PRE-PROCESSING ISLAND (fused island 1: lightweight request stages) ----
            var preProcess = builder.Add(
                PreProcessingGraphBuilder.Build(descriptor, requestOptionsFactory));

            // ---- PROTOCOL ENGINE ISLAND (fused island 2: CPU-intensive encode/decode + decompression) ----
            // Async boundary separates this from the lightweight pre/post-processing stages,
            // allowing protocol work to run in parallel on a separate thread.
            var engineCore = builder.Add(
                Flow.FromGraph(
                        ProtocolCoreGraphBuilder.Build(poolRouter, options,
                            http10Factory, http11Factory, http20Factory))
                    .WithAttributes(Attributes.CreateAsyncBoundary()));

            builder.From(preProcess.CacheMissOut).To(engineCore.Inlet);

            // ---- POST-PROCESSING ISLAND (fused island 3: response evaluation stages) ----
            // Async boundary separates this from the protocol engine island.
            var postProcess = builder.Add(
                PostProcessingGraphBuilder.Build(descriptor)
                    .Async());

            builder.From(engineCore.Outlet).To(postProcess.ResponseIn);
            builder.From(preProcess.CacheHitOut).To(postProcess.CacheHitIn);

            // Feedback loops: cross from post-processing island back to pre-processing island.
            // Buffer(4) breaks the cycle and allows multiple in-flight redirects/retries
            // without back-pressuring the main pipeline. MergePreferred ensures feedback
            // items are always processed before new requests from the source.
            builder.From(postProcess.RetryFeedbackOut)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
                .To(preProcess.RetryFeedbackIn);

            builder.From(postProcess.RedirectFeedbackOut)
                .Via(Flow.Create<HttpRequestMessage>().Buffer(4, OverflowStrategy.Backpressure))
                .To(preProcess.RedirectFeedbackIn);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                preProcess.RequestIn,
                postProcess.ResponseOut
            );
        }));
    }
}
