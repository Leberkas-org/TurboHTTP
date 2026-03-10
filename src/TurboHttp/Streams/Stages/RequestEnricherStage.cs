using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

internal sealed class RequestEnricherStage
    : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly Uri? _baseAddress;
    private readonly Version _defaultVersion;
    private readonly HttpRequestHeaders _defaultHeaders;

    private readonly Inlet<HttpRequestMessage> _inlet = new("enricher.in");
    private readonly Outlet<HttpRequestMessage> _outlet = new("enricher.out");

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }

    public RequestEnricherStage(
        Uri? baseAddress,
        Version defaultVersion,
        HttpRequestHeaders defaultHeaders)
    {
        _baseAddress = baseAddress;
        _defaultVersion = defaultVersion;
        _defaultHeaders = defaultHeaders;

        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RequestEnricherStage _stage;

        public Logic(RequestEnricherStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var request = Grab(stage._inlet);

                    try
                    {
                        Enrich(request);
                    }
                    catch (Exception ex)
                    {
                        FailStage(ex);
                        return;
                    }

                    Push(stage._outlet, request);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Enrich(HttpRequestMessage request)
        {
            // Rule 1: URI resolution
            if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
            {
                if (_stage._baseAddress is null)
                {
                    throw new InvalidOperationException("RequestUri is null or relative but no BaseAddress is configured.");
                }

                request.RequestUri = request.RequestUri is null
                    ? _stage._baseAddress
                    : new Uri(_stage._baseAddress, request.RequestUri);
            }

            // Rule 2: Version — only override when request is still at the 1.1 default
            if (request.Version == HttpVersion.Version11 && _stage._defaultVersion != HttpVersion.Version11)
            {
                request.Version = _stage._defaultVersion;
            }

            // Rule 3: Default headers — add those absent from the request
            foreach (var header in _stage._defaultHeaders)
            {
                if (!request.Headers.Contains(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }
    }
}