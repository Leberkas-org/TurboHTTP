using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ProtocolNegotiatorConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("NegotiatorConnection.In.Network");
    private readonly Outlet<RequestContext> _outRequest = new("NegotiatorConnection.Out.Request");
    private readonly Inlet<RequestContext> _inResponse = new("NegotiatorConnection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("NegotiatorConnection.Out.Network");
    private readonly TurboServerOptions _options;
    private readonly IServiceProvider? _services;

    public ProtocolNegotiatorConnectionStage(TurboServerOptions options, IServiceProvider? services = null)
    {
        _options = options;
        _services = services;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<ProtocolNegotiatingStateMachine>(this,
            ops => new ProtocolNegotiatingStateMachine(_options, ops),
            _services);
}
