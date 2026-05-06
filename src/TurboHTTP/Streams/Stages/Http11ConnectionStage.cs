using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http11ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http11Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http11Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http11Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http11ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        var memoryBuffer = inheritedAttributes.GetAttribute(
            new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));

        return new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(ops, _options, memoryBuffer.Initial, memoryBuffer.Max));
    }
}
