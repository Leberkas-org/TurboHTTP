using System.Net;
using Akka.Event;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly int _maxPipelineDepth;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;

    private int _requestsPipelined;
    private int _pendingResponseCount;
    private bool _outboundBodyPending;
    private bool _requestHeadersTimerActive;
    private bool _draining;
    private readonly TurboServerOptions _serverOptions;

    public bool CanAcceptResponse => !_outboundBodyPending && _pendingResponseCount > 0;
    public bool ShouldComplete { get; private set; }
    public int MaxQueuedRequests => _maxPipelineDepth;

    public Http11ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        _serverOptions = options;

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http1.MaxRequestBodySize,
            MaxHeaderBytes = options.Http1.MaxHeaderListSize,
            HeaderLineMaxLength = options.Http1.MaxRequestLineLength,
            RequestLineMaxLength = options.Http1.MaxRequestLineLength,
        };

        var encOpts = new Http11ServerEncoderOptions
        {
            Shared = shared,
            KeepAliveTimeout = options.Http1.KeepAliveTimeout ?? options.Limits.KeepAliveTimeout,
            RequestHeadersTimeout = options.Http1.RequestHeadersTimeout ?? options.Limits.RequestHeadersTimeout,
        };

        var decOpts = new Http11ServerDecoderOptions
        {
            Shared = shared,
            MaxPipelinedRequests = options.Http1.MaxPipelinedRequests,
        };

        encOpts.Validate();
        decOpts.Validate();

        _decoder = new Http11ServerDecoder(decOpts);
        _encoder = new Http11ServerEncoder(encOpts);
        _keepAliveTimeout = encOpts.KeepAliveTimeout;
        _requestHeadersTimeout = encOpts.RequestHeadersTimeout;
        _maxPipelineDepth = decOpts.MaxPipelinedRequests;
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
            var span = buffer.Memory.Span;
            var pos = 0;

            if (_draining && _decoder.CurrentBodyDecoder is { } bodyDecoder)
            {
                var drained = bodyDecoder.Drain(span[pos..]);
                pos += drained;

                if (bodyDecoder.IsComplete)
                {
                    _draining = false;
                    _decoder.Reset();
                }
            }

            // Schedule request headers timeout if not already active
            if (!_requestHeadersTimerActive && _pendingResponseCount == 0 && _requestHeadersTimeout > TimeSpan.Zero)
            {
                _ops.OnScheduleTimer("request-headers", _requestHeadersTimeout);
                _requestHeadersTimerActive = true;
            }

            while (pos < span.Length)
            {
                var outcome = _decoder.Feed(span[pos..], out var consumed);
                pos += consumed;

                if (outcome != DecodeOutcome.Complete)
                {
                    break;
                }

                // Cancel the request headers timer once headers are complete
                if (_requestHeadersTimerActive)
                {
                    _ops.OnCancelTimer("request-headers");
                    _requestHeadersTimerActive = false;
                }

                _requestsPipelined++;
                if (_requestsPipelined > _maxPipelineDepth)
                {
                    ShouldComplete = true;
                    break;
                }

                if (!ShouldComplete && _decoder.HasConnectionClose)
                {
                    ShouldComplete = true;
                }

                var feature = _decoder.GetRequestFeature();
                var hasBody = feature.Body != Stream.Null;
                var features = FeatureCollectionFactory.Create(feature, hasBody, _ops.Services, _ops.ConnectionFeature,
                    _ops.TlsHandshakeFeature, _serverOptions.Limits.MaxRequestBodySize);

                if (!ShouldComplete && feature.Protocol == "HTTP/1.0")
                {
                    ShouldComplete = true;
                }

                if (TryHandleH2cUpgrade(features))
                {
                    _decoder.Reset();
                    break;
                }

                _pendingResponseCount++;
                _ops.OnRequest(features);
                _decoder.Reset();
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
        if (_pendingResponseCount == 0)
        {
            throw new InvalidOperationException("Cannot send a response when no requests are pending.");
        }

        _pendingResponseCount--;

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();

        var statusCode = responseFeature?.StatusCode ?? 200;
        var suppressBody = statusCode is >= 100 and < 200 or 204 or 304;

        var contentLength = ExtractContentLength(responseFeature);
        var hasExplicitChunked = responseFeature?.Headers?.Any(h =>
            h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            && h.Value.Any(v => v.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))) ?? false;
        var isChunked = !suppressBody && (contentLength is null || hasExplicitChunked);

        var responseBuffer = TransportBuffer.Rent(8192);
        var span = responseBuffer.FullMemory.Span;
        var written = _encoder.Encode(span, features, isChunked, connectionClose: ShouldComplete);
        responseBuffer.Length = written;
        _ops.OnOutbound(new TransportData(responseBuffer));

        if (suppressBody)
        {
            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
            }

            return;
        }

        if (!_draining && _decoder.CurrentBodyDecoder is { IsComplete: false })
        {
            _draining = true;
        }

        if (responseBody is TurboHttpResponseBodyFeature turboBody)
        {
            _outboundBodyPending = true;

            var bodyStream = turboBody.GetResponseStream();
            var encoder = BodyEncoderFactory.Create(bodyStream, contentLength, HttpVersion.Version11);
            if (encoder is not null)
            {
                _encoder.SetActiveBodyEncoder(encoder);
                encoder.Start(bodyStream, _ops.StageActor);
            }
        }
        else
        {
            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
            }
        }
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == "keep-alive")
        {
            // Keep-alive timeout expired, close the connection
            ShouldComplete = true;
        }
        else if (name == "request-headers")
        {
            // Request headers timeout expired before headers were fully received
            _requestHeadersTimerActive = false;
            ShouldComplete = true;
        }
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case OutboundBodyChunk chunk:
                var buf = TransportBuffer.Rent(chunk.Length);
                chunk.Owner.Memory.Span[..chunk.Length].CopyTo(buf.FullMemory.Span);
                buf.Length = chunk.Length;
                chunk.Owner.Dispose();
                _ops.OnOutbound(new TransportData(buf));
                break;

            case OutboundBodyComplete:
                _outboundBodyPending = false;
                // Schedule keep-alive timer after body completes if needed
                if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
                {
                    _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
                }

                break;

            case OutboundBodyFailed failed:
                _outboundBodyPending = false;
                _ops.Log.Warning("Failed to encode HTTP/1.1 response body: {0}", failed.Reason.Message);
                break;
        }
    }

    private static long? ExtractContentLength(IHttpResponseFeature? responseFeature)
    {
        if (responseFeature?.Headers is null)
        {
            return null;
        }

        foreach (var header in responseFeature.Headers)
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (header.Value.FirstOrDefault() is { } value && long.TryParse(value, out var length))
                {
                    return length;
                }
            }
        }

        return null;
    }

    private bool TryHandleH2cUpgrade(IFeatureCollection features)
    {
        if (_ops is not IProtocolSwitchCapable switchable)
        {
            return false;
        }

        var requestFeature = features.Get<IHttpRequestFeature>();
        var requestHeaders = requestFeature?.Headers;
        if (requestHeaders is null)
        {
            return false;
        }

        var hasUpgrade = requestHeaders.TryGetValue("Upgrade", out var upgradeValue)
                         && !string.IsNullOrEmpty(upgradeValue)
                         && upgradeValue.ToString().Split(',')
                             .Any(v => v.Trim().Equals("h2c", StringComparison.OrdinalIgnoreCase));

        if (!hasUpgrade)
        {
            return false;
        }

        if (!requestHeaders.TryGetValue("HTTP2-Settings", out _))
        {
            return false;
        }

        var responseBytes = "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: h2c\r\n\r\n"u8;
        var responseBuffer = TransportBuffer.Rent(responseBytes.Length);
        responseBytes.CopyTo(responseBuffer.FullMemory.Span);
        responseBuffer.Length = responseBytes.Length;
        _ops.OnOutbound(new TransportData(responseBuffer));

        switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(_serverOptions, ops));

        return true;
    }

    public void Cleanup()
    {
        _encoder.CancelActiveBody();
        _outboundBodyPending = false;
        _pendingResponseCount = 0;
        if (_requestHeadersTimerActive)
        {
            _ops.OnCancelTimer("request-headers");
            _requestHeadersTimerActive = false;
        }

        _ops.OnCancelTimer("keep-alive");
    }
}