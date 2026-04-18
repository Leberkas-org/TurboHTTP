using System.Buffers;
using System.Text;

namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// RFC 7541-compliant HPACK encoder.
///
/// Implements:
///   §5.1  Integer Representation
///   §5.2  String Literal Representation (raw and Huffman)
///   §6.1  Indexed Header Field Representation
///   §6.2.1 Literal Header Field with Incremental Indexing
///   §6.2.2 Literal Header Field without Indexing
///   §6.2.3 Literal Header Field Never Indexed
///   §6.3  Dynamic Table Size Update
///   §7.1  Security: automatic Never-Indexed for sensitive header names
///
/// Design decisions:
///   - Writes into a caller-provided <c>ref Span&lt;byte&gt;</c> → zero-copy, no intermediate allocation
///   - Maintains its own dynamic table in sync with the peer decoder
///   - Sensitive headers (Authorization, Cookie, Set-Cookie, Proxy-Authorization)
///     are automatically promoted to NeverIndexed (RFC 7541 §7.1)
///   - Huffman encoding is opt-in per Encode() call
/// </summary>
internal sealed class HpackEncoder
{
    // RFC 7541 §7.1 – headers that must never be indexed
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
    };

    // RFC 7541 §4.2 – default dynamic table size
    private int _maxTableSize = 4096;

    // Pending table size update to emit at the start of the next header block (RFC 7541 §6.3)
    private int? _pendingTableSizeUpdate;

    private readonly HpackDynamicTable _table = new();

    // Default Huffman encoding setting for backward compatibility
    private readonly bool _defaultUseHuffman;

    /// <summary>
    /// Creates a new HpackEncoder with optional default Huffman encoding.
    /// </summary>
    /// <param name="useHuffman">Default Huffman encoding setting for Encode overloads that don't specify it.</param>
    public HpackEncoder(bool useHuffman = true)
    {
        _defaultUseHuffman = useHuffman;
    }

    /// <summary>
    /// Notifies the encoder that the peer has acknowledged a new
    /// SETTINGS_HEADER_TABLE_SIZE value.
    /// RFC 7541 §6.3: the encoder MUST emit a Dynamic Table Size Update
    /// at the start of the next header block.
    /// </summary>
    public void AcknowledgeTableSizeChange(int newMaxSize)
    {
        if (newMaxSize < 0)
        {
            throw new HpackException($"Invalid SETTINGS_HEADER_TABLE_SIZE: {newMaxSize}");
        }

        _maxTableSize = newMaxSize;
        _pendingTableSizeUpdate = newMaxSize;
        _table.SetMaxSize(newMaxSize);
    }

    /// <summary>
    /// Encodes a list of header fields into the provided span.
    /// The span is advanced past the written bytes on return.
    /// </summary>
    /// <param name="headers">Headers to encode.</param>
    /// <param name="output">Destination span; advanced past the bytes written on return.</param>
    /// <param name="useHuffman">
    ///   When true, string literals are Huffman-encoded (RFC 7541 §5.2).
    ///   Typically saves 20–30 % compared to raw ASCII.
    /// </param>
    /// <returns>Number of bytes written.</returns>
    public int Encode(IReadOnlyList<HpackHeader> headers, ref Span<byte> output,
        bool useHuffman = true)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var totalWritten = 0;

        // RFC 7541 §6.3: emit pending table size update BEFORE any header field
        if (_pendingTableSizeUpdate.HasValue)
        {
            totalWritten += WriteTableSizeUpdate(_pendingTableSizeUpdate.Value, ref output);
            _pendingTableSizeUpdate = null;
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrEmpty(header.Name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: empty header name is not allowed.");
            }

            totalWritten += EncodeHeader(header, ref output, useHuffman);
        }

        return totalWritten;
    }

    /// <summary>
    /// Encodes a list of header tuples and returns the encoded bytes.
    /// Convenience overload for Http2RequestEncoder and Http2SizePredictor.
    /// Uses MemoryPool for the internal buffer.
    /// </summary>
    /// <param name="headers">Headers as (name, value) tuples.</param>
    /// <returns>HPACK-encoded header block.</returns>
    public ReadOnlyMemory<byte> Encode(IReadOnlyList<(string Name, string Value)> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        // Rent a generous buffer from MemoryPool
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var span = owner.Memory.Span;

        var totalWritten = 0;

        // RFC 7541 §6.3: emit pending table size update BEFORE any header field
        if (_pendingTableSizeUpdate.HasValue)
        {
            totalWritten += WriteTableSizeUpdate(_pendingTableSizeUpdate.Value, ref span);
            _pendingTableSizeUpdate = null;
        }

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: empty header name is not allowed.");
            }

            var header = new HpackHeader(name, value);
            totalWritten += EncodeHeader(header, ref span, _defaultUseHuffman);
        }

        return owner.Memory[..totalWritten].ToArray();
    }

    private int EncodeHeader(HpackHeader header, ref Span<byte> output, bool useHuffman)
    {
        // Automatically upgrade sensitive headers to NeverIndexed (RFC 7541 §7.1)
        var encoding = header.NeverIndex || SensitiveHeaders.Contains(header.Name)
            ? HpackEncoding.NeverIndexed
            : HpackEncoding.IncrementalIndexing;

        // 1. Try full match in static table (name + value)
        var staticFullIdx = FindStaticFullMatch(header.Name, header.Value);
        if (staticFullIdx > 0 && encoding != HpackEncoding.NeverIndexed)
        {
            // RFC 7541 §6.1 – Indexed Header Field: cheapest possible encoding
            // Only emit as indexed if the header is not sensitive
            return WriteIndexed(staticFullIdx, ref output);
        }

        // 2. Try full match in dynamic table (name + value)
        var dynamicFullIdx = FindDynamicFullMatch(header.Name, header.Value);
        if (dynamicFullIdx > 0 && encoding != HpackEncoding.NeverIndexed)
        {
            return WriteIndexed(dynamicFullIdx, ref output);
        }

        // 3. Try name-only match to use indexed name + literal value
        var staticNameIdx = staticFullIdx > 0 ? staticFullIdx : FindStaticNameMatch(header.Name);
        var dynamicNameIdx = dynamicFullIdx > 0 ? dynamicFullIdx : FindDynamicNameMatch(header.Name);

        // Prefer the static table index when both match (RFC 7541 §2.3.2)
        var nameIdx = staticNameIdx > 0 ? staticNameIdx : dynamicNameIdx;

        return WriteLiteral(header, nameIdx, encoding, ref output, useHuffman);
    }

    /// <summary>
    /// RFC 7541 §6.1 – Indexed Header Field.
    /// Bit pattern: 1xxxxxxx
    /// </summary>
    private static int WriteIndexed(int index, ref Span<byte> output)
    {
        return WriteInteger(index, prefixBits: 7, prefixFlags: 0x80, ref output);
    }

    /// <summary>
    /// RFC 7541 §6.2.1 / §6.2.2 / §6.2.3 – Literal Header Field.
    /// </summary>
    private int WriteLiteral(HpackHeader header, int nameIndex, HpackEncoding encoding, ref Span<byte> output,
        bool useHuffman)
    {
        var written = 0;

        // First byte encodes the representation type and name index prefix
        switch (encoding)
        {
            case HpackEncoding.IncrementalIndexing:
                // RFC 7541 §6.2.1 – bit pattern: 01xxxxxx, prefix 6 bits
                written += WriteInteger(nameIndex, prefixBits: 6, prefixFlags: 0x40, ref output);
                break;

            case HpackEncoding.WithoutIndexing:
                // RFC 7541 §6.2.2 – bit pattern: 0000xxxx, prefix 4 bits
                written += WriteInteger(nameIndex, prefixBits: 4, prefixFlags: 0x00, ref output);
                break;

            case HpackEncoding.NeverIndexed:
                // RFC 7541 §6.2.3 – bit pattern: 0001xxxx, prefix 4 bits
                written += WriteInteger(nameIndex, prefixBits: 4, prefixFlags: 0x10, ref output);
                break;

            default:
                throw new HpackException($"Unknown HpackEncoding value: {encoding}");
        }

        // When nameIndex == 0, emit the name as a string literal
        if (nameIndex == 0)
        {
            written += WriteString(header.Name, ref output, useHuffman);
        }

        // Always emit value as a string literal
        written += WriteString(header.Value, ref output, useHuffman);

        // Update dynamic table for IncrementalIndexing only (RFC 7541 §6.2.1)
        if (encoding == HpackEncoding.IncrementalIndexing)
        {
            _table.Add(header.Name, header.Value);
        }

        return written;
    }

    /// <summary>
    /// RFC 7541 §6.3 – Dynamic Table Size Update.
    /// Bit pattern: 001xxxxx, prefix 5 bits.
    /// </summary>
    private static int WriteTableSizeUpdate(int newSize, ref Span<byte> output)
    {
        return WriteInteger(newSize, prefixBits: 5, prefixFlags: 0x20, ref output);
    }

    /// <summary>
    /// Encodes a non-negative integer using HPACK integer representation.
    /// Writes directly into the caller-provided span and advances it past the written bytes.
    /// </summary>
    /// <param name="value">The integer value to encode.</param>
    /// <param name="prefixBits">Number of bits available in the first byte (1–8).</param>
    /// <param name="prefixFlags">High bits of the first byte (the representation type flags).</param>
    /// <param name="output">Destination span; advanced past the bytes written on return.</param>
    /// <returns>Number of bytes written.</returns>
    internal static int WriteInteger(int value, int prefixBits, byte prefixFlags, ref Span<byte> output)
    {
        if (value < 0)
        {
            throw new HpackException($"RFC 7541 §5.1 violation: integer value must be non-negative, got {value}.");
        }

        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        var mask = (1 << prefixBits) - 1;

        if (value < mask)
        {
            // Value fits in the prefix – single byte
            output[0] = (byte)(prefixFlags | value);
            output = output[1..];
            return 1;
        }

        // Value does not fit – emit prefix byte followed by continuation bytes
        output[0] = (byte)(prefixFlags | mask);
        output = output[1..];
        var written = 1;

        var remaining = value - mask;

        while (remaining >= 0x80)
        {
            output[0] = (byte)((remaining & 0x7F) | 0x80); // set continuation bit
            output = output[1..];
            remaining >>= 7;
            written++;
        }

        // Final byte: no continuation bit
        output[0] = (byte)remaining;
        output = output[1..];
        written++;

        return written;
    }

    /// <summary>
    /// Encodes a string as an HPACK string literal.
    /// When <paramref name="useHuffman"/> is true, compares the Huffman-encoded
    /// length against the raw length and picks whichever is shorter (RFC 7541 §5.2).
    /// Writes directly into the caller-provided span.
    /// </summary>
    private static int WriteString(string value, ref Span<byte> output, bool useHuffman)
    {
        var rawLength = Encoding.UTF8.GetByteCount(value);

        if (useHuffman && rawLength > 0)
        {
            // Encode UTF-8 directly into the output span (after space for length prefix).
            // We write UTF-8 bytes at the start of output, then compute Huffman.
            // Reserve space for integer prefix (max 6 bytes for length) + Huffman data.
            // Strategy: write UTF-8 into a region of output, compute exact Huffman length,
            // then decide whether to keep Huffman or plain.

            // First, get UTF-8 bytes into a temporary region at the end of the output span
            // to avoid overlap with the length prefix we'll write at the start.
            var maxHuffLen = HuffmanCodec.GetMaxEncodedLength(rawLength);

            // Write UTF-8 bytes into the tail of the output span (past where Huffman result would go)
            // We need: [length prefix][huffman or raw data]
            // Temporarily use the tail of the span for UTF-8 source bytes
            var utf8Start = output.Length - rawLength;
            if (utf8Start < maxHuffLen + 6)
            {
                // Span is tight — fall through to non-Huffman path if Huffman can't possibly help
                // (This is a safety check; in practice, the caller provides ample space)
            }
            else
            {
                var utf8Region = output[utf8Start..];
                Encoding.UTF8.GetBytes(value, utf8Region);

                // Compute exact Huffman length
                var huffLen = HuffmanCodec.GetEncodedLength(utf8Region[..rawLength]);

                if (huffLen < rawLength)
                {
                    // Huffman wins — write length prefix with H bit, then Huffman data
                    var written = WriteInteger(huffLen, prefixBits: 7, prefixFlags: 0x80, ref output);
                    var actualHuffLen = HuffmanCodec.Encode(utf8Region[..rawLength], output[..huffLen]);
                    output = output[actualHuffLen..];
                    return written + actualHuffLen;
                }
            }
        }

        // Non-Huffman (or Huffman rejected): encode directly into the output span.
        var n = WriteInteger(rawLength, prefixBits: 7, prefixFlags: 0x00, ref output);
        if (rawLength > 0)
        {
            Encoding.UTF8.GetBytes(value, output[..rawLength]);
            output = output[rawLength..];
        }

        return n + rawLength;
    }

    /// <summary>
    /// Searches the static table for an entry matching both name and value.
    /// Returns the 1-based static table index, or 0 if not found.
    /// Uses <see cref="HpackStaticTable.NameFirstIndex"/> for O(1) name lookup,
    /// then walks forward over consecutive same-name entries to match the value (O(1) in practice).
    /// </summary>
    private static int FindStaticFullMatch(string name, string value)
    {
        if (!HpackStaticTable.NameFirstIndex.TryGetValue(name, out var firstIdx))
        {
            return 0;
        }

        // Entries sharing the same name are consecutive in the static table.
        // Walk forward until the name changes or a value match is found.
        for (var i = firstIdx; i <= HpackStaticTable.StaticCount; i++)
        {
            var entry = HpackStaticTable.Entries[i];
            if (!string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Searches the static table for an entry matching the name only.
    /// Returns the 1-based static table index of the first match, or 0 if not found.
    /// O(1) via <see cref="HpackStaticTable.NameFirstIndex"/>.
    /// </summary>
    private static int FindStaticNameMatch(string name)
    {
        return HpackStaticTable.NameFirstIndex.GetValueOrDefault(name, 0);
    }

    /// <summary>
    /// Searches the dynamic table for an entry matching both name and value.
    /// Returns the absolute HPACK index (static count + dynamic offset), or 0 if not found.
    /// </summary>
    private int FindDynamicFullMatch(string name, string value)
    {
        for (var i = 1; i <= _table.Count; i++)
        {
            var entry = _table.GetEntry(i);
            if (entry == null)
            {
                break;
            }

            if (string.Equals(entry.Value.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value.Value, value, StringComparison.Ordinal))
            {
                return HpackStaticTable.StaticCount + i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Searches the dynamic table for an entry matching the name only.
    /// Returns the absolute HPACK index, or 0 if not found.
    /// </summary>
    private int FindDynamicNameMatch(string name)
    {
        for (var i = 1; i <= _table.Count; i++)
        {
            var entry = _table.GetEntry(i);
            if (entry == null)
            {
                break;
            }

            if (string.Equals(entry.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return HpackStaticTable.StaticCount + i;
            }
        }

        return 0;
    }
}
