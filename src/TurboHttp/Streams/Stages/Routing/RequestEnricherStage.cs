using System;
using System.Net;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Client;

namespace TurboHttp.Streams.Stages.Routing;

internal sealed class RequestEnricherStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly Func<TurboRequestOptions> _optionsFactory;

    private readonly Inlet<HttpRequestMessage> _in = new("RequestEnricher.In");
    private readonly Outlet<HttpRequestMessage> _out = new("RequestEnricher.Out");

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }


    public RequestEnricherStage(Func<TurboRequestOptions> optionsFactory)
    {
        _optionsFactory = optionsFactory;
        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RequestEnricherStage _stage;

        public Logic(RequestEnricherStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    try
                    {
                        Enrich(request);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("RequestEnricherStage: Enrichment failed for [{0}]: {1}",
                            request.RequestUri, ex.Message);
                    }

                    Push(stage._out, request);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("RequestEnricherStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Enrich(HttpRequestMessage request)
        {
            var options = _stage._optionsFactory.Invoke();

            // Rule 1: URI resolution
            if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
            {
                var baseAddress = options.BaseAddress;
                if (baseAddress is null)
                {
                    throw new InvalidOperationException(
                        "RequestUri is null or relative but no BaseAddress is configured.");
                }

                request.RequestUri = request.RequestUri is null
                    ? baseAddress
                    : new Uri(baseAddress, request.RequestUri);
            }

            // Rule 2: Version — only override when request is still at the 1.1 default
            if (request.Version == HttpVersion.Version11 && options.DefaultRequestVersion != HttpVersion.Version11)
            {
                request.Version = options.DefaultRequestVersion;
            }

            // Rule 3: Default headers — add those absent from the request
            foreach (var header in options.DefaultRequestHeaders)
            {
                if (!request.Headers.Contains(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }
    }
}