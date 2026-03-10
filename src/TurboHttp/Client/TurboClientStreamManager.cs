using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;

namespace TurboHttp.Client;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// </summary>
public sealed class TurboClientStreamManager
{
    private readonly HttpRequestMessage _defaultHeadersHolder;

    public ChannelWriter<HttpRequestMessage> Requests { get; }
    public ChannelReader<HttpResponseMessage> Responses { get; }

    public TurboClientStreamManager(TurboClientOptions options, ActorSystem system,
        HttpRequestHeaders? defaultHeaders = null)
    {
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>();

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;

        _defaultHeadersHolder = new HttpRequestMessage();
        var headers = defaultHeaders ?? _defaultHeadersHolder.Headers;

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