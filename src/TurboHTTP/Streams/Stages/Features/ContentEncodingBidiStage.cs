using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that handles both request body compression and response body
/// decompression for Content-Encoding (RFC 9110 §8.4).
/// <para>
/// <b>Request direction (In1→Out1):</b> When a <see cref="CompressionPolicy"/> is
/// provided, requests with a body at or above the threshold are compressed. Otherwise
/// requests pass through unchanged.
/// </para>
/// <para>
/// <b>Response direction (In2→Out2):</b> When a <see cref="bool"/> is
/// true, responses with a Content-Encoding header (gzip, x-gzip, deflate, br) are
/// decompressed, the header is removed, and Content-Length is updated. Otherwise responses
/// pass through unchanged.
/// </para>
/// </summary>
internal sealed class ContentEncodingBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly bool _automaticDecompression;
    private readonly CompressionPolicy? _compressionPolicy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("ContentEncoding.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ContentEncoding.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("ContentEncoding.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ContentEncoding.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    public ContentEncodingBidiStage(
        bool automaticDecompression = true,
        CompressionPolicy? compressionPolicy = null)
    {
        _automaticDecompression = automaticDecompression;
        _compressionPolicy = compressionPolicy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new ContentEncodingBidiLogic(this);

    internal sealed class ContentEncodingBidiLogic : GraphStageLogic, IFeatureStageOperations
    {
        private readonly ContentEncodingBidiStage _stage;
        private readonly ContentEncodingBidiProcessor _processor;

        public ContentEncodingBidiLogic(ContentEncodingBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _processor =
                new ContentEncodingBidiProcessor(this, stage._compressionPolicy, stage._automaticDecompression);

            if (stage._compressionPolicy is not null)
            {
                SetHandler(stage._inRequest,
                    onPush: () =>
                    {
                        var request = Grab(stage._inRequest);
                        try
                        {
                            _processor.OnRequestPushWithCompression(request);
                        }
                        catch (Exception ex)
                        {
                            Tracing.For("ContentEncoding").Warning(this, "→ compression failed: {0}", ex.Message);
                            Push(stage._outRequest, request);
                        }
                    },
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });
            }
            else
            {
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });
            }

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            if (stage._automaticDecompression)
            {
                SetHandler(stage._inResponse,
                    onPush: () =>
                    {
                        var response = Grab(stage._inResponse);
                        try
                        {
                            _processor.OnResponsePushWithDecompression(response);
                        }
                        catch (Exception ex)
                        {
                            Tracing.For("ContentEncoding").Warning(this, "← decompression failed: {0}", ex.Message);
                            Push(stage._outResponse, response);
                        }
                    },
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });
            }
            else
            {
                SetHandler(stage._inResponse,
                    onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });
            }

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        void IFeatureStageOperations.OnPushRequest(HttpRequestMessage request)
        {
            Push(_stage._outRequest, request);
        }

        void IFeatureStageOperations.OnPushResponse(HttpResponseMessage response)
        {
            Push(_stage._outResponse, response);
        }

        void IFeatureStageOperations.OnSignalPullRequest()
        {
        }

        void IFeatureStageOperations.OnSignalPullResponse()
        {
        }

        void IFeatureStageOperations.OnCompleteStage()
        {
        }

        void IFeatureStageOperations.OnScheduleTimer(string key, TimeSpan delay)
        {
        }

        void IFeatureStageOperations.OnCancelTimer(string key)
        {
        }

        ILoggingAdapter IFeatureStageOperations.Log => Log;
    }
}

internal sealed class ContentEncodingBidiProcessor
{
    private readonly IFeatureStageOperations _ops;
    private readonly CompressionPolicy? _compressionPolicy;
    private readonly bool _automaticDecompression;

    public ContentEncodingBidiProcessor(
        IFeatureStageOperations ops,
        CompressionPolicy? compressionPolicy,
        bool automaticDecompression)
    {
        _ops = ops;
        _compressionPolicy = compressionPolicy;
        _automaticDecompression = automaticDecompression;
    }

    public void OnRequestPushWithCompression(HttpRequestMessage request)
    {
        _ops.OnPushRequest(CompressIfNeeded(request, _compressionPolicy!));
    }

    public void OnResponsePushWithDecompression(HttpResponseMessage response)
    {
        _ops.OnPushResponse(Decompress(response));
    }

    private HttpRequestMessage CompressIfNeeded(HttpRequestMessage request, CompressionPolicy policy)
    {
        if (request.Content is null)
        {
            Tracing.For("ContentEncoding").Debug(this, "→ skip compression: no body");
            return request;
        }

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        if (bodySize < policy.MinBodySizeBytes)
        {
            Tracing.For("ContentEncoding").Debug(this, "→ skip compression: body size {0} < threshold {1}", bodySize, policy.MinBodySizeBytes);
            return request;
        }

        Tracing.For("ContentEncoding").Debug(this, "→ compressing request body ({0} bytes, {1})", bodySize, policy.Encoding);
        request.Content = new CompressingContent(request.Content, policy.Encoding);
        return request;
    }

    private HttpResponseMessage Decompress(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Content-Encoding", out var values))
        {
            return response;
        }

        var encoding = string.Join(", ", values).Trim();

        if (string.IsNullOrEmpty(encoding) ||
            encoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        if (!ContentEncoding.IsSupported(encoding))
        {
            Tracing.For("ContentEncoding").Info(this, "← unsupported encoding '{0}', passing through", encoding);
            return response;
        }

        Tracing.For("ContentEncoding").Debug(this, "← decompressing response body ({0})", encoding);
        var newContent = new DecompressingContent(response.Content, encoding);

        foreach (var header in response.Content.Headers)
        {
            if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = newContent;
        return response;
    }
}