using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Util;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol;
using TurboHTTP.Server;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class HttpContextBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, TurboHttpContext, TurboHttpContext, HttpResponseMessage>>
{
    private readonly TurboConnectionInfo _connectionInfo;
    private readonly IServiceProvider _services;
    private readonly CancellationToken _connectionAborted;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("HttpContext.In.Request");
    private readonly Outlet<TurboHttpContext> _outRequest = new("HttpContext.Out.Request");
    private readonly Inlet<TurboHttpContext> _inResponse = new("HttpContext.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("HttpContext.Out.Response");

    public override BidiShape<HttpRequestMessage, TurboHttpContext, TurboHttpContext, HttpResponseMessage> Shape
    {
        get;
    }

    public HttpContextBidiStage(
        TurboConnectionInfo connectionInfo,
        IServiceProvider services,
        CancellationToken connectionAborted)
    {
        _connectionInfo = connectionInfo;
        _services = services;
        _connectionAborted = connectionAborted;
        Shape = new BidiShape<HttpRequestMessage, TurboHttpContext, TurboHttpContext, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly HttpContextBidiStage _stage;
        private IMaterializer? _materializer;

        public Logic(HttpContextBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inRequest,
                onPush: OnRequestPush,
                onUpstreamFinish: () => Complete(stage._outRequest));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: OnResponsePush,
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: _ =>
                {
                    if (IsAvailable(stage._outResponse))
                    {
                        Push(stage._outResponse, new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    }

                    CompleteStage();
                });

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._inResponse) && !IsClosed(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        public override void PreStart()
        {
            _materializer = Materializer;
        }

        private void OnRequestPush()
        {
            var request = Grab(_stage._inRequest);
            try
            {
                var ctx = CreateContext(request);
                Push(_stage._outRequest, ctx);
            }
            catch
            {
                Push(_stage._outResponse, new HttpResponseMessage(HttpStatusCode.InternalServerError));
                CompleteStage();
            }
        }

        private void OnResponsePush()
        {
            var ctx = Grab(_stage._inResponse);
            var response = ExtractResponse(ctx);
            Push(_stage._outResponse, response);
        }

        private TurboHttpContext CreateContext(HttpRequestMessage request)
        {
            var bodySource = request.Content is not null
                ? Source.UnfoldResourceAsync(
                    () => request.Content.ReadAsStreamAsync(),
                    async stream =>
                    {
                        var buffer = new byte[16 * 1024];
                        var bytesRead = await stream.ReadAsync(buffer);
                        if (bytesRead == 0)
                        {
                            return Option<ReadOnlyMemory<byte>>.None;
                        }

                        return new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
                    },
                    stream =>
                    {
                        stream.Dispose();
                        return Task.FromResult(Akka.Done.Instance);
                    })
                : Source.Empty<ReadOnlyMemory<byte>>();

            var features = new FeatureCollection();
            var requestFeature = new TurboHttpRequestFeature(request, bodySource);
            features.Set<IHttpRequestFeature>(requestFeature);
            features.Set<ITurboRequestBodyFeature>(requestFeature);
            var responseFeature = new TurboHttpResponseFeature();
            features.Set<IHttpResponseFeature>(responseFeature);
            features.Set<IHttpConnectionFeature>(new TurboHttpConnectionFeature(_stage._connectionInfo));
            var bodyFeature = new TurboHttpResponseBodyFeature();
            features.Set<IHttpResponseBodyFeature>(bodyFeature);
            features.Set<ITurboResponseBodyFeature>(bodyFeature);
            features.Set<IHttpRequestBodyDetectionFeature>(new TurboHttpRequestBodyDetectionFeature(request));

            return new TurboHttpContext(
                features,
                _stage._connectionInfo,
                _stage._services,
                _stage._connectionAborted,
                _materializer!);
        }

        private static HttpResponseMessage ExtractResponse(TurboHttpContext ctx)
        {
            var feature = ctx.Features.Get<IHttpResponseFeature>();
            var statusCode = feature?.StatusCode ?? 200;
            var response = new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                ReasonPhrase = feature?.ReasonPhrase
            };

            if (ctx.Features.Get<ITurboResponseBodyFeature>() is TurboHttpResponseBodyFeature turboBodyFeature)
            {
                turboBodyFeature.Complete();
                response.Content = new StreamContent(turboBodyFeature.GetResponseStream());
                response.Headers.TransferEncodingChunked = true;
            }
            else
            {
                response.Content = new ByteArrayContent([]);
            }

            if (feature?.Headers is null)
            {
                return response;
            }

            foreach (var header in feature.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var values = header.Value.ToArray();
                if (ContentHeaderClassifier.IsContentHeader(header.Key))
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Key, values);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(header.Key, values);
                }
            }

            return response;
        }
    }
}