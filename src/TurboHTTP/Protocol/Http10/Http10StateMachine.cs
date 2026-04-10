using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http10;

/// <summary>
/// Callback interface for the stage Logic to receive protocol effects from the state machine.
/// The stage implements this and translates calls to Akka Push/Emit/Log operations.
/// </summary>
public interface IHttp10StageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(IOutputItem item);
    void OnWarning(string message);
}

/// <summary>
/// Encapsulates all HTTP/1.0 connection protocol logic — request encoding, response decoding,
/// request-response correlation, and control signal emission.
/// Calls back into <see cref="IHttp10StageOperations"/> for responses, outbound items, and warnings.
/// </summary>
public sealed class Http10StateMachine
{
    private readonly IHttp10StageOperations _ops;
    private readonly Http10Decoder _decoder = new();
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;

    private HttpRequestMessage? _inFlightRequest;
    private bool _closed;

    /// <summary>Whether a new request can be accepted (no in-flight request and not closed).</summary>
    public bool CanAcceptRequest => _inFlightRequest is null && !_closed;

    /// <summary>Whether there is an in-flight request waiting for a response.</summary>
    public bool HasInFlightRequest => _inFlightRequest is not null;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    public Http10StateMachine(IHttp10StageOperations ops, int minBufferSize = 4 * 1024, int maxBufferSize = 256 * 1024)
    {
        _ops = ops;
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

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
        }

        // Emit StreamAcquireItem before request data
        _ops.OnOutbound(StreamAcquireItem.Rent(endpoint));

        NetworkBuffer? item = null;
        try
        {
            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
            var estimatedSize = _minBufferSize + contentLength;
            var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
            item = NetworkBuffer.Rent(bufferSize);
            item.Key = endpoint;
            var span = item.FullMemory.Span;

            var written = Http10Encoder.Encode(request, ref span);
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
    /// Emits PipelineRetryItem for any orphaned in-flight request.
    /// Called when the upstream (server connection) finishes or fails.
    /// </summary>
    public void HandleOrphanedRequest()
    {
        if (_inFlightRequest is not null)
        {
            _ops.OnWarning("Connection closed with orphaned request — emitting PipelineRetryItem");
            var retryItem = new PipelineRetryItem(_inFlightRequest);
            _inFlightRequest = null;
            _ops.OnOutbound(retryItem);
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
        var endpoint = response.RequestMessage is { RequestUri: not null }
            ? RequestEndpoint.FromRequest(response.RequestMessage)
            : RequestEndpoint.Default;
        var decision = ConnectionReuseEvaluator.Evaluate(response, response.Version);

        _ops.OnResponse(response);
        _ops.OnOutbound(ConnectionReuseItem.Rent(endpoint, decision));
    }
}
