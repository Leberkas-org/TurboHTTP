using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http3;

internal sealed class QpackStreamManager
{
    private readonly IClientStageOperations _ops;
    private readonly Client.Http3ClientEncoder _requestEncoder;
    private readonly Client.Http3ClientDecoder _responseDecoder;

    private bool _encoderPrefaceSent;
    private bool _decoderPrefaceSent;

    private const int FlushThreshold = 256;
    private byte[]? _pendingInstructions;
    private int _pendingLength;

    public QpackTableSync TableSync { get; }

    public QpackStreamManager(
        IClientStageOperations ops,
        Client.Http3ClientEncoder requestEncoder,
        Client.Http3ClientDecoder responseDecoder,
        QpackTableSync tableSync)
    {
        _ops = ops;
        _requestEncoder = requestEncoder;
        _responseDecoder = responseDecoder;
        TableSync = tableSync;
    }

    public void OpenCriticalStreams(Action<ITransportOutbound> emit)
    {
        emit(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        emit(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        emit(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));
    }

    public void ProcessEncoderInstructions(ReadOnlySpan<byte> data)
    {
        try
        {
            TableSync.ProcessEncoderInstructions(data);
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "QPACK encoder stream error absorbed — {0}", ex.Message);
        }
    }

    public void ProcessDecoderInstructions(ReadOnlySpan<byte> data)
    {
        try
        {
            TableSync.ProcessDecoderInstructions(data);
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "QPACK decoder stream error absorbed — {0}", ex.Message);
        }
    }

    public IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> ProcessEncoderInstructionsAndResolveBlocked(ReadOnlySpan<byte> data)
    {
        try
        {
            TableSync.ProcessEncoderInstructions(data);
            return TableSync.ResolveBlockedStreams();
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "QPACK encoder stream error absorbed — {0}", ex.Message);
            return [];
        }
    }

    public void AccumulateEncoderInstructions()
    {
        var instructions = _requestEncoder.EncoderInstructions;
        if (instructions.Length == 0)
        {
            return;
        }

        var needed = _pendingLength + instructions.Length;
        if (_pendingInstructions is null || _pendingInstructions.Length < needed)
        {
            var newBuf = new byte[Math.Max(needed, FlushThreshold * 2)];
            if (_pendingLength > 0)
            {
                _pendingInstructions.AsSpan(0, _pendingLength).CopyTo(newBuf);
            }
            _pendingInstructions = newBuf;
        }

        instructions.Span.CopyTo(_pendingInstructions.AsSpan(_pendingLength));
        _pendingLength += instructions.Length;
    }

    public void FlushIfNeeded(bool force = false)
    {
        if (_pendingLength == 0)
        {
            return;
        }

        if (!force && _pendingLength < FlushThreshold)
        {
            return;
        }

        FlushPendingEncoderBuffer();
    }

    private void FlushPendingEncoderBuffer()
    {
        if (_pendingLength == 0)
        {
            return;
        }

        var prefaceSize = _encoderPrefaceSent ? 0 : 1;
        var totalLength = prefaceSize + _pendingLength;

        var buf = TransportBuffer.Rent(totalLength);
        var dest = buf.FullMemory.Span;

        if (!_encoderPrefaceSent)
        {
            _encoderPrefaceSent = true;
            dest[0] = (byte)StreamType.QpackEncoder;
            _pendingInstructions.AsSpan(0, _pendingLength).CopyTo(dest[1..]);
        }
        else
        {
            _pendingInstructions.AsSpan(0, _pendingLength).CopyTo(dest);
        }

        buf.Length = totalLength;
        _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.QpackEncoder));
        _pendingLength = 0;
    }

    public void FlushPendingInstructions()
    {
        FlushDecoderInstructions();
        AccumulateEncoderInstructions();
        FlushPendingEncoderBuffer();
    }

    public void FlushEncoderInstructions()
    {
        AccumulateEncoderInstructions();
        FlushPendingEncoderBuffer();
    }

    public void FlushDecoderInstructions()
    {
        var sectionAck = _responseDecoder.DecoderInstructions;

        var buf = TransportBuffer.Rent(1 + sectionAck.Length + 16);
        var dest = buf.FullMemory.Span;
        var offset = 0;

        if (!_decoderPrefaceSent)
        {
            dest[offset++] = (byte)StreamType.QpackDecoder;
        }

        if (sectionAck.Length > 0)
        {
            sectionAck.Span.CopyTo(dest[offset..]);
            offset += sectionAck.Length;
        }

        var icrWriter = SpanWriter.Create(dest[offset..]);
        TableSync.WriteInsertCountIncrement(ref icrWriter);
        offset += icrWriter.BytesWritten;

        if (offset == 0 || (offset == 1 && !_decoderPrefaceSent))
        {
            buf.Dispose();
            return;
        }

        _decoderPrefaceSent = true;
        buf.Length = offset;
        _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.QpackDecoder));
    }

    public void ApplyPeerSettings(Settings settings)
    {
        var peerQpackCapacity = settings.QpackMaxTableCapacity;
        if (peerQpackCapacity > 0)
        {
            TableSync.UpdateEncoderCapacity((int)peerQpackCapacity);
            FlushEncoderInstructions();
        }

        TableSync.RemoteMaxFieldSectionSize = settings.MaxFieldSectionSize;
    }

    public void Reset()
    {
        _encoderPrefaceSent = false;
        _decoderPrefaceSent = false;
        _pendingLength = 0;
    }
}
