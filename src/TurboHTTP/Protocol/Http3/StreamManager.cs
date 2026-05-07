using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Manages per-stream response assembly, request–response correlation, and
/// frame-decoder / stream-state pooling for an HTTP/3 connection.
/// Extracted from <see cref="StateMachine"/> for single-responsibility.
/// </summary>
internal sealed class StreamManager
{
    private const int MaxPoolSize = 16;
    private const int MaxDecoderPoolSize = 16;

    private readonly IStageOperations _ops;
    private readonly ResponseDecoder _responseDecoder;
    private readonly QpackTableSync _tableSync;

    private readonly Dictionary<long, StreamState> _streams = new();
    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();
    private readonly Stack<StreamState> _statePool = new();

    private readonly Dictionary<long, FrameDecoder> _streamDecoders = new();
    private readonly Stack<FrameDecoder> _decoderPool = new();

    /// <summary>Whether a response was produced during the most recent assembly call.</summary>
    public bool ResponseProduced { get; private set; }

    /// <summary>Whether there are in-flight requests awaiting responses.</summary>
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streams.Count > 0;

    public StreamManager(IStageOperations ops, ResponseDecoder responseDecoder, QpackTableSync tableSync)
    {
        _ops = ops;
        _responseDecoder = responseDecoder;
        _tableSync = tableSync;
    }

    /// <summary>
    /// Decodes a TransportBuffer into HTTP/3 frames using a per-stream decoder.
    /// Each QUIC stream has independent framing, so decoders must not share
    /// partial-frame remainder state across streams.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeServerData(TransportBuffer buffer, long streamId)
    {
        if (!_streamDecoders.TryGetValue(streamId, out var decoder))
        {
            decoder = RentDecoder();
            _streamDecoders[streamId] = decoder;
        }

        var frames = decoder.DecodeAll(buffer.Memory.Span, out _);
        buffer.Dispose();
        return frames;
    }

    /// <summary>
    /// Assembles a response from an HTTP/3 frame (HEADERS or DATA) on the given stream.
    /// </summary>
    public void AssembleResponse(Http3Frame frame, long streamId, RequestEndpoint endpoint)
    {
        ResponseProduced = false;

        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = RentStreamState(streamId);
            _streams[streamId] = state;
        }

        switch (frame)
        {
            case HeadersFrame headers:
                HandleResponseHeaders(headers, state, endpoint);
                break;

            case DataFrame data:
                HandleResponseData(data, state);
                break;
        }
    }

    /// <summary>
    /// Completes response assembly for a specific stream (QUIC FIN on request stream).
    /// </summary>
    public void FlushPendingResponse(long streamId)
    {
        if (_streams.TryGetValue(streamId, out var state) && state.HasResponse)
        {
            EmitResponse(streamId);
        }
    }

    /// <summary>
    /// Fails an in-flight request on the given stream due to a transport error.
    /// Removes the correlation and stream state, and completes the <see cref="PendingRequest"/>
    /// with an exception so the caller's <c>SendAsync</c> or <c>ReadAsStringAsync</c> throws.
    /// Returns true if a correlated request was found and failed.
    /// </summary>
    public void FailInflightRequest(long streamId, Exception exception)
    {
        if (_streams.TryGetValue(streamId, out var state))
        {
            state.Reset();
            if (_statePool.Count < MaxPoolSize)
            {
                _statePool.Push(state);
            }

            _streams.Remove(streamId);
        }

        if (!_correlationMap.Remove(streamId, out var request))
        {
            return;
        }

        OnStreamClosedCallback?.Invoke(streamId);
        ReturnDecoder(streamId);

        if (request.Options.TryGetValue(TcsCorrelation.Key, out var pending))
        {
            pending.TrySetException(exception);
        }
    }

    /// <summary>
    /// Completes all in-progress response assemblies (upstream finish / connection close).
    /// </summary>
    public void FlushAllPendingResponses()
    {
        var streamIds = ArrayPool<long>.Shared.Rent(_streams.Count);
        var streamCount = 0;
        foreach (var key in _streams.Keys)
        {
            streamIds[streamCount++] = key;
        }

        for (var i = 0; i < streamCount; i++)
        {
            if (_streams.TryGetValue(streamIds[i], out var state) && state.HasResponse)
            {
                EmitResponse(streamIds[i]);
            }
        }

        ArrayPool<long>.Shared.Return(streamIds);
    }

    /// <summary>
    /// Resolves blocked streams after QPACK encoder instructions arrive.
    /// </summary>
    public void ResolveBlockedStreams(
        IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> resolved)
    {
        foreach (var (streamId, headers) in resolved)
        {
            if (_streams.TryGetValue(streamId, out var state))
            {
                if (!state.HasResponse)
                {
                    _responseDecoder.AssembleHeaders(headers, state);
                }

                if (state.HasResponse)
                {
                    EmitResponse(streamId);
                }
            }
        }
    }

    /// <summary>
    /// Registers a request correlation for the given stream ID.
    /// </summary>
    public void Correlate(long streamId, HttpRequestMessage request)
    {
        _correlationMap[streamId] = request;
    }

    public bool HasCorrelation(long streamId) => _correlationMap.ContainsKey(streamId);

    /// <summary>
    /// Returns all correlated requests as a list and clears the correlation map.
    /// Used during reconnection to snapshot old correlations for replay.
    /// </summary>
    public List<HttpRequestMessage> SnapshotAndClearCorrelations()
    {
        var result = new List<HttpRequestMessage>(_correlationMap.Count);
        result.AddRange(_correlationMap.Values);
        _correlationMap.Clear();
        return result;
    }

    /// <summary>
    /// Drains and pools all per-stream state. Keeps correlation map intact for reconnect.
    /// </summary>
    public void DrainStreams()
    {
        foreach (var (_, state) in _streams)
        {
            state.Reset();
            if (_statePool.Count < MaxPoolSize)
            {
                _statePool.Push(state);
            }
        }

        _streams.Clear();
    }

    /// <summary>
    /// Resets all frame decoders and returns them to the pool.
    /// </summary>
    public void ResetAllDecoders()
    {
        foreach (var decoder in _streamDecoders.Values)
        {
            decoder.Reset();
            if (_decoderPool.Count < MaxDecoderPoolSize)
            {
                _decoderPool.Push(decoder);
            }
            else
            {
                decoder.Dispose();
            }
        }

        _streamDecoders.Clear();
    }

    /// <summary>
    /// Disposes all owned resources (decoders, stream states, pools).
    /// </summary>
    public void Dispose()
    {
        ResetAllDecoders();

        foreach (var decoder in _decoderPool)
        {
            decoder.Dispose();
        }

        _decoderPool.Clear();

        foreach (var state in _streams.Values)
        {
            state.Reset();
        }

        _streams.Clear();

        while (_statePool.TryPop(out _))
        {
            // Pool entries are already reset — just drain
        }
    }

    private void HandleResponseHeaders(HeadersFrame frame, StreamState state, RequestEndpoint endpoint)
    {
        var result = _tableSync.TryDecodeOrBlock(frame.HeaderBlock, (int)state.StreamId);

        if (result.IsBlocked)
        {
            return;
        }

        _responseDecoder.AssembleHeaders(result.Headers!, state);
        FlushDecoderInstructionsCallback?.Invoke(endpoint);
    }

    private void HandleResponseData(DataFrame frame, StreamState state)
    {
        if (!_responseDecoder.AccumulateData(frame, state))
        {
            Tracing.For("Protocol").Warning(this, "RFC 9114 §4.1 — DATA frame received before HEADERS; dropping.");
        }
    }

    private void EmitResponse(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasResponse)
        {
            return;
        }

        var response = _responseDecoder.CompleteResponse(state);

        if (_correlationMap.Remove(streamId, out var request))
        {
            response.RequestMessage = request;
        }

        ResponseProduced = true;

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "{0}", partialContentResult.ErrorMessage!);
        }

        _ops.OnResponse(response);

        ReturnStreamState(streamId);
    }

    private StreamState RentStreamState(long streamId)
    {
        var state = _statePool.TryPop(out var pooled) ? pooled : new StreamState();
        state.Initialize(streamId);
        return state;
    }

    private void ReturnStreamState(long streamId)
    {
        if (!_streams.Remove(streamId, out var state))
        {
            return;
        }

        OnStreamClosedCallback?.Invoke(streamId);

        state.Reset();
        if (_statePool.Count < MaxPoolSize)
        {
            _statePool.Push(state);
        }

        ReturnDecoder(streamId);
    }

    private FrameDecoder RentDecoder()
    {
        if (_decoderPool.TryPop(out var decoder))
        {
            decoder.Reset();
            return decoder;
        }

        return new FrameDecoder();
    }

    private void ReturnDecoder(long streamId)
    {
        if (!_streamDecoders.Remove(streamId, out var decoder))
        {
            return;
        }

        decoder.Reset();
        if (_decoderPool.Count < MaxDecoderPoolSize)
        {
            _decoderPool.Push(decoder);
        }
        else
        {
            decoder.Dispose();
        }
    }

    /// <summary>
    /// Callback to flush QPACK decoder instructions after header decoding.
    /// Set by <see cref="StateMachine"/> to avoid circular dependency.
    /// </summary>
    internal Action<RequestEndpoint>? FlushDecoderInstructionsCallback { get; set; }

    /// <summary>
    /// Callback invoked when a stream is closed (response emitted).
    /// The StateMachine uses this to update <see cref="StreamTracker"/> and <see cref="ConnectionState"/>.
    /// </summary>
    internal Action<long>? OnStreamClosedCallback { get; set; }
}