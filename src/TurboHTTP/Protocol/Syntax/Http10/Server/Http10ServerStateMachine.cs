using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;
using HttpVersion = System.Net.HttpVersion;


namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http10ServerDecoder _decoder;
    private readonly Http10ServerEncoder _encoder;
    private readonly long _maxRequestBodySize;
    private readonly TurboServerOptions _serverOptions;

    private IFeatureCollection? _deferredFeatures;
    private IMemoryOwner<byte>? _deferredBodyOwner;
    private int _deferredBodyLength;
    private IBodyEncoder? _activeBodyEncoder;

    public bool CanAcceptResponse => true;
    public bool ShouldComplete { get; private set; }
    public int MaxQueuedRequests => 1;

    public Http10ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        _serverOptions = options;
        _maxRequestBodySize = options.Http1.MaxRequestBodySize;

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http1.MaxRequestBodySize,
            MaxHeaderBytes = options.Http1.MaxHeaderListSize,
            HeaderLineMaxLength = options.Http1.MaxRequestLineLength,
            RequestLineMaxLength = options.Http1.MaxRequestLineLength,
        };

        var decoderOpts = new Http10ServerDecoderOptions { Shared = shared };
        var encoderOpts = new Http10ServerEncoderOptions { Shared = shared };

        _decoder = new Http10ServerDecoder(decoderOpts);
        _encoder = new Http10ServerEncoder(encoderOpts);
    }

    public void PreStart()
    {
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        try
        {
            if (ShouldComplete)
            {
                return;
            }

            var outcome = _decoder.Feed(buffer.Memory.Span, out _);

            if (outcome == DecodeOutcome.Complete)
            {
                ShouldComplete = true;
                var feature = _decoder.GetRequestFeature();
                var hasBody = feature.Body != Stream.Null;
                var features = FeatureCollectionFactory.Create(feature, hasBody, _ops.Services, _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _serverOptions.Limits.MaxRequestBodySize);
                _ops.OnRequest(features);
            }
        }
        catch (Exception)
        {
            ShouldComplete = true;
        }
        finally
        {
            buffer.Dispose();
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        _deferredFeatures = features;

        var responseBody = features.Get<IHttpResponseBodyFeature>();
        if (responseBody is TurboHttpResponseBodyFeature turboBody)
        {
            var bodyStream = turboBody.GetResponseStream();
            var encoder = BodyEncoderFactory.Create(bodyStream, null, HttpVersion.Version10);
            if (encoder is not null)
            {
                _activeBodyEncoder = encoder;
                encoder.Start(bodyStream, _ops.StageActor);
            }
        }
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case OutboundBodyChunk chunk when _deferredFeatures is not null:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = chunk.Owner;
                _deferredBodyLength = chunk.Length;
                break;

            case OutboundBodyComplete when _deferredFeatures is not null && _deferredBodyOwner is not null:
                TransportBuffer? item = null;
                try
                {
                    var body = _deferredBodyOwner.Memory.Span[.._deferredBodyLength];
                    var bufferSize = 8192 + _deferredBodyLength;
                    item = TransportBuffer.Rent(bufferSize);
                    var written = _encoder.EncodeDeferred(item.FullMemory.Span, _deferredFeatures, body);
                    item.Length = written;
                    _ops.OnOutbound(new TransportData(item));
                }
                catch (Exception ex)
                {
                    item?.Dispose();
                    Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 response body: {0}", ex.Message);
                }
                finally
                {
                    _deferredBodyOwner.Dispose();
                    _deferredBodyOwner = null;
                    _deferredFeatures = null;
                }
                break;

            case OutboundBodyFailed failed:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = null;
                if (_deferredFeatures is not null)
                {
                    Tracing.For("Protocol").Error(this, "Failed to read HTTP/1.0 response body: {0}", failed.Reason.Message);
                    _deferredFeatures = null;
                }
                break;
        }
    }

    public void Cleanup()
    {
        _activeBodyEncoder?.Dispose();
        _activeBodyEncoder = null;
        _deferredBodyOwner?.Dispose();
        _deferredBodyOwner = null;
        _deferredFeatures = null;
    }
}
