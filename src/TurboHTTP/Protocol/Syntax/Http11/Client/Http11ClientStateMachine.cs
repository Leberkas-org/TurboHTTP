using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientStateMachine : IClientStateMachine
{
    private readonly IClientStageOperations _ops;
    private readonly Http11ClientDecoder _decoder;
    private readonly Http11ClientEncoder _encoder;
    private readonly TurboClientOptions _options;

    private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
    private Queue<HttpRequestMessage>? _reconnectBufferedQueue;
    private readonly int _effectivePipelineDepth;
    private int _reconnectAttempts;
    private TransportOptions? _transportOptions;
    private HttpResponseMessage? _pendingBodyResponse;
    private bool _outboundBodyPending;
    private bool _connectionCloseReceived;

    public bool CanAcceptRequest =>
        _inFlightQueue.Count < _effectivePipelineDepth && !IsReconnecting && !_outboundBodyPending &&
        !_connectionCloseReceived;

    public bool HasInFlightRequests => _inFlightQueue.Count > 0;

    public bool IsReconnecting { get; private set; }

    internal int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectBufferedQueue?.Count ?? 0;
            }

            return _inFlightQueue.Count;
        }
    }

    internal RequestEndpoint Endpoint { get; private set; }

    public Http11ClientStateMachine(
        IClientStageOperations ops,
        TurboClientOptions options)
    {
        _ops = ops;
        _options = options;

        var decoderOpts = new Http11ClientDecoderOptions
        {
            Shared = SharedHttpOptions.Default with
            {
                MaxHeaderBytes = options.Http1.MaxResponseHeadersLength * 1024,
                MaxBufferedBodySize = options.MaxBufferedBodySize,
                MaxStreamedBodySize = options.MaxStreamedBodySize,
            },
            MaxPipelineDepth = options.Http1.MaxPipelineDepth,
        };
        var encoderOpts = new Http11ClientEncoderOptions
        {
            AutoHost = options.Http1.AutoHost,
            AutoAcceptEncoding = options.Http1.AutoAcceptEncoding,
        };

        _decoder = new Http11ClientDecoder(decoderOpts);
        _encoder = new Http11ClientEncoder(encoderOpts);
        _effectivePipelineDepth = decoderOpts.MaxPipelineDepth;
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
            item = TransportBuffer.Rent(HttpMessageSize.Estimate(request, contentLength));
            var span = item.FullMemory.Span;

            item.Length = _encoder.Encode(span, request, _ops.StageActor);
            _ops.OnOutbound(new TransportData(item));

            if (request.Content is not null)
            {
                _outboundBodyPending = true;
            }
        }
        catch (Exception ex)
        {
            item?.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.1 request [{0}]: {1}", request.RequestUri,
                ex.Message);
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
        _decoder.SignalEof();

        if (_pendingBodyResponse is not null)
        {
            CompleteResponse(_pendingBodyResponse);
            _pendingBodyResponse = null;
        }
        else if (_decoder.IsBodyComplete)
        {
            var response = _decoder.GetResponse();
            CompleteResponse(response);
        }

        if (IsReconnecting)
        {
            if (_reconnectBufferedQueue is { Count: > 0 })
            {
                RequestFault.FailAll(_reconnectBufferedQueue,
                    new HttpRequestException("HTTP/1.1 transport closed during reconnect."));
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

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case OutboundBodyChunk chunk:
                // Hand the chunk's pooled buffer straight to the transport — no rent + copy.
                _ops.OnOutbound(new TransportData(TransportBuffer.Wrap(chunk.Owner, chunk.Length)));
                break;

            case OutboundBodyComplete:
                _outboundBodyPending = false;
                break;

            case OutboundBodyFailed failed:
                _outboundBodyPending = false;
                if (_inFlightQueue.Count > 0)
                {
                    var req = _inFlightQueue.Peek();
                    req.Fail(new HttpRequestException("Failed to encode HTTP/1.1 request body.", failed.Reason));
                }

                break;
        }
    }

    public void Cleanup()
    {
        _inFlightQueue.Clear();
        _pendingBodyResponse?.Dispose();
        _pendingBodyResponse = null;
        _outboundBodyPending = false;
        _connectionCloseReceived = false;
        _decoder.Reset();
    }

    private void DecodeResponse(TransportBuffer buffer)
    {
        var data = buffer.Memory.Span;
        try
        {
            while (data.Length > 0)
            {
                var isHead = _inFlightQueue.Count > 0 && _inFlightQueue.Peek().Method == HttpMethod.Head;
                var outcome = _decoder.Feed(data, isHead, out var consumed);
                data = data[consumed..];

                if (outcome == DecodeOutcome.NeedMore)
                {
                    if (_decoder.IsBodyStreaming && _pendingBodyResponse is null)
                    {
                        _pendingBodyResponse = _decoder.GetResponse();
                    }

                    return;
                }

                if (outcome == DecodeOutcome.Complete)
                {
                    var response = _pendingBodyResponse ?? _decoder.GetResponse();
                    _pendingBodyResponse = null;

                    if ((int)response.StatusCode is >= 100 and < 200)
                    {
                        _decoder.Reset();
                        continue;
                    }

                    CompleteResponse(response);
                    _decoder.Reset();
                }
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.1 response: {0}", ex.Message);
            if (_inFlightQueue.Count > 0)
            {
                var req = _inFlightQueue.Dequeue();
                req.Fail(new HttpRequestException("Failed to decode HTTP/1.1 response.", ex));
            }

            _pendingBodyResponse = null;
            _decoder.Reset();
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        if (isGraceful)
        {
            if (_pendingBodyResponse is not null)
            {
                _decoder.SignalEof();
                if (_decoder.IsBodyComplete)
                {
                    CompleteResponse(_pendingBodyResponse);
                }
                else if (_inFlightQueue.Count > 0)
                {
                    var req = _inFlightQueue.Dequeue();
                    req.Fail(new HttpRequestException(
                        "HTTP/1.1 response body truncated: server closed before all bytes were received."));
                }

                _pendingBodyResponse = null;
            }
            else if (_decoder.HasActiveBody)
            {
                if (_decoder.SignalEof())
                {
                    var response = _decoder.GetResponse();
                    CompleteResponse(response);
                }
                else if (_inFlightQueue.Count > 0)
                {
                    var req = _inFlightQueue.Dequeue();
                    req.Fail(new HttpRequestException(
                        "HTTP/1.1 response body truncated: server closed before all bytes were received."));
                }
            }

            _decoder.Reset();
            return;
        }

        if (_pendingBodyResponse is not null)
        {
            _pendingBodyResponse = null;
            _decoder.Reset();
            if (_inFlightQueue.Count > 0)
            {
                var req = _inFlightQueue.Dequeue();
                req.Fail(new HttpRequestException("Connection closed while receiving HTTP/1.1 response body."));
            }
        }

        if (HasInFlightRequests && _options.Http1.MaxReconnectAttempts > 0)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        if (HasInFlightRequests)
        {
            const string message = "Connection was aborted while receiving HTTP/1.1 response.";
            RequestFault.FailAll(_inFlightQueue, new HttpRequestException(message));
            _inFlightQueue.Clear();
            Tracing.For("Protocol").Info(this, "HTTP/1.1: {0}", message);
        }

        _decoder.Reset();
    }

    private void TryDecodeEof()
    {
        try
        {
            if (_pendingBodyResponse is not null)
            {
                CompleteResponse(_pendingBodyResponse);
                _pendingBodyResponse = null;
            }
            else if (_decoder.IsBodyComplete)
            {
                var response = _decoder.GetResponse();
                CompleteResponse(response);
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.1 EOF: {0}", ex.Message);
        }
        finally
        {
            _decoder.Reset();
        }
    }

    private void FailOrphanedRequests()
    {
        if (_inFlightQueue.Count > 0)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.1 connection closed with orphaned requests — failing");
            RequestFault.FailAll(_inFlightQueue,
                new HttpRequestException("HTTP/1.1 connection closed with orphaned requests."));
            _inFlightQueue.Clear();
        }
    }

    private void StartReconnect()
    {
        _reconnectBufferedQueue = new Queue<HttpRequestMessage>(_inFlightQueue);
        _inFlightQueue.Clear();
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;
        _connectionCloseReceived = false;
        _decoder.Reset();

        if (_reconnectBufferedQueue is { Count: > 0 })
        {
            var queue = _reconnectBufferedQueue;
            _reconnectBufferedQueue = null;

            while (queue.Count > 0)
            {
                var req = queue.Dequeue();
                OnRequest(req);
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
                RequestFault.FailAll(_reconnectBufferedQueue,
                    new HttpRequestException("HTTP/1.1 reconnect failed after max attempts."));
                _reconnectBufferedQueue.Clear();
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
        if (_decoder.ConnectionWillClose)
        {
            _connectionCloseReceived = true;
        }

        HttpRequestMessage? request = null;
        if (_inFlightQueue.Count > 0)
        {
            request = _inFlightQueue.Dequeue();
        }

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}