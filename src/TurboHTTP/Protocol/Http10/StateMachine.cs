using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http10;

internal sealed class StateMachine
{
    private readonly IStageOperations _ops;
    private readonly Decoder _decoder;
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;
    private readonly TurboClientOptions _options;

    private TransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private bool _closed;
    private HttpRequestMessage? _reconnectBufferedRequest;
    private int _reconnectAttempts;

    public bool CanAcceptRequest => _inFlightRequest is null && !_closed && !IsReconnecting;

    public bool HasInFlightRequest => _inFlightRequest is not null;

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

    public void EncodeRequest(HttpRequestMessage request)
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
            _ops.OnWarning($"Failed to encode request [{request.RequestUri}]: {ex.Message}");
            _inFlightRequest = null;
        }
    }

    public void DecodeServerData(ITransportInbound inputItem)
    {
        if (inputItem is TransportDisconnected disconnect)
        {
            HandleDisconnect(disconnect);
            return;
        }

        if (inputItem is not TransportData { Buffer: var buffer })
        {
            return;
        }

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
            _ops.OnWarning($"Failed to decode response: {ex.Message}");
            _decoder.Reset();
        }
    }

    public bool TryDecodeEof()
    {
        try
        {
            if (_decoder.TryDecodeEof(out var response) && response is not null)
            {
                CompleteResponse(response);
                return true;
            }

            _decoder.Reset();
            return false;
        }
        catch (Exception ex)
        {
            _ops.OnWarning($"Failed to decode EOF: {ex.Message}");
            _decoder.Reset();
            return false;
        }
    }

    public void HandleOrphanedRequest()
    {
        if (_inFlightRequest is not null)
        {
            _ops.OnWarning("Connection closed with orphaned request — discarding.");
            _inFlightRequest = null;
        }
    }

    public void MarkClosed()
    {
        _closed = true;
    }

    public void StartReconnect()
    {
        _reconnectBufferedRequest = _inFlightRequest;
        _inFlightRequest = null;
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    public void OnConnectionRestored()
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

    public void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    public void Cleanup()
    {
        _inFlightRequest = null;
        _decoder.Reset();
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        if (!isGraceful)
        {
            var message = _decoder.IsWaitingForContentLength
                ? "Content-Length mismatch: connection closed before all body data was received."
                : "Connection was aborted while receiving HTTP/1.0 response.";

            _decoder.Reset();
            _closed = true;
            throw new HttpRequestException(message);
        }

        if (_decoder.TryDecodeEof(out var eofResponse) && eofResponse is not null)
        {
            CompleteResponse(eofResponse);
        }
        else
        {
            _decoder.Reset();
        }
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