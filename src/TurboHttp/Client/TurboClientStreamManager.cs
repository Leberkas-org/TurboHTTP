using System;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka;
using TurboHttp.Pooling;
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

        // Create PoolRouter — supervises the actor-based connection pool hierarchy.
        // PoolRouter → HostPool → ConnectionActor → TCP
        var poolRouter = system.ResolveActor<PoolRouter>($"pool-router-{streamManagerId}", clientOptions);

        // Build the full pipeline flow from Engine using the provided descriptor.
        var engine = new Engine();
        var engineFlow = engine.CreateFlow(poolRouter, clientOptions, requestOptionsFactory, descriptor);


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
                File.AppendAllText(@"D:\GIT\Akka.Streams.Http\diag.log",
                    $"[DIAG-MGR] SinkTask FAULTED: {task.Exception}\n");
                responseWriter.Complete(task.Exception);
            }
            else
            {
                File.AppendAllText(@"D:\GIT\Akka.Streams.Http\diag.log",
                    "[DIAG-MGR] SinkTask completed normally\n");
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

    private static async Task PumpRequestsAsync(ChannelReader<HttpRequestMessage> reader,
        ISourceQueueWithComplete<HttpRequestMessage> queue)
    {
        try
        {
            await foreach (var request in reader.ReadAllAsync())
            {
                File.AppendAllText(@"D:\GIT\Akka.Streams.Http\diag.log",
                    $"[DIAG-MGR] Offering request: {request.Method} {request.RequestUri} v{request.Version}\n");
                var result = await queue.OfferAsync(request);
                File.AppendAllText(@"D:\GIT\Akka.Streams.Http\diag.log",
                    $"[DIAG-MGR] OfferAsync result: {result}\n");
            }
        }

        finally
        {
            queue.Complete();
        }
    }
}