using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http11ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<TurboHttpContext> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<TurboHttpContext> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http11ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(_options, ops));
}
