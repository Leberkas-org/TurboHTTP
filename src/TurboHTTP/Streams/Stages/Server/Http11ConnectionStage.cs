using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http11ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly Http11ServerEncoderOptions? _encoderOptions;
    private readonly Http11ServerDecoderOptions? _decoderOptions;

    public Http11ConnectionStage(
        Http11ServerEncoderOptions? encoderOptions = null,
        Http11ServerDecoderOptions? decoderOptions = null)
    {
        _encoderOptions = encoderOptions;
        _decoderOptions = decoderOptions;
    }

    public override ConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(ops, _encoderOptions, _decoderOptions));
}