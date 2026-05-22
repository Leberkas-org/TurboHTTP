using TurboHTTP.Client;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Client;

/// <summary>
/// Bidirectional stage that wraps a <see cref="TurboHandler"/> instance,
/// calling <see cref="TurboHandler.ProcessRequest"/> on outbound requests
/// and <see cref="TurboHandler.ProcessResponse"/> on inbound responses.
/// Composes via <c>BidiFlow.Atop</c> alongside built-in feature stages.
/// </summary>
internal sealed class HandlerBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly TurboHandler _handler;

    private readonly Inlet<HttpRequestMessage> _inRequest;
    private readonly Outlet<HttpRequestMessage> _outRequest;
    private readonly Inlet<HttpResponseMessage> _inResponse;
    private readonly Outlet<HttpResponseMessage> _outResponse;

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public HandlerBidiStage(TurboHandler handler, int index)
    {
        _handler = handler;

        var name = handler.GetType().Name;
        var prefix = $"{name}{index}";

        _inRequest = new Inlet<HttpRequestMessage>($"{prefix}.In.Request");
        _outRequest = new Outlet<HttpRequestMessage>($"{prefix}.Out.Request");
        _inResponse = new Inlet<HttpResponseMessage>($"{prefix}.In.Response");
        _outResponse = new Outlet<HttpResponseMessage>($"{prefix}.Out.Response");

        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(HandlerBidiStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    try
                    {
                        Push(stage._outRequest, stage._handler.ProcessRequest(request));
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Handler").Warning(this, "→ ProcessRequest threw: {0}", ex.Message);
                        Push(stage._outRequest, request);
                    }
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("HandlerBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var resp = Grab(stage._inResponse);
                    try
                    {
                        Push(stage._outResponse, stage._handler.ProcessResponse(resp.RequestMessage!, resp));
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Handler").Warning(this, "← ProcessResponse threw: {0}", ex.Message);
                        Push(stage._outResponse, resp);
                    }
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("HandlerBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }
    }
}
