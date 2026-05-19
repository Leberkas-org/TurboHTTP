using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http10ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http10Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http10Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http10Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");

    public override ConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http10ServerStateMachine>(this,
            ops => new Http10ServerStateMachine(ops));
}