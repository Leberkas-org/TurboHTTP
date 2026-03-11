using System;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.Protocol;
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

    public TurboClientStreamManager(
        TurboClientOptions clientOptions,
        Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system)
    {
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

        // Create ClientManager actor for TCP connection lifecycle
        var clientManager = system.ActorOf(
            Props.Create<ClientManager>(),
            $"client-manager-{Guid.NewGuid()}");

        // Build the full pipeline flow from Engine.
        // Engine.CreateFlow internally creates per-client instances:
        //   - CookieJar (one per client, thread-safe) when EnableCookies is set
        //   - HttpCacheStore (one per client, thread-safe LRU) when EnableCaching is set
        //   - RedirectHandler (one per pipeline, stateful redirect count) when EnableRedirectHandling is set
        //   - Stages for retry, decompression, cookie injection/storage, cache lookup/storage
        var engine = new Engine();
        var engineFlow = engine.CreateFlow(clientManager, clientOptions);

        // Materialise the graph:
        //   Source.Queue → Engine flow → Sink.ForEach (writes to response channel)
        var queue = Source.Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(engineFlow)
            .To(Sink.ForEach<HttpResponseMessage>(r => responsesChannel.Writer.TryWrite(r)))
            .Run(system.Materializer());

        // Pump requests from the channel reader into the Akka.Streams queue
        _ = PumpRequestsAsync(requestsChannel.Reader, queue, responsesChannel.Writer);
    }

    private static async Task PumpRequestsAsync(
        ChannelReader<HttpRequestMessage> reader,
        ISourceQueueWithComplete<HttpRequestMessage> queue,
        ChannelWriter<HttpResponseMessage> responseWriter)
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
            responseWriter.TryComplete();
        }
    }
}
