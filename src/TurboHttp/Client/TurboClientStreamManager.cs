using System;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.Streams;

namespace TurboHttp.Client;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// </summary>
internal sealed class TurboClientStreamManager
{
    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    public TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system)
    {
        var streamManagerId = Guid.NewGuid();
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;
        var responseWriter = responsesChannel.Writer;
        var requestReader = requestsChannel.Reader;

        // Create PoolRouterActor — supervises the actor-based connection pool hierarchy.
        // PoolRouterActor → HostPoolActor → ConnectionActor → TCP
        var poolRouter = system.ActorOf(
            Props.Create(() => new PoolRouterActor(clientOptions)),
            $"pool-router-{streamManagerId}");

        // Build the full pipeline flow from Engine.
        // Engine.CreateFlow internally creates per-client instances:
        //   - CookieJar (one per client, thread-safe) when EnableCookies is set
        //   - HttpCacheStore (one per client, thread-safe LRU) when EnableCaching is set
        //   - RedirectHandler (one per pipeline, stateful redirect count) when EnableRedirectHandling is set
        //   - Stages for retry, decompression, cookie injection/storage, cache lookup/storage
        var engine = new Engine();
        var engineFlow = engine.CreateFlow(poolRouter, clientOptions, requestOptionsFactory);


        var sink = Sink.ForEachAsync<HttpResponseMessage>(1, async r => await responseWriter.WriteAsync(r));
        // Materialise the graph:
        //   Source.Queue → Engine flow → Sink.ForEach (writes to response channel)
        var materializerSettings = ActorMaterializerSettings.Create(system)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = system.Materializer(
            settings: materializerSettings,
            namePrefix: $"stream-manager-{streamManagerId}");

        var (queue, sinkTask) = Source.Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(engineFlow)
            .ToMaterialized(sink, Keep.Both)
            .Run(materializer);

        _ = sinkTask!.ContinueWith(task =>
        {
            if (task.Exception is not null)
            {
                responseWriter.Complete(task.Exception);
            }
            else
            {
                responseWriter.Complete();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);

        // Pump requests from the channel reader into the Akka.Streams queue
        _ = PumpRequestsAsync(requestReader, queue);
    }

    private static async Task PumpRequestsAsync(ChannelReader<HttpRequestMessage> reader,
        ISourceQueueWithComplete<HttpRequestMessage> queue)
    {
        try
        {
            await foreach (var request in reader.ReadAllAsync())
            {
                await queue.OfferAsync(request);
            }
        }

        finally
        {
            queue.Complete();
        }
    }
}