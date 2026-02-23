using System;
using System.Net.Http;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka;
using Servus.Akka.IO;

namespace TurboHttp.Streams;

public sealed class HostConnectionPool
{
    private readonly ActorSystem _system;
    private readonly ISourceQueueWithComplete<HttpRequestMessage> _queue;

    public HostConnectionPool(TcpOptions options, ActorSystem system, Action<HttpResponseMessage> onResponse)
    {
        _system = system;
        _queue = BuildConnectionStream(options, system.GetActor<ClientManager>(), onResponse);
    }

    public void Send(HttpRequestMessage request)
    {
        _queue.OfferAsync(request);
    }

    private ISourceQueueWithComplete<HttpRequestMessage> BuildConnectionStream(TcpOptions options,
        IActorRef clientManager, Action<HttpResponseMessage> onResponse)
    {
        var flow = new Engine().CreateFlow(clientManager, options);

        return Source
            .Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach(onResponse),
                Keep.Left)
            .Run(_system.Materializer());
    }
}