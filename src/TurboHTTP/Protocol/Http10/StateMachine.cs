using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Http10;

internal sealed class StateMachine : IHttpStateMachine
{
    private readonly IStageOperations _ops;
    private readonly Decoder _decoder;
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;
    private readonly TurboClientOptions _options;

    private TransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private HttpRequestMessage? _reconnectBufferedRequest;
    private int _reconnectAttempts;

    public bool CanAcceptRequest => _inFlightRequest is null && !IsReconnecting;

    public bool HasInFlightRequest => _inFlightRequest is not null;

    bool IHttpStateMachine.HasInFlightRequests => HasInFlightRequest;

    public bool IsReconnecting { get; private set; }

    public int PendingRequestCount => IsReconnecting
        ? _reconnectBufferedRequest is not null ? 1 : 0
        : _inFlightRequest is not null
            ? 1
            : 0;

    public RequestEndpoint Endpoint { get; private set; }

    public StateMachine(
        IStageOperations ops,
        TurboClientOptions options,
        int minBufferSize = 4 * 1024,
        int maxBufferSize = 256 * 1024)
    {
        _ops = ops;
        _options = options;
        _decoder = new Decoder(maxTotalHeaderSize: options.Http1.MaxResponseHeadersLength * 1024);
        _minBufferSize = minBufferSize;
        _maxBufferSize = maxBufferSize;
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

        TryDecodeEof();
        FailOrphanedRequest();
    }

    public void OnTimerFired(string name)
    {
    }

    public void Cleanup()
    {
        _inFlightRequest = null;
        _decoder.Reset();
    }

    private void EncodeRequest(HttpRequestMessage request)
    {
        _inFlightRequest = request;

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        TransportBuffer? item = null;
        try
        {
            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
            var estimatedSize = _minBufferSize + contentLength;
            var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
            item = TransportBuffer.Rent(bufferSize);
            var span = item.FullMemory.Span;

            var written = Encoder.Encode(request, ref span);
            item.Length = written;

            _ops.OnOutbound(new TransportData(item));
        }
        catch (Exception ex)
        {
            item?.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 request [{0}]: {1}", request.RequestUri, ex.Message);
            request.Fail(ex);
            _inFlightRequest = null;
        }
    }

    private void DecodeResponse(TransportBuffer buffer)
    {
        try
        {
            var data = buffer.Memory;

            if (_decoder.TryDecode(data, out var response) && response is not null)
            {
                buffer.Dispose();
                CompleteResponse(response);
            }
            else
            {
                buffer.Dispose();
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
        if (HasInFlightRequest && _options.Http1.MaxReconnectAttempts > 0)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        if (!isGraceful)
        {
            var message = _decoder.IsWaitingForContentLength
                ? "Content-Length mismatch: connection closed before all body data was received."
                : "Connection was aborted while receiving HTTP/1.0 response.";

            if (_inFlightRequest is { } req)
            {
                req.Fail(new HttpRequestException(message));
                _inFlightRequest = null;
            }

            _decoder.Reset();
            Tracing.For("Protocol").Info(this, "HTTP/1.0: {0}", message);
            return;
        }

        if (_decoder.TryDecodeEof(out var eofResponse) && eofResponse is not null)
        {
            CompleteResponse(eofResponse);
        }
    }

    private void TryDecodeEof()
    {
        try
        {
            if (_decoder.TryDecodeEof(out var response) && response is not null)
            {
                CompleteResponse(response);
                return;
            }

            _decoder.Reset();
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.0 EOF: {0}", ex.Message);
            _decoder.Reset();
        }
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

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}