using System;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;

namespace TurboHttp.Client;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// </summary>
internal sealed class TurboClientStreamManager
{
    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    public TurboClientStreamManager(Func<TurboRequestOptions> requestOptionsFactory, ActorSystem system)
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

        // ChannelSource
        //     .FromReader(requestsChannel.Reader)
        //     .Via(Flow.FromGraph(new RequestEnricherStage(
        //         options.BaseAddress,
        //         options.DefaultRequestVersion,
        //         headers)))
        //     .Via(Flow.FromGraph(_hostRoutingStage))
        //     .RunWith(
        //         Sink.ForEach<HttpResponseMessage>(r =>
        //             responsesChannel.Writer.TryWrite(r)),
        //         system.Materializer());
    }
}

internal sealed class StreamInstance : ReceiveActor
{
    public StreamInstance()
    {
        
    }
}