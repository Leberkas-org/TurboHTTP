using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerSessionManager
{
    private const int MaxStatePoolCapacity = 1000;

    private readonly IServerStageOperations _ops;
    private readonly ServerStreamResolver _streamResolver = new();
    private readonly Http3ServerDecoder _requestDecoder;
    private readonly Http3ServerEncoder _responseEncoder;
    private readonly QpackTableSync _tableSync;
    private readonly Http3ServerEncoderOptions _encoderOptions;
    private readonly Http3ServerDecoderOptions _decoderOptions;
    private readonly long _maxRequestBodySize;

    private readonly Dictionary<long, (FrameDecoder Decoder, StreamState State)> _streams = new();
    private readonly StackStreamStatePool<StreamState> _statePool;
    private readonly Dictionary<long, BodyRateState> _bodyRateStates = new();

    private bool _controlPrefaceSent;

    public int ActiveStreamCount => _streams.Count;

    public Http3ServerSessionManager(
        Http3ServerEncoderOptions encoderOptions,
        Http3ServerDecoderOptions decoderOptions,
        IServerStageOperations ops,
        long maxRequestBodySize = 30 * 1024 * 1024)
    {
        _encoderOptions = encoderOptions;
        _decoderOptions = decoderOptions;
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _maxRequestBodySize = maxRequestBodySize;

        _tableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: encoderOptions.QpackMaxTableCapacity,
            maxBlockedStreams: 100,
            configuredEncoderLimit: encoderOptions.QpackMaxTableCapacity);

        _requestDecoder = new Http3ServerDecoder(_tableSync, int.MaxValue);
        _responseEncoder = new Http3ServerEncoder(_tableSync);

        var statePoolCapacity = Math.Min(
            decoderOptions.MaxConcurrentStreams > 0 ? decoderOptions.MaxConcurrentStreams : 100,
            MaxStatePoolCapacity);
        _statePool = new StackStreamStatePool<StreamState>(
            statePoolCapacity,
            () => new StreamState());
    }

    public void PreStart()
    {
        _ops.OnOutbound(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));

        var preface = BuildControlPreface();
        _ops.OnOutbound(preface);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        switch (data)
        {
            case ServerStreamAccepted { Id: var id }:
                {
                    _streamResolver.OnServerStreamOpened(id);
                    return;
                }

            case MultiplexedData multiplexed:
                {
                    HandleTaggedStreamData(multiplexed);
                    return;
                }

            case StreamReadCompleted { Id.Value: >= 0 } readCompleted:
                {
                    FlushPendingRequest(readCompleted.Id.Value);
                    return;
                }

            case StreamClosed { Id.Value: >= 0 } streamClosed:
                {
                    FlushPendingRequest(streamClosed.Id.Value);
                    return;
                }

            case TransportData rawData:
                {
                    Tracing.For("Protocol").Warning(this,
                        "Received untagged TransportData — dropping to prevent stream ID misrouting.");
                    rawData.Buffer.Dispose();
                    return;
                }
        }
    }

    public void OnResponse(TurboHttpContext context)
    {
        var streamId = GetStreamIdFromContext(context);

        if (streamId < 0)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/3 response missing stream ID");
            return;
        }

        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/3: Response for unknown stream {0}", streamId);
            return;
        }

        var (_, state) = streamData;

        var headersFrame = _responseEncoder.EncodeHeaders(context);
        EmitDataFrame(headersFrame, streamId);

        var responseFeature = context.Features.Get<IHttpResponseFeature>();
        var contentLength = ExtractContentLength(responseFeature);
        var hasBody = contentLength is not 0;

        if (!hasBody)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        var responseBody = context.Features.Get<ITurboResponseBodyFeature>();
        if (responseBody is not TurboHttpResponseBodyFeature turboBody)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        var bodyStream = turboBody.GetResponseStream();
        var encoder = BodyEncoderFactory.Create(bodyStream, contentLength);
        if (encoder is null)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        state.InitBodyEncoder(encoder);
        state.StartBodyEncoder(bodyStream, streamId, _ops.StageActor);
        _ops.OnScheduleTimer(string.Concat("drain-body:", streamId.ToString()), TimeSpan.FromMilliseconds(0));
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
                if (header.Value.FirstOrDefault() is string value && long.TryParse(value, out var length))
                {
                    return length;
                }
            }
        }

        return null;
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamBodyChunk<long> chunk:
                HandleOutboundBodyChunk(chunk);
                break;

            case StreamBodyComplete<long> complete:
                HandleOutboundBodyComplete(complete.StreamId);
                break;

            case StreamBodyFailed<long> failed:
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3: Response body encoding failed for stream {0}: {1}", failed.StreamId,
                    failed.Reason.Message);
                EmitRstStream(failed.StreamId, ErrorCode.GeneralProtocolError);
                break;
        }
    }

    private void HandleOutboundBodyChunk(StreamBodyChunk<long> chunk)
    {
        if (!_streams.TryGetValue(chunk.StreamId, out var streamData))
        {
            chunk.Owner.Dispose();
            return;
        }

        var (_, state) = streamData;
        state.EnqueueBodyChunk(chunk);
        DrainOutboundBuffer(chunk.StreamId);
    }

    private void HandleOutboundBodyComplete(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;
        state.MarkBodyEncoderComplete();

        if (!state.HasPendingOutbound)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
        }
    }

    public void DrainOutboundBuffer(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        const int maxFrameSize = 16384;

        while (state.PeekBodyChunk() is { } chunk)
        {
            var chunkSize = Math.Min(maxFrameSize, chunk.Length);
            var dataFrame = new DataFrame(chunk.Owner.Memory[..chunkSize]);

            EmitDataFrame(dataFrame, streamId);

            if (chunkSize >= chunk.Length)
            {
                state.TryDequeueBodyChunk(out _);
                chunk.Owner.Dispose();
            }
            else
            {
                break;
            }
        }

        if (state is { HasPendingOutbound: false, IsBodyEncoderComplete: true })
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
        }
    }

    public void FlushAllPendingRequests()
    {
        var streamIds = _streams.Keys.ToList();
        foreach (var streamId in streamIds)
        {
            FlushPendingRequest(streamId);
        }
    }

    public void Cleanup()
    {
        foreach (var (_, (decoder, state)) in _streams)
        {
            decoder.Dispose();
            state.AbortBody();
            state.Reset();
            _statePool.Return(state);
        }

        _streams.Clear();
        _streamResolver.Reset();
        _tableSync.Reset();
    }

    public void CheckBodyRates(int minDataRate, TimeSpan gracePeriod)
    {
        var now = Environment.TickCount64;
        var streamsToReset = new List<long>();

        foreach (var (streamId, state) in _bodyRateStates)
        {
            var elapsedMs = now - state.LastCheckTimestamp;
            if (elapsedMs < 500)
            {
                continue;
            }

            var elapsedSeconds = elapsedMs / 1000.0;
            var bytesTransferred = state.TotalBytes - state.LastCheckBytes;
            var rate = bytesTransferred / elapsedSeconds;

            state.LastCheckBytes = state.TotalBytes;
            state.LastCheckTimestamp = now;

            if (rate < minDataRate)
            {
                if (!state.InGracePeriod)
                {
                    state.InGracePeriod = true;
                    state.GracePeriodStartTimestamp = now;
                }
                else
                {
                    var graceElapsedMs = now - state.GracePeriodStartTimestamp;
                    if (graceElapsedMs > (long)gracePeriod.TotalMilliseconds)
                    {
                        streamsToReset.Add(streamId);
                    }
                }
            }
            else
            {
                state.InGracePeriod = false;
            }
        }

        foreach (var streamId in streamsToReset)
        {
            EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
        }

        if (_bodyRateStates.Count > 0)
        {
            _ops.OnScheduleTimer("body-rate-check", TimeSpan.FromSeconds(1));
        }
    }

    public void EmitRstStream(long streamId, ErrorCode errorCode)
    {
        _ops.OnOutbound(new ResetStream(streamId, (long)errorCode));
        CloseStream(streamId);
    }

    private void HandleTaggedStreamData(MultiplexedData multiplexed)
    {
        var (logicalStreamId, transportBuffer) = _streamResolver.Resolve(multiplexed.StreamId, multiplexed.Buffer);

        if (transportBuffer is null)
        {
            return;
        }

        if (logicalStreamId == CriticalStreamId.ControlId)
        {
            ProcessFrameData(transportBuffer, CriticalStreamId.ControlId);
            return;
        }

        if (logicalStreamId == CriticalStreamId.QpackEncoderId)
        {
            transportBuffer.Dispose();
            return;
        }

        if (logicalStreamId == CriticalStreamId.QpackDecoderId)
        {
            transportBuffer.Dispose();
            return;
        }

        ProcessFrameData(transportBuffer, logicalStreamId);
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            var frameDecoder = new FrameDecoder();
            var streamState = new StreamState();
            streamState.Initialize(streamId);
            streamData = (frameDecoder, streamState);
            _streams[streamId] = streamData;
        }

        var (decoder, state) = streamData;

        var frames = decoder.DecodeAll(buffer.Span, out _);
        buffer.Dispose();

        foreach (var frame in frames)
        {
            try
            {
                switch (frame)
                {
                    case HeadersFrame headersFrame:
                        {
                            var requestFeature = _requestDecoder.DecodeHeadersToFeature(headersFrame, state, endStream: false);
                            if (requestFeature is not null)
                            {
                                state.InitRequestFeature(requestFeature);
                            }
                            else
                            {
                                _ops.OnScheduleTimer(string.Concat("headers-timeout:", streamId.ToString()),
                                    TimeSpan.FromSeconds(30));
                            }

                            break;
                        }

                    case DataFrame dataFrame:
                        {
                            HandleDataFrame(dataFrame, streamId, state);
                            break;
                        }

                    case SettingsFrame:
                    case GoAwayFrame:
                        {
                            break;
                        }
                }
            }
            catch (HttpProtocolException ex)
            {
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3 frame processing error on stream {0}: {1}", streamId, ex.Message);
            }
        }
    }

    private void FlushPendingRequest(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        var requestFeature = state.GetRequestFeature();
        if (requestFeature is not null)
        {
            _ops.OnCancelTimer(string.Concat("headers-timeout:", streamId.ToString()));

            var hasBody = state.HasBodyDecoder;
            if (hasBody)
            {
                state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
                requestFeature.Body = state.GetBodyStream();
            }

            var context = ServerContextFactory.Create(requestFeature, hasBody, _ops.Services, _ops.ConnectionInfo, _ops.TlsHandshakeFeature);
            context.Features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            context.Features.Set<IHttpResetFeature>(new TurboHttpResetFeature(
                errorCode => EmitRstStream(capturedStreamId, (ErrorCode)errorCode)));

            _bodyRateStates.Remove(streamId);
            _ops.OnRequest(context);
        }
    }

    private void HandleDataFrame(DataFrame dataFrame, long streamId, StreamState state)
    {
        if (!state.HasBodyDecoder)
        {
            state.InitBodyDecoder(new StreamingBodyDecoder(_maxRequestBodySize));

            if (!_bodyRateStates.ContainsKey(streamId))
            {
                _bodyRateStates[streamId] = new BodyRateState();
                _ops.OnScheduleTimer("body-rate-check", TimeSpan.FromSeconds(1));
            }
        }

        try
        {
            state.FeedBody(dataFrame.Data.Span, endStream: false);
        }
        catch (HttpProtocolException)
        {
            state.AbortBody();
            EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
            return;
        }

        if (!dataFrame.Data.IsEmpty)
        {
            _bodyRateStates[streamId].TotalBytes += dataFrame.Data.Length;
        }
    }

    private long GetStreamIdFromContext(TurboHttpContext context)
    {
        var streamIdFeature = context.Features.Get<IHttpStreamIdFeature>();
        if (streamIdFeature is not null)
        {
            return streamIdFeature.StreamId;
        }

        return -1L;
    }

    private void CloseStream(long streamId)
    {
        _bodyRateStates.Remove(streamId);

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            var (decoder, state) = streamData;

            decoder.Dispose();
            state.Reset();
            _statePool.Return(state);

            _streams.Remove(streamId);
        }
    }

    private void EmitDataFrame(object frame, long streamId)
    {
        var serialized = frame switch
        {
            HeadersFrame hf => hf.SerializedSize,
            DataFrame df => df.SerializedSize,
            _ => 0
        };

        var buf = TransportBuffer.Rent(serialized);
        var span = buf.FullMemory.Span;

        switch (frame)
        {
            case HeadersFrame hf:
                hf.WriteTo(ref span);
                break;
            case DataFrame df:
                df.WriteTo(ref span);
                break;
        }

        buf.Length = serialized;
        _ops.OnOutbound(new MultiplexedData(buf, streamId));
    }

    private MultiplexedData BuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            throw new InvalidOperationException("Control preface already sent");
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, _encoderOptions.QpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, 100);
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        var buf = TransportBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;

        return new MultiplexedData(buf, CriticalStreamId.Control);
    }
}