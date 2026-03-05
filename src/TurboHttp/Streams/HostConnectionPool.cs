using System;
using System.Net.Http;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka;
using TurboHttp.IO;

namespace TurboHttp.Streams;

internal interface IHostConnectionPool
{
    void Send(HttpRequestMessage request);
}

public sealed class HostConnectionPool : IHostConnectionPool
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