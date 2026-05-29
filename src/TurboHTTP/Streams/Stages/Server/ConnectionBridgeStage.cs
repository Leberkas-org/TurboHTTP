using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal static class ConnectionBridge
{
    public static IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> Create(
        int connectionId,
        Sink<IFeatureCollection, NotUsed> requestIngress,
        Source<IFeatureCollection, NotUsed> responseFanoutSource)
    {
        return GraphDsl.Create(b =>
        {
            var tagAndSink = b.Add(
                Flow.Create<IFeatureCollection>()
                    .Select(features =>
                    {
                        features.Set(new ConnectionRoutingFeature { ConnectionId = connectionId });
                        return features;
                    })
                    .To(requestIngress));

            var responseSource = b.Add(responseFanoutSource);

            return new FlowShape<IFeatureCollection, IFeatureCollection>(
                tagAndSink.Inlet,
                responseSource.Outlet);
        });
    }
}
