using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http20ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<TurboHttpContext> _outRequest = new("Http20Connection.Out.Request");
    private readonly Inlet<TurboHttpContext> _inResponse = new("Http20Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly TurboServerOptions _options;
    private readonly IServiceProvider? _services;
    private readonly TurboConnectionInfo? _connectionInfo;

    public Http20ServerConnectionStage(TurboServerOptions options, IServiceProvider? services = null, TurboConnectionInfo? connectionInfo = null)
    {
        _options = options;
        _services = services;
        _connectionInfo = connectionInfo;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http2ServerStateMachine>(this,
            ops => new Http2ServerStateMachine(_options, ops),
            _services,
            _connectionInfo);
}
