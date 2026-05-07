using Servus.Akka.Transport;

namespace TurboHTTP.Protocol.Http3;

internal readonly record struct ResolvedStream(long LogicalStreamId, TransportBuffer? Buffer);

internal delegate void PushStreamDetected(long quicStreamId, ReadOnlySpan<byte> remaining);

internal sealed class ServerStreamResolver
{
    private readonly Dictionary<long, long> _serverStreamMap = new();
    private readonly HashSet<long> _pendingStreamType = [];
    private readonly HashSet<long> _assignedCriticalStreams = [];

    internal PushStreamDetected? OnPushStreamDetected { get; set; }

    public void OnServerStreamOpened(long quicStreamId)
    {
        if (quicStreamId < 0 || (quicStreamId & 1) == 0)
        {
            return;
        }

        _pendingStreamType.Add(quicStreamId);
    }

    public ResolvedStream Resolve(long quicStreamId, TransportBuffer buffer)
    {
        if (_pendingStreamType.Remove(quicStreamId))
        {
            return ResolveStreamType(quicStreamId, buffer);
        }

        if (_serverStreamMap.TryGetValue(quicStreamId, out var mapped))
        {
            return new ResolvedStream(mapped, buffer);
        }

        return new ResolvedStream(quicStreamId, buffer);
    }

    public void Reset()
    {
        _serverStreamMap.Clear();
        _pendingStreamType.Clear();
        _assignedCriticalStreams.Clear();
    }

    private ResolvedStream ResolveStreamType(long quicStreamId, TransportBuffer buffer)
    {
        var span = buffer.Span;
        if (!QuicVarInt.TryDecode(span, out var rawType, out var typeBytes))
        {
            return new ResolvedStream(quicStreamId, buffer);
        }

        var streamType = (StreamType)rawType;

        if (streamType == StreamType.Push)
        {
            OnPushStreamDetected?.Invoke(quicStreamId, span[typeBytes..]);
            buffer.Dispose();
            return new ResolvedStream(quicStreamId, null);
        }

        long logicalId = streamType switch
        {
            StreamType.Control => CriticalStreamId.Control,
            StreamType.QpackEncoder => CriticalStreamId.QpackEncoder,
            StreamType.QpackDecoder => CriticalStreamId.QpackDecoder,
            _ => quicStreamId
        };

        if (CriticalStreamId.IsCritical(logicalId))
        {
            if (!_assignedCriticalStreams.Add(logicalId))
            {
                throw new Http3Exception(ErrorCode.ClosedCriticalStream,
                    string.Concat("RFC 9114 §6.2.1: Duplicate stream type ", streamType.ToString()));
            }
        }

        _serverStreamMap[quicStreamId] = logicalId;

        var remaining = span.Length - typeBytes;
        if (remaining <= 0)
        {
            buffer.Dispose();
            return new ResolvedStream(logicalId, null);
        }

        var trimmed = TransportBuffer.Rent(remaining);
        span[typeBytes..].CopyTo(trimmed.FullMemory.Span);
        trimmed.Length = remaining;
        buffer.Dispose();
        return new ResolvedStream(logicalId, trimmed);
    }
}
