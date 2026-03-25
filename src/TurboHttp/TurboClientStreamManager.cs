using System;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;
using TurboHttp.Transport;

namespace TurboHttp;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// </summary>
internal sealed class TurboClientStreamManager : IDisposable
{
    private readonly ConnectionPool _pool;
    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>
    /// Exposes the response-channel writer so tests can inject synthetic responses
    /// without requiring a live TCP connection.
    /// </summary>
    internal ChannelWriter<HttpResponseMessage> ResponseWriter { get; private set; } = null!;

    public TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system)
        : this(clientOptions, requestOptionsFactory, system, PipelineDescriptor.Empty)
    {
    }

    internal TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system, PipelineDescriptor descriptor)
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
        ResponseWriter = responseWriter;
        var requestReader = requestsChannel.Reader;

        // Create ConnectionPool — manages per-host connections with idle eviction.
        var pool = new ConnectionPool(clientOptions.IdleTimeout);
        _pool = pool;

        // Build the full pipeline flow from Engine using the provided descriptor.
        var engine = new Engine();
        var engineFlow = engine.CreateFlow(pool, clientOptions, requestOptionsFactory, descriptor);


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

        // Pump requests from the channel reader into the Akka.Streams queue.
        // Attach a fault continuation so an unexpected exception in the pump
        // is observed (preventing UnobservedTaskException) and logged.
        var log = system.Log;
        var pumpTask = PumpRequestsAsync(requestReader, queue);
        _ = pumpTask.ContinueWith(
            t => log.Error(t.Exception, "TurboClientStreamManager: request pump faulted unexpectedly"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    public void Dispose() => _pool.Dispose();

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