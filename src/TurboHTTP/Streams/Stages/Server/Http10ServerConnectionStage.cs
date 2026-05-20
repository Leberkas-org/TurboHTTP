using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http10ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http10Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http10Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http10Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http10ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http10ServerStateMachine>(this,
            ops => new Http10ServerStateMachine(_options, ops));
}
