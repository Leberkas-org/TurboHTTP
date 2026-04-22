using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Protocol.Http10;

/// <summary>
/// Encapsulates all HTTP/1.0 connection protocol logic — request encoding, response decoding,
/// request-response correlation, and control signal emission.
/// Calls back into <see cref="IStageOperations"/> for responses, outbound items, and warnings.
/// </summary>
internal sealed class StateMachine
{
    private readonly IStageOperations _ops;
    private readonly Decoder _decoder;
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;
    private readonly TurboClientOptions _options;

    private ITransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private bool _closed;
    private HttpRequestMessage? _reconnectBufferedRequest;
    private bool _reconnecting;
    private int _reconnectAttempts;

    /// <summary>Whether a new request can be accepted (no in-flight request, not closed, not reconnecting).</summary>
    public bool CanAcceptRequest => _inFlightRequest is null && !_closed && !_reconnecting;

    /// <summary>Whether there is an in-flight request waiting for a response.</summary>
    public bool HasInFlightRequest => _inFlightRequest is not null;

    /// <summary>Whether the state machine is currently in reconnect state.</summary>
    public bool IsReconnecting => _reconnecting;

    /// <summary>Number of requests currently buffered or in-flight (used for discard logging).</summary>
    public int PendingRequestCount => _reconnecting
        ? _reconnectBufferedRequest is not null ? 1 : 0
        : _inFlightRequest is not null
            ? 1
            : 0;

    /// <summary>The current connection endpoint.</summary>
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

    /// <summary>
    /// Encodes an outbound HTTP/1.0 request into a NetworkBuffer and emits it via callbacks.
    /// Emits StreamAcquireItem before the encoded data.
    /// </summary>
    public void EncodeRequest(HttpRequestMessage request)
    {
        _inFlightRequest = request;

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(endpoint, _options);
            _ops.OnOutbound(new ConnectItem
            {
                Key = Endpoint,
                Options = _transportOptions
            });
        }

        // Emit StreamAcquireItem before request data
        _ops.OnOutbound(new StreamAcquireItem { Key = endpoint });

        NetworkBuffer? item = null;
        try
        {
            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
            var estimatedSize = _minBufferSize + contentLength;
            var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
            item = NetworkBuffer.Rent(bufferSize);
            item.Key = endpoint;
            var span = item.FullMemory.Span;

            var written = Encoder.Encode(request, ref span);
            item.Length = written;

            _ops.OnOutbound(item);
        }
        catch (Exception ex)
        {
            item?.Dispose();
            _ops.OnWarning($"Failed to encode request [{request.RequestUri}]: {ex.Message}");
            // Clear in-flight since encoding failed
            _inFlightRequest = null;
        }
    }

    /// <summary>
    /// Processes inbound server data (NetworkBuffer or CloseSignalItem).
    /// Decodes responses and emits them via callbacks along with ConnectionReuseItem.
    /// </summary>
    public void DecodeServerData(IInputItem item)
    {
        if (item is CloseSignalItem closeSignal)
        {
            HandleCloseSignal(closeSignal);
            return;
        }

        if (item is not NetworkBuffer buffer)
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
                // Not enough data yet — caller will pull more from server
            }
        }
        catch (Exception ex)
        {
            buffer.Dispose();
            _ops.OnWarning($"Failed to decode response: {ex.Message}");
            _decoder.Reset();
        }
    }

    /// <summary>
    /// Attempts to decode any remaining buffered data on EOF (upstream finish).
    /// Returns true if a response was emitted.
    /// </summary>
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

    /// <summary>
    /// Logs and discards any orphaned in-flight request.
    /// Called when the upstream (server connection) finishes or fails.
    /// </summary>
    public void HandleOrphanedRequest()
    {
        if (_inFlightRequest is not null)
        {
            _ops.OnWarning("Connection closed with orphaned request — discarding.");
            _inFlightRequest = null;
        }
    }

    /// <summary>
    /// Marks the state machine as closed. No more requests will be accepted.
    /// </summary>
    public void MarkClosed()
    {
        _closed = true;
    }

    /// <summary>
    /// Buffers the in-flight request and emits a ConnectItem (reconnect) to trigger a new TCP connection.
    /// Call when a CloseSignalItem arrives with an in-flight request and we are not yet reconnecting.
    /// </summary>
    public void StartReconnect()
    {
        _reconnectBufferedRequest = _inFlightRequest;
        _inFlightRequest = null;
        _reconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectItem
        {
            Key = Endpoint, IsReconnect = true,
            Options = _transportOptions!
        });
    }

    /// <summary>
    /// Called when ConnectedSignalItem arrives. Replays the buffered request over the new connection.
    /// Resets the decoder so stale partial response data from the old connection is discarded.
    /// </summary>
    public void OnConnectionRestored()
    {
        _reconnecting = false;
        _reconnectAttempts = 0;
        _decoder.Reset();

        if (_reconnectBufferedRequest is { } req)
        {
            _reconnectBufferedRequest = null;
            EncodeRequest(req);
        }
    }

    /// <summary>
    /// Called when a CloseSignalItem arrives while already reconnecting (reconnect attempt failed).
    /// Increments the attempt counter; emits a new ConnectItem (reconnect) or calls OnReconnectFailed.
    /// </summary>
    public void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ConnectItem
        {
            Key = Endpoint, IsReconnect = true,
            Options = _transportOptions!
        });
    }

    /// <summary>
    /// Returns pooled resources. Called from PostStop.
    /// </summary>
    public void Cleanup()
    {
        _inFlightRequest = null;
        _decoder.Reset();
    }

    private void HandleCloseSignal(CloseSignalItem closeSignal)
    {
        if (closeSignal.CloseKind == TlsCloseKind.AbruptClose)
        {
            var message = _decoder.IsWaitingForContentLength
                ? "Content-Length mismatch: connection closed before all body data was received."
                : "Connection was aborted while receiving HTTP/1.0 response.";

            _decoder.Reset();
            _closed = true;
            throw new HttpRequestException(message);
        }

        // Clean close: body is delimited by connection close (RFC 1945 §7.2.2)
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

        // HTTP/1.0 default is Connection: close (RFC 1945)
        var endpoint = RequestEndpoint.FromRequest(response.RequestMessage!);
        var decision = ConnectionReuseEvaluator.Evaluate(response, response.Version);
        var item = new ConnectionReuseItem(decision) { Key = endpoint };
        _ops.OnResponse(response);
        _ops.OnOutbound(item);
    }
}