using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http11;

/// <summary>
/// Callback interface for the stage Logic to receive protocol effects from the state machine.
/// The stage implements this and translates calls to Akka Push/Emit/Log operations.
/// </summary>
public interface IHttp11StageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(IOutputItem item);
    void OnWarning(string message);
    void OnReconnectFailed();
}

/// <summary>
/// Encapsulates all HTTP/1.1 connection protocol logic — request encoding, response decoding,
/// request-response correlation with pipelining, and control signal emission.
/// Calls back into <see cref="IHttp11StageOperations"/> for responses, outbound items, and warnings.
/// </summary>
public sealed class Http11StateMachine
{
    private readonly IHttp11StageOperations _ops;
    private readonly Http11Decoder _decoder = new();
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;
    private readonly int _maxPipelineDepth;
    private readonly int _maxReconnectAttempts;

    private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
    private int _effectivePipelineDepth;
    private Queue<HttpRequestMessage>? _reconnectBufferedQueue;
    private bool _reconnecting;
    private int _reconnectAttempts;

    /// <summary>
    /// Holds a response whose body is delimited by connection close (no Content-Length,
    /// no Transfer-Encoding). Body data is accumulated in <see cref="_bodyOwners"/>
    /// until a <see cref="CloseSignalItem"/> arrives.
    /// </summary>
    private HttpResponseMessage? _pendingCloseDelimitedResponse;

    private List<NetworkBuffer>? _bodyOwners;

    /// <summary>
    /// Body bytes flushed from the decoder remainder when the close-delimited response
    /// is first detected (decoder internal buffer — one unavoidable copy).
    /// </summary>
    private byte[]? _initialBodyBytes;

    /// <summary>Whether a new request can be accepted (queue not full and not reconnecting).</summary>
    public bool CanAcceptRequest => _inFlightQueue.Count < _effectivePipelineDepth && !_reconnecting;

    /// <summary>Whether there are in-flight requests waiting for responses.</summary>
    public bool HasInFlightRequests => _inFlightQueue.Count > 0;

    /// <summary>Whether the state machine is currently in reconnect state.</summary>
    public bool IsReconnecting => _reconnecting;

    /// <summary>Whether we are accumulating a close-delimited body.</summary>
    public bool IsAccumulatingCloseDelimitedBody => _pendingCloseDelimitedResponse is not null;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    public Http11StateMachine(
        IHttp11StageOperations ops,
        int maxPipelineDepth = 8,
        int maxReconnectAttempts = 3,
        int minBufferSize = 4 * 1024,
        int maxBufferSize = 256 * 1024)
    {
        _ops = ops;
        _maxPipelineDepth = maxPipelineDepth;
        _effectivePipelineDepth = maxPipelineDepth;
        _maxReconnectAttempts = maxReconnectAttempts;
        _minBufferSize = minBufferSize;
        _maxBufferSize = maxBufferSize;
    }

    /// <summary>
    /// Encodes an outbound HTTP/1.1 request into a NetworkBuffer and emits it via callbacks.
    /// Emits StreamAcquireItem before the encoded data.
    /// </summary>
    public void EncodeRequest(HttpRequestMessage request)
    {
        _inFlightQueue.Enqueue(request);

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
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

            var written = Http11Encoder.Encode(request, ref span);
            item.Length = written;

            _ops.OnOutbound(item);
        }
        catch (Exception ex)
        {
            item?.Dispose();
            _ops.OnWarning($"Failed to encode request [{request.RequestUri}]: {ex.Message}");
            // Remove request from queue since encoding failed
            // It was the last one enqueued, but we can't easily remove from a Queue,
            // so we dequeue all, skip the failed one, and re-enqueue
            var count = _inFlightQueue.Count;
            for (var i = 0; i < count; i++)
            {
                var queued = _inFlightQueue.Dequeue();
                if (!ReferenceEquals(queued, request))
                {
                    _inFlightQueue.Enqueue(queued);
                }
            }
        }
    }

    /// <summary>
    /// Processes inbound server data (NetworkBuffer or CloseSignalItem).
    /// Decodes responses and emits them via callbacks along with ConnectionReuseItem.
    /// Returns true if more server data should be pulled (i.e. not all data decoded yet).
    /// </summary>
    public bool DecodeServerData(IInputItem inputItem)
    {
        if (inputItem is CloseSignalItem closeSignal)
        {
            HandleCloseSignal(closeSignal);
            return false;
        }

        if (inputItem is not NetworkBuffer buffer)
        {
            return true; // pull more
        }

        // If we're accumulating a connection-close-delimited body,
        // take ownership of the buffer instead of copying to byte[].
        if (_pendingCloseDelimitedResponse is not null)
        {
            _bodyOwners ??= [];
            _bodyOwners.Add(buffer);
            return true; // pull more body data
        }

        try
        {
            var data = buffer.Memory;

            if (_decoder.TryDecode(data, out var responses))
            {
                buffer.Dispose();

                // Check if the last response is connection-close-delimited
                var last = responses[^1];
                if (IsCloseDelimited(last))
                {
                    // Emit all responses except the last one
                    for (var i = 0; i < responses.Count - 1; i++)
                    {
                        CompleteResponse(responses[i]);
                    }

                    // Hold the last response — body is delimited by connection close
                    _pendingCloseDelimitedResponse = last;
                    _bodyOwners = [];

                    // Flush any body data the decoder stored in its remainder
                    var remainder = _decoder.FlushRemainder();
                    _initialBodyBytes = remainder.Length > 0 ? remainder : null;

                    return true; // pull more body data
                }

                foreach (var response in responses)
                {
                    CompleteResponse(response);
                }

                return false;
            }

            // Not enough data yet – return the buffer and wait for more
            buffer.Dispose();
            return true; // pull more
        }
        catch (Exception ex)
        {
            buffer.Dispose();
            _ops.OnWarning($"Failed to decode response: {ex.Message}");
            _decoder.Reset();
            return true; // pull more
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
    /// Emits PipelineRetryItem for all orphaned in-flight requests.
    /// Called when the upstream (server connection) finishes or fails.
    /// </summary>
    public void HandleOrphanedRequests()
    {
        if (_inFlightQueue.Count == 0)
        {
            return;
        }

        _ops.OnWarning(
            $"Connection closed with {_inFlightQueue.Count} orphaned pipelined request(s) — emitting for retry");
        _effectivePipelineDepth = 1;

        while (_inFlightQueue.Count > 0)
        {
            _ops.OnOutbound(new PipelineRetryItem(_inFlightQueue.Dequeue()));
        }
    }

    /// <summary>
    /// Buffers all in-flight requests and emits a ReconnectItem to trigger a new TCP connection.
    /// Call when a CloseSignalItem arrives with in-flight requests and we are not yet reconnecting.
    /// </summary>
    public void StartReconnect()
    {
        _reconnectBufferedQueue = new Queue<HttpRequestMessage>(_inFlightQueue);
        _inFlightQueue.Clear();
        _reconnecting = true;
        _reconnectAttempts = 1;
        _decoder.Reset();
        _ops.OnOutbound(new ReconnectItem { Key = Endpoint });
    }

    /// <summary>
    /// Called when ConnectedSignalItem arrives. Replays all buffered requests over the new connection.
    /// Resets the decoder so stale partial response data from the old connection is discarded.
    /// </summary>
    public void HandleConnectedSignal()
    {
        _reconnecting = false;
        _reconnectAttempts = 0;
        _decoder.Reset();

        if (_reconnectBufferedQueue is { Count: > 0 } queue)
        {
            _reconnectBufferedQueue = null;
            while (queue.Count > 0)
            {
                EncodeRequest(queue.Dequeue());
            }
        }
    }

    /// <summary>
    /// Called when a CloseSignalItem arrives while already reconnecting (reconnect attempt failed).
    /// Increments the attempt counter; emits a new ReconnectItem or calls OnReconnectFailed.
    /// </summary>
    public void HandleReconnectAttempt()
    {
        if (_reconnectAttempts >= _maxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ReconnectItem { Key = Endpoint });
    }

    /// <summary>
    /// Returns pooled resources. Called from PostStop.
    /// </summary>
    public void Cleanup()
    {
        _inFlightQueue.Clear();
        _decoder.Reset();

        if (_bodyOwners is not null)
        {
            foreach (var buf in _bodyOwners)
            {
                buf.Dispose();
            }

            _bodyOwners = null;
        }

        _pendingCloseDelimitedResponse?.Dispose();
        _pendingCloseDelimitedResponse = null;
        _initialBodyBytes = null;
    }

    private void HandleCloseSignal(CloseSignalItem closeSignal)
    {
        if (_pendingCloseDelimitedResponse is not null)
        {
            if (closeSignal.CloseKind == TlsCloseKind.CleanClose)
            {
                // RFC 9112 §9.8: connection close is a valid body delimiter.
                var content = new PooledChunksContent(_initialBodyBytes, _bodyOwners);
                _pendingCloseDelimitedResponse.Content = content;
                var response = _pendingCloseDelimitedResponse;
                _pendingCloseDelimitedResponse = null;
                _bodyOwners = null;
                _initialBodyBytes = null;
                CompleteResponse(response);
            }
            else
            {
                _ops.OnWarning("Abrupt connection close — discarding incomplete response");
                if (_bodyOwners is not null)
                {
                    foreach (var buf in _bodyOwners)
                    {
                        buf.Dispose();
                    }
                }

                _pendingCloseDelimitedResponse = null;
                _bodyOwners = null;
                _initialBodyBytes = null;
                throw new HttpRequestException(
                    "Connection was aborted while receiving close-delimited HTTP/1.1 response.");
            }

            return;
        }

        if (closeSignal.CloseKind == TlsCloseKind.CleanClose)
        {
            // Flush any partially buffered response whose body was delimited by close.
            if (_decoder.TryDecodeEof(out var response) && response is not null)
            {
                CompleteResponse(response);
                return;
            }
        }
        else
        {
            _ops.OnWarning("Abrupt connection close — discarding incomplete response");
        }
    }

    private void CompleteResponse(HttpResponseMessage response)
    {
        var queueCountBeforeDequeue = _inFlightQueue.Count;

        if (_inFlightQueue.Count > 0)
        {
            var request = _inFlightQueue.Dequeue();
            response.RequestMessage = request;
        }

        // Check for Connection: close header
        if (HasConnectionClose(response))
        {
            if (queueCountBeforeDequeue > 1)
            {
                _ops.OnWarning(
                    $"Server sent Connection: close with {queueCountBeforeDequeue} pipelined requests in-flight — disabling pipelining");
            }

            _effectivePipelineDepth = 1;
        }

        var endpoint = response.RequestMessage is { RequestUri: not null, Version: not null }
            ? RequestEndpoint.FromRequest(response.RequestMessage)
            : RequestEndpoint.Default;
        var decision = ConnectionReuseEvaluator.Evaluate(response, response.Version);

        _ops.OnResponse(response);
        var item = new ConnectionReuseItem(decision) { Key = endpoint };
        _ops.OnOutbound(item);
    }

    /// <summary>
    /// RFC 9112 §6.3: A response without Content-Length or Transfer-Encoding
    /// has its body delimited by connection close.
    /// </summary>
    private static bool IsCloseDelimited(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        // 1xx (informational), 204 (No Content), 304 (Not Modified) never carry a message body.
        if (status is >= 100 and < 200 or 204 or 304)
        {
            return false;
        }

        // Transfer-Encoding present — body is chunked
        if (response.Headers.TransferEncodingChunked == true)
        {
            return false;
        }

        // Content-Length explicitly set — body length is known
        if (response.Content.Headers.Contains("Content-Length"))
        {
            return false;
        }

        return true;
    }

    private static bool HasConnectionClose(HttpResponseMessage response)
    {
        return response.Headers.ConnectionClose == true;
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that holds pooled <see cref="NetworkBuffer"/> chunks
    /// accumulated during connection-close-delimited body streaming.
    /// </summary>
    private sealed class PooledChunksContent : HttpContent
    {
        private readonly byte[]? _initial;
        private readonly List<NetworkBuffer>? _chunks;

        public PooledChunksContent(byte[]? initial, List<NetworkBuffer>? chunks)
        {
            _initial = initial;
            _chunks = chunks;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context,
            CancellationToken ct)
        {
            if (_initial is { Length: > 0 })
            {
                await stream.WriteAsync(_initial, ct).ConfigureAwait(false);
            }

            if (_chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    await stream.WriteAsync(buf.Memory, ct).ConfigureAwait(false);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _initial?.Length ?? 0;
            if (_chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    length += buf.Length;
                }
            }

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    buf.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}