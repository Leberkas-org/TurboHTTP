using System;
using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Engine
{
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef clientManager,
        TcpOptions options)
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(4, msg => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            }));
            var hub = builder.Add(new Merge<HttpResponseMessage>(4));

            var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(4, clientManager, options));
            var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(4, clientManager, options));
            var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, clientManager, options));
            var http30 = builder.Add(BuildProtocolFlow<Http30Engine>(1, clientManager, options));

            builder.From(partition.Out(0)).Via(http10).To(hub);
            builder.From(partition.Out(1)).Via(http11).To(hub);
            builder.From(partition.Out(2)).Via(http20).To(hub);
            builder.From(partition.Out(3)).Via(http30).To(hub);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
        }));
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildProtocolFlow<TEngine>(
        int connectionCount,
        IActorRef clientManager,
        TcpOptions options,
        Func<Flow<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>>? transportFactory = null)
        where TEngine : IHttpProtocolEngine, new()
    {
        return GraphDsl.Create(builder =>
        {
            var balance = builder.Add(new Balance<HttpRequestMessage>(connectionCount));
            var merge = builder.Add(new Merge<HttpResponseMessage>(connectionCount));

            for (var i = 0; i < connectionCount; i++)
            {
                var tcp = transportFactory?.Invoke() ?? Flow.FromGraph(new ConnectionStage(clientManager, options));
                var conn = builder.Add(new TEngine().CreateFlow().Join(tcp));
                builder.From(balance.Out(i)).Via(conn).To(merge.In(i));
            }

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(balance.In, merge.Out);
        });
    }
}