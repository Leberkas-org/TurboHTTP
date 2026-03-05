using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Client;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// </summary>
public sealed class TurboClientStreamManager
{
    private readonly HttpRequestMessage _defaultHeadersHolder;
    private readonly HostRoutingStage _hostRoutingStage;

    public ChannelWriter<HttpRequestMessage> Requests { get; }
    public ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>
    /// Exposed so tests can inject a fake pool factory before the first element flows.
    /// Must be set immediately after construction and before the first
    /// <see cref="Requests"/> write.
    /// </summary>
    internal HostRoutingStage HostRoutingStage => _hostRoutingStage;

    public TurboClientStreamManager(
        TurboClientOptions options,
        ActorSystem system,
        HttpRequestHeaders? defaultHeaders = null)
    {
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>();

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;

        _defaultHeadersHolder = new HttpRequestMessage();
        var headers = defaultHeaders ?? _defaultHeadersHolder.Headers;

        _hostRoutingStage = new HostRoutingStage(options);

        ChannelSource
            .FromReader(requestsChannel.Reader)
            .Via(Flow.FromGraph(new RequestEnricherStage(
                options.BaseAddress,
                options.DefaultRequestVersion,
                headers)))
            .Via(Flow.FromGraph(_hostRoutingStage))
            .RunWith(
                Sink.ForEach<HttpResponseMessage>(r =>
                    responsesChannel.Writer.TryWrite(r)),
                system.Materializer());
    }
}