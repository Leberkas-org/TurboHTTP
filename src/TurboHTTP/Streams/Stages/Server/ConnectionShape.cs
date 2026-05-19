using System.Collections.Immutable;
using Akka.Streams;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ConnectionShape : Shape
{
    public Inlet<ITransportInbound> InNetwork { get; }
    public Outlet<HttpRequestMessage> OutRequest { get; }
    public Inlet<HttpResponseMessage> InResponse { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ConnectionShape(
        Inlet<ITransportInbound> inNetwork,
        Outlet<HttpRequestMessage> outResponse,
        Inlet<HttpResponseMessage> inRequest,
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
        return new ConnectionShape(
            (Inlet<ITransportInbound>)InNetwork.CarbonCopy(),
            (Outlet<HttpRequestMessage>)OutRequest.CarbonCopy(),
            (Inlet<HttpResponseMessage>)InResponse.CarbonCopy(),
            (Outlet<ITransportOutbound>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)inlets[0],
            (Outlet<HttpRequestMessage>)outlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Outlet<ITransportOutbound>)outlets[1]);
    }
}

