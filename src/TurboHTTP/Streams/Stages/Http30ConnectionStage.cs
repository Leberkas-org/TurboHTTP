using TurboHTTP.Client;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Client;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http30Connection.In.Network");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http30Connection.In.Request");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http30ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionStageLogic<Http3ClientStateMachine>(
            this,
            ops => new Http3ClientStateMachine(_options, ops));
}