using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientStateMachine : IClientStateMachine
{
    private readonly IStageOperations _ops;
    private readonly Http10ClientDecoder _decoder;
    private readonly Http10ClientEncoder _encoder;
    private readonly TurboClientOptions _options;

    private TransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private HttpRequestMessage? _reconnectBufferedRequest;
    private int _reconnectAttempts;
    private bool _lastRequestWasHead;
    private bool _outboundBodyPending;
    private HttpRequestMessage? _deferredRequest;
    private IMemoryOwner<byte>? _deferredBodyOwner;
    private int _deferredBodyLength;
    private bool _connectionClosed;

    public bool CanAcceptRequest => _inFlightRequest is null && !IsReconnecting && !_outboundBodyPending;

    public bool HasInFlightRequests => _inFlightRequest is not null;

    public bool IsReconnecting { get; private set; }

    private int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectBufferedRequest is not null ? 1 : 0;
            }

            return _inFlightRequest is not null ? 1 : 0;
        }
    }

    public RequestEndpoint Endpoint { get; private set; }

    public Http10ClientStateMachine(IStageOperations ops, TurboClientOptions options)
    {
        _ops = ops;
        _options = options;

        var decoderOpts = new Http10ClientDecoderOptions
        {
            Shared = SharedHttpOptions.Default with
            {
                MaxHeaderBytes = options.Http1.MaxResponseHeadersLength * 1024,
                MaxBufferedBodySize = options.MaxBufferedBodySize,
                MaxStreamedBodySize = options.MaxStreamedBodySize,
            }
        };
        var encoderOpts = Http10ClientEncoderOptions.Default;

        _decoder = new Http10ClientDecoder(decoderOpts);
        _encoder = new Http10ClientEncoder(encoderOpts);
    }

    public void PreStart()
    {
    }

    public void OnRequest(HttpRequestMessage request)
    {
        EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                OnConnectionRestored();
                return;

            case TransportDisconnected when IsReconnecting:
                OnReconnectAttemptFailed();
                return;

            case TransportDisconnected disconnect when !IsReconnecting:
                HandleDisconnect(disconnect);
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        DecodeResponse(buffer);
    }

    public void OnUpstreamFinished()
    {
        var bodyComplete = _decoder.SignalEof();

        if (IsReconnecting)
        {
            if (_reconnectBufferedRequest is { } buffered)
            {
                buffered.Fail(new HttpRequestException("HTTP/1.0 transport closed during reconnect."));
                _reconnectBufferedRequest = null;
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 transport closed during reconnect");
            return;
        }

        TryDecodeEof(bodyComplete);
        FailOrphanedRequest();
    }

    public void OnTimerFired(string name)
    {
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case OutboundBodyChunk chunk when _deferredRequest is not null:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = chunk.Owner;
                _deferredBodyLength = chunk.Length;
                break;

            case OutboundBodyComplete when _deferredRequest is not null && _deferredBodyOwner is not null:
                TransportBuffer? item = null;
                try
                {
                    var body = _deferredBodyOwner.Memory.Span[.._deferredBodyLength];
                    item = TransportBuffer.Rent(HttpMessageSize.Estimate(_deferredRequest, _deferredBodyLength));
                    var written = _encoder.EncodeDeferred(item.FullMemory.Span, _deferredRequest, body);
                    item.Length = written;
                    _ops.OnOutbound(new TransportData(item));
                }
                catch (Exception ex)
                {
                    item?.Dispose();
                    _deferredRequest.Fail(new HttpRequestException("Failed to encode HTTP/1.0 request body.", ex));
                }
                finally
                {
                    _deferredBodyOwner.Dispose();
                    _deferredBodyOwner = null;
                    _deferredRequest = null;
                    _outboundBodyPending = false;
                }

                break;

            case OutboundBodyComplete:
                _outboundBodyPending = false;
                break;

            case OutboundBodyFailed failed:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = null;
                _outboundBodyPending = false;
                if (_deferredRequest is not null)
                {
                    _deferredRequest.Fail(new HttpRequestException("Failed to read HTTP/1.0 request body.",
                        failed.Reason));
                    _deferredRequest = null;
                }

                break;
        }
    }

    public void Cleanup()
    {
        _inFlightRequest = null;
        _outboundBodyPending = false;
        _deferredBodyOwner?.Dispose();
        _deferredBodyOwner = null;
        _deferredRequest = null;
        _connectionClosed = false;
        _decoder.Reset();
    }

    private void EncodeRequest(HttpRequestMessage request)
    {
        _inFlightRequest = request;
        _lastRequestWasHead = request.Method == HttpMethod.Head;

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }
        else if (_connectionClosed && _transportOptions is not null)
        {
            _connectionClosed = false;
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        TransportBuffer? item = null;
        try
        {
            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
            item = TransportBuffer.Rent(HttpMessageSize.Estimate(request, contentLength));
            var span = item.FullMemory.Span;

            var written = _encoder.Encode(span, request, _ops.StageActor);
            if (written > 0)
            {
                item.Length = written;
                _ops.OnOutbound(new TransportData(item));
            }
            else
            {
                // Deferred — HTTP/1.0 with body; waiting for OutboundBodyChunk + OutboundBodyComplete
                item.Dispose();
                item = null;
                _deferredRequest = request;
                _outboundBodyPending = true;
            }
        }
        catch (Exception ex)
        {
            item?.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 request [{0}]: {1}", request.RequestUri,
                ex.Message);
            request.Fail(ex);
            _inFlightRequest = null;
        }
    }

    private void DecodeResponse(TransportBuffer buffer)
    {
        try
        {
            var outcome = _decoder.Feed(buffer.Memory.Span, _lastRequestWasHead, out _);
            buffer.Dispose();

            if (outcome == DecodeOutcome.Complete)
            {
                var response = _decoder.GetResponse();
                CompleteResponse(response);
                _decoder.Reset();
            }
        }
        catch (Exception ex)
        {
            buffer.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.0 response: {0}", ex.Message);
            if (_inFlightRequest is { } req)
            {
                req.Fail(new HttpRequestException("Failed to decode HTTP/1.0 response.", ex));
                _inFlightRequest = null;
            }

            _decoder.Reset();
        }
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        var bodyComplete = _decoder.SignalEof();
        _connectionClosed = true;

        if (isGraceful)
        {
            TryCompleteAfterEof(bodyComplete);
            return;
        }

        if (HasInFlightRequests && _options.Http1.MaxReconnectAttempts > 0)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        const string message = "Connection was aborted while receiving HTTP/1.0 response.";

        if (_inFlightRequest is { } req)
        {
            req.Fail(new HttpRequestException(message));
            _inFlightRequest = null;
        }

        _decoder.Reset();
        Tracing.For("Protocol").Info(this, "HTTP/1.0: {0}", message);
    }

    private void TryCompleteAfterEof(bool bodyComplete)
    {
        if (_inFlightRequest is null)
        {
            _decoder.Reset();
            return;
        }

        if (!bodyComplete)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.0 connection closed before response body was complete");
            _inFlightRequest.Fail(
                new HttpRequestException("HTTP/1.0 connection closed before response body was complete."));
            _inFlightRequest = null;
            _decoder.Reset();
            return;
        }

        try
        {
            var response = _decoder.GetResponse();
            _decoder.Reset();
            CompleteResponse(response);
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to complete HTTP/1.0 response at EOF: {0}", ex.Message);
            _inFlightRequest.Fail(new HttpRequestException("Failed to complete HTTP/1.0 response at EOF.", ex));
            _inFlightRequest = null;
            _decoder.Reset();
        }
    }

    private void TryDecodeEof(bool bodyComplete)
    {
        TryCompleteAfterEof(bodyComplete);
    }

    private void FailOrphanedRequest()
    {
        if (_inFlightRequest is not null)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.0 connection closed with orphaned request — failing");
            _inFlightRequest.Fail(new HttpRequestException("HTTP/1.0 connection closed with orphaned request."));
            _inFlightRequest = null;
        }
    }

    private void StartReconnect()
    {
        _reconnectBufferedRequest = _inFlightRequest;
        _inFlightRequest = null;
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;
        _connectionClosed = false;
        _decoder.Reset();

        if (_reconnectBufferedRequest is { } req)
        {
            _reconnectBufferedRequest = null;
            EncodeRequest(req);
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 reconnect failed after {0} attempts", _reconnectAttempts);
            if (_reconnectBufferedRequest is { } buffered)
            {
                buffered.Fail(new HttpRequestException("HTTP/1.0 reconnect failed after max attempts."));
                _reconnectBufferedRequest = null;
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void CompleteResponse(HttpResponseMessage response)
    {
        var request = _inFlightRequest;
        _inFlightRequest = null;
        _connectionClosed = true;

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}