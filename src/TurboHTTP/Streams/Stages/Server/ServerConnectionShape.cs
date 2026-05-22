using System.Collections.Immutable;
using Akka.Streams;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ServerConnectionShape : Shape
{
    public Inlet<ITransportInbound> InNetwork { get; }
    public Outlet<TurboHttpContext> OutRequest { get; }
    public Inlet<TurboHttpContext> InResponse { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ServerConnectionShape(
        Inlet<ITransportInbound> inNetwork,
        Outlet<TurboHttpContext> outResponse,
        Inlet<TurboHttpContext> inRequest,
        Outlet<ITransportOutbound> outNetwork)
    {
        InNetwork = inNetwork;
        OutRequest = outResponse;
        InResponse = inRequest;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InNetwork, InResponse];

    public override ImmutableArray<Outlet> Outlets => [OutRequest, OutNetwork];

    public override Shape DeepCopy()
    {
        return new ServerConnectionShape(
            (Inlet<ITransportInbound>)InNetwork.CarbonCopy(),
            (Outlet<TurboHttpContext>)OutRequest.CarbonCopy(),
            (Inlet<TurboHttpContext>)InResponse.CarbonCopy(),
            (Outlet<ITransportOutbound>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ServerConnectionShape(
            (Inlet<ITransportInbound>)inlets[0],
            (Outlet<TurboHttpContext>)outlets[0],
            (Inlet<TurboHttpContext>)inlets[1],
            (Outlet<ITransportOutbound>)outlets[1]);
    }
}

