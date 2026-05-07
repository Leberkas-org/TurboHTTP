using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Http11;

internal sealed class StateMachine : IHttpStateMachine
{
    private readonly IStageOperations _ops;
    private readonly Decoder _decoder;
    private readonly int _minBufferSize;
    private readonly int _maxBufferSize;
    private readonly TurboClientOptions _options;

    private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
    private Queue<HttpRequestMessage>? _reconnectBufferedQueue;
    private int _effectivePipelineDepth;
    private int _reconnectAttempts;
    private TransportOptions? _transportOptions;

    private HttpResponseMessage? _pendingCloseDelimitedResponse;
    private List<TransportBuffer>? _bodyOwners;
    private byte[]? _initialBodyBytes;

    public bool CanAcceptRequest => _inFlightQueue.Count < _effectivePipelineDepth && !IsReconnecting;

    public bool HasInFlightRequests => _inFlightQueue.Count > 0;

    public int PendingRequestCount => IsReconnecting
        ? _reconnectBufferedQueue?.Count ?? 0
        : _inFlightQueue.Count;

    public bool IsReconnecting { get; private set; }

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
        _effectivePipelineDepth = options.Http1.MaxPipelineDepth;
    }

    public void PreStart()
    {
    }

    public void OnRequest(HttpRequestMessage request)
    {
        _inFlightQueue.Enqueue(request);

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(Endpoint, _options);
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
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.1 request [{0}]: {1}", request.RequestUri, ex.Message);
            request.Fail(ex);
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
            if (_reconnectBufferedQueue is { Count: > 0 })
            {
                RequestFault.FailAll(_reconnectBufferedQueue, new HttpRequestException("HTTP/1.1 transport closed during reconnect."));
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            Tracing.For("Protocol").Debug(this, "HTTP/1.1 transport closed during reconnect");
            return;
        }

        TryDecodeEof();
        FailOrphanedRequests();
    }

    public void OnTimerFired(string name)
    {
    }

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

    private void DecodeResponse(TransportBuffer buffer)
    {
        if (_pendingCloseDelimitedResponse is not null)
        {
            AccumulateCloseDelimitedBody(buffer);
            return;
        }

        DecodeNormalResponse(buffer);
    }

    private void AccumulateCloseDelimitedBody(TransportBuffer buffer)
    {
        _bodyOwners ??= [];
        _bodyOwners.Add(buffer);
    }

    private void DecodeNormalResponse(TransportBuffer buffer)
    {
        try
        {
            var data = buffer.Memory;

            if (!_decoder.TryDecode(data, out var responses))
            {
                buffer.Dispose();
                return;
            }

            buffer.Dispose();

            var last = responses[^1];
            if (IsCloseDelimited(last))
            {
                BeginCloseDelimitedResponse(responses);
                return;
            }

            foreach (var response in responses)
            {
                CompleteResponse(response);
            }
        }
        catch (Exception ex)
        {
            buffer.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.1 response: {0}", ex.Message);
            _decoder.Reset();
        }
    }

    private void BeginCloseDelimitedResponse(IReadOnlyList<HttpResponseMessage> responses)
    {
        for (var i = 0; i < responses.Count - 1; i++)
        {
            CompleteResponse(responses[i]);
        }

        _pendingCloseDelimitedResponse = responses[^1];
        _bodyOwners = [];

        var remainder = _decoder.FlushRemainder();
        _initialBodyBytes = remainder.Length > 0 ? remainder : null;
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        if (_pendingCloseDelimitedResponse is not null)
        {
            if (isGraceful)
            {
                var content = PooledBodyContent.FromChunks(_initialBodyBytes, _bodyOwners);
                _pendingCloseDelimitedResponse.Content = content;
                var response = _pendingCloseDelimitedResponse;
                _pendingCloseDelimitedResponse = null;
                _bodyOwners = null;
                _initialBodyBytes = null;
                CompleteResponse(response);
            }
            else
            {
                Tracing.For("Protocol").Info(this, "HTTP/1.1: Abrupt connection close — discarding incomplete response");
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

                if (_inFlightQueue.Count > 0)
                {
                    var exception = new HttpRequestException("Connection was aborted while receiving close-delimited HTTP/1.1 response.");
                    RequestFault.FailAll(_inFlightQueue, exception);
                    _inFlightQueue.Clear();
                }
            }

            return;
        }

        if (HasInFlightRequests && _options.Http1.MaxReconnectAttempts > 0)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        if (isGraceful)
        {
            if (_decoder.TryDecodeEof(out var response) && response is not null)
            {
                CompleteResponse(response);
            }
        }
        else
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1: Abrupt connection close — discarding incomplete response");
            if (_inFlightQueue.Count > 0)
            {
                var exception = new HttpRequestException("Connection was aborted while receiving HTTP/1.1 response.");
                RequestFault.FailAll(_inFlightQueue, exception);
                _inFlightQueue.Clear();
            }
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
            Tracing.For("Protocol").Warning(this, "Failed to decode HTTP/1.1 EOF: {0}", ex.Message);
            _decoder.Reset();
        }
    }

    private void FailOrphanedRequests()
    {
        if (_inFlightQueue.Count == 0)
        {
            return;
        }

        Tracing.For("Protocol").Debug(this, "HTTP/1.1 connection closed with {0} orphaned pipelined request(s) — failing", _inFlightQueue.Count);
        var exception = new HttpRequestException("HTTP/1.1 connection closed with orphaned request(s).");
        RequestFault.FailAll(_inFlightQueue, exception);
        _inFlightQueue.Clear();
        _effectivePipelineDepth = 1;
    }

    private void StartReconnect()
    {
        _reconnectBufferedQueue = new Queue<HttpRequestMessage>(_inFlightQueue);
        _inFlightQueue.Clear();
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _decoder.Reset();
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;
        _decoder.Reset();

        if (_reconnectBufferedQueue is { Count: > 0 } queue)
        {
            _reconnectBufferedQueue = null;
            while (queue.Count > 0)
            {
                OnRequest(queue.Dequeue());
            }
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 reconnect failed after {0} attempts", _reconnectAttempts);
            if (_reconnectBufferedQueue is { Count: > 0 })
            {
                var exception = new HttpRequestException("HTTP/1.1 reconnect failed after max attempts.");
                RequestFault.FailAll(_reconnectBufferedQueue, exception);
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
        var queueCountBeforeDequeue = _inFlightQueue.Count;

        if (_inFlightQueue.Count > 0)
        {
            var request = _inFlightQueue.Dequeue();
            response.RequestMessage = request;
        }

        if (HasConnectionClose(response))
        {
            if (queueCountBeforeDequeue > 1)
            {
                Tracing.For("Protocol").Info(this, "HTTP/1.1: Server sent Connection: close with {0} pipelined requests in-flight — disabling pipelining", queueCountBeforeDequeue);
            }

            _effectivePipelineDepth = 1;
        }

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/1.1: {0}", partialContentResult.ErrorMessage!);
        }

        _ops.OnResponse(response);
    }

    private static bool IsCloseDelimited(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        if (status is >= 100 and < 200 or 204 or 304)
        {
            return false;
        }

        if (response.Headers.TransferEncodingChunked == true)
        {
            return false;
        }

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
}
