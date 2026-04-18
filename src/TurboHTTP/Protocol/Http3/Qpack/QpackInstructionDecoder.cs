using System.Buffers;

namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.3, §4.4 — Stateful decoder for QPACK instruction streams.
///
/// Parses encoder instructions (encoder→decoder stream):
///   - Set Dynamic Table Capacity (§4.3.1): 001xxxxx
///   - Insert With Name Reference (§4.3.2): 1Txxxxxx + value
///   - Insert With Literal Name (§4.3.3):   01Hxxxxx + name + value
///   - Duplicate (§4.3.4):                  000xxxxx
///
/// Parses decoder instructions (decoder→encoder stream):
///   - Section Acknowledgment (§4.4.1):     1xxxxxxx
///   - Stream Cancellation (§4.4.2):        01xxxxxx
///   - Insert Count Increment (§4.4.3):     00xxxxxx
///
/// Maintains a remainder buffer for partial instructions split across reads.
/// When combining remainder + new data, a transient working buffer is rented from
/// <see cref="MemoryPool{T}"/> to avoid per-call heap allocations on the hot path.
/// </summary>
internal sealed class QpackInstructionDecoder : IDisposable
{
    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;

    /// <summary>True if there is unconsumed data from a previous call.</summary>
    public bool HasRemainder => _remainderLength > 0;

    /// <summary>Resets the decoder state, clearing any buffered remainder.</summary>
    public void Reset()
    {
        _remainderOwner?.Dispose();
        _remainderOwner = null;
        _remainderLength = 0;
    }

    /// <summary>Disposes the decoder, returning any pooled remainder buffer to the pool.</summary>
    public void Dispose() => Reset();

    /// <summary>
    /// Attempts to decode one encoder instruction (RFC 9204 §4.3).
    /// </summary>
    /// <param name="data">Input data (appended to any existing remainder).</param>
    /// <param name="instruction">The decoded instruction, or null if more data is needed.</param>
    /// <returns><see cref="QpackDecodeStatus.Success"/> if an instruction was decoded.</returns>
    public QpackDecodeStatus TryDecodeEncoderInstruction(ReadOnlySpan<byte> data, out EncoderInstruction? instruction)
    {
        instruction = null;

        // Build the working span — rent a combined buffer only when we have a remainder to merge
        ReadOnlySpan<byte> span;
        IMemoryOwner<byte>? rentedCombined = null;
        int spanLength;

        if (_remainderLength == 0)
        {
            if (data.Length == 0)
            {
                return QpackDecodeStatus.NeedMoreData;
            }

            // Hot path: no remainder, use incoming data directly (zero allocation)
            span = data;
            spanLength = data.Length;
        }
        else if (data.Length == 0)
        {
            // Only remainder present — parse it as-is
            span = _remainderOwner!.Memory.Span[.._remainderLength];
            spanLength = _remainderLength;
        }
        else
        {
            // Combine remainder + new data into a pooled working buffer
            spanLength = _remainderLength + data.Length;
            rentedCombined = MemoryPool<byte>.Shared.Rent(spanLength);
            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(rentedCombined.Memory.Span);
            data.CopyTo(rentedCombined.Memory.Span[_remainderLength..]);
            span = rentedCombined.Memory.Span[..spanLength];
            _remainderOwner?.Dispose();
            _remainderOwner = null;
            _remainderLength = 0;
        }

        var pos = 0;

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                // §4.3.2 — Insert With Name Reference: 1Txxxxxx
                var isStatic = (firstByte & 0x40) != 0;
                var nameIndex = QpackIntegerCodec.Decode(span, ref pos, 6);
                var value = QpackStringCodec.Decode(span, ref pos, 7);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithNameReference,
                    NameIndex = nameIndex,
                    IsStatic = isStatic,
                    Value = value
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                // §4.3.3 — Insert With Literal Name: 01Hxxxxx
                var name = QpackStringCodec.Decode(span, ref pos, 5);
                var value = QpackStringCodec.Decode(span, ref pos, 7);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithLiteralName,
                    Name = name,
                    Value = value
                };
            }
            else if ((firstByte & 0x20) != 0)
            {
                // §4.3.1 — Set Dynamic Table Capacity: 001xxxxx
                var capacity = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.SetDynamicTableCapacity,
                    IntValue = capacity
                };
            }
            else
            {
                // §4.3.4 — Duplicate: 000xxxxx
                var index = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.Duplicate,
                    IntValue = index
                };
            }

            // Store any leftover bytes in remainder buffer
            if (pos < spanLength)
            {
                _remainderOwner = MemoryPool<byte>.Shared.Rent(spanLength - pos);
                span[pos..].CopyTo(_remainderOwner.Memory.Span);
                _remainderLength = spanLength - pos;
            }
            else
            {
                _remainderOwner?.Dispose();
                _remainderOwner = null;
                _remainderLength = 0;
            }

            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            // Integer or string codec threw because data was truncated — need more data.
            // Store the entire working span to remainder before returning the rented buffer.
            if (spanLength > 0)
            {
                _remainderOwner?.Dispose();
                _remainderOwner = MemoryPool<byte>.Shared.Rent(spanLength);
                span.CopyTo(_remainderOwner.Memory.Span);
                _remainderLength = spanLength;
            }

            return QpackDecodeStatus.NeedMoreData;
        }
        finally
        {
            if (rentedCombined != null)
            {
                rentedCombined.Dispose();
            }
        }
    }

    /// <summary>
    /// Attempts to decode one decoder instruction (RFC 9204 §4.4).
    /// </summary>
    /// <param name="data">Input data (appended to any existing remainder).</param>
    /// <param name="instruction">The decoded instruction, or null if more data is needed.</param>
    /// <returns><see cref="QpackDecodeStatus.Success"/> if an instruction was decoded.</returns>
    public QpackDecodeStatus TryDecodeDecoderInstruction(ReadOnlySpan<byte> data, out DecoderInstruction? instruction)
    {
        instruction = null;

        // Build the working span — rent a combined buffer only when we have a remainder to merge
        ReadOnlySpan<byte> span;
        IMemoryOwner<byte>? rentedCombined = null;
        int spanLength;

        if (_remainderLength == 0)
        {
            if (data.Length == 0)
            {
                return QpackDecodeStatus.NeedMoreData;
            }

            // Hot path: no remainder, use incoming data directly (zero allocation)
            span = data;
            spanLength = data.Length;
        }
        else if (data.Length == 0)
        {
            // Only remainder present — parse it as-is
            span = _remainderOwner!.Memory.Span[.._remainderLength];
            spanLength = _remainderLength;
        }
        else
        {
            // Combine remainder + new data into a pooled working buffer
            spanLength = _remainderLength + data.Length;
            rentedCombined = MemoryPool<byte>.Shared.Rent(spanLength);
            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(rentedCombined.Memory.Span);
            data.CopyTo(rentedCombined.Memory.Span[_remainderLength..]);
            span = rentedCombined.Memory.Span[..spanLength];
            _remainderOwner?.Dispose();
            _remainderOwner = null;
            _remainderLength = 0;
        }

        var pos = 0;

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                // §4.4.1 — Section Acknowledgment: 1xxxxxxx
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 7);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.SectionAcknowledgment,
                    IntValue = streamId
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                // §4.4.2 — Stream Cancellation: 01xxxxxx
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.StreamCancellation,
                    IntValue = streamId
                };
            }
            else
            {
                // §4.4.3 — Insert Count Increment: 00xxxxxx
                var increment = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.InsertCountIncrement,
                    IntValue = increment
                };
            }

            // Store any leftover bytes in remainder buffer
            if (pos < spanLength)
            {
                _remainderOwner = MemoryPool<byte>.Shared.Rent(spanLength - pos);
                span[pos..].CopyTo(_remainderOwner.Memory.Span);
                _remainderLength = spanLength - pos;
            }
            else
            {
                _remainderOwner?.Dispose();
                _remainderOwner = null;
                _remainderLength = 0;
            }

            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            // Store the entire working span to remainder before returning the rented buffer.
            if (spanLength > 0)
            {
                _remainderOwner?.Dispose();
                _remainderOwner = MemoryPool<byte>.Shared.Rent(spanLength);
                span.CopyTo(_remainderOwner.Memory.Span);
                _remainderLength = spanLength;
            }

            return QpackDecodeStatus.NeedMoreData;
        }
        finally
        {
            if (rentedCombined != null)
            {
                rentedCombined.Dispose();
            }
        }
    }

    /// <summary>
    /// Decodes all encoder instructions from the given data.
    /// Partial trailing data is buffered for the next call.
    /// </summary>
    public EncoderInstruction[] DecodeAllEncoderInstructions(ReadOnlySpan<byte> data)
    {
        var results = new List<EncoderInstruction>();
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeEncoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            results.Add(instruction!);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Decodes all decoder instructions from the given data.
    /// Partial trailing data is buffered for the next call.
    /// </summary>
    public DecoderInstruction[] DecodeAllDecoderInstructions(ReadOnlySpan<byte> data)
    {
        var results = new List<DecoderInstruction>();
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeDecoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            results.Add(instruction!);
        }

        return results.ToArray();
    }
}
