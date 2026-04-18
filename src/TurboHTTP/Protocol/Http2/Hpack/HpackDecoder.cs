using System.Buffers;
using System.Text;

namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// RFC 7541 compliant HPACK decoder.
///
/// Implements:
///   §5.1  Integer Representation (with overflow protection)
///   §5.2  String Literal Representation (Huffman + Raw)
///   §6.1  Indexed Header Field
///   §6.2.1 Literal Header Field with Incremental Indexing
///   §6.2.2 Literal Header Field without Indexing
///   §6.2.3 Literal Header Field Never Indexed
///   §6.3  Dynamic Table Size Update (only allowed at the start of a header block)
///   §7.1  Security: Never-Indexed semantics preserved through the decode pipeline
/// </summary>
internal sealed class HpackDecoder
{
    // RFC 7541 §5.1: Maximum integer value = int.MaxValue (2^31-1 = 2147483647)
    private const int MaxIntegerValue = int.MaxValue;

    // RFC 7541 §4.2: Maximum table size is negotiated via SETTINGS_HEADER_TABLE_SIZE
    private int _maxAllowedTableSize = 4096;

    // RFC 9113 §6.5.2 / RFC 7541: MAX_HEADER_LIST_SIZE — maximum cumulative decoded header list size.
    // Size is computed as: sum of (name_bytes + value_bytes + 32) per entry.
    // Default: int.MaxValue (no limit enforced until SETTINGS is received).
    private int _maxHeaderListSize = int.MaxValue;

    // Security: Maximum string literal length for header names and values (prevents resource exhaustion).
    private int _maxStringLength = 65535;

    private readonly HpackDynamicTable _table = new();

    // Reused per-Decode-call header list. Cleared at the start of each Decode() call.
    // Safe to reuse: HTTP/2 processes one header block at a time per connection; Akka back-pressure
    // guarantees the list is consumed before the next Decode() call.
    private readonly List<HpackHeader> _headers = [];

    /// <summary>
    /// Sets the maximum table size allowed by the peer via SETTINGS_HEADER_TABLE_SIZE.
    /// RFC 7541 §4.2: Table size updates inside a header block must not exceed this value.
    /// </summary>
    public void SetMaxAllowedTableSize(int size)
    {
        if (size < 0)
        {
            throw new HpackException($"Invalid SETTINGS_HEADER_TABLE_SIZE: {size}");
        }

        _maxAllowedTableSize = size;
    }

    /// <summary>
    /// Sets the MAX_HEADER_LIST_SIZE limit (RFC 9113 §6.5.2).
    /// When the cumulative decoded header list size (name + value + 32 per entry) exceeds
    /// this value, <see cref="HpackException"/> is thrown (COMPRESSION_ERROR — connection error).
    /// </summary>
    public void SetMaxHeaderListSize(int size)
    {
        if (size < 0)
        {
            throw new HpackException($"Invalid MAX_HEADER_LIST_SIZE: {size}");
        }

        _maxHeaderListSize = size;
    }

    /// <summary>
    /// Sets the maximum allowed length for literal header name and value strings.
    /// Strings exceeding this limit throw <see cref="HpackException"/> (COMPRESSION_ERROR).
    /// </summary>
    public void SetMaxStringLength(int maxLength)
    {
        if (maxLength < 0)
        {
            throw new HpackException($"Invalid max string length: {maxLength}");
        }

        _maxStringLength = maxLength;
    }

    /// <summary>
    /// Decodes an HPACK-encoded header block.
    /// </summary>
    /// <param name="data">Raw HPACK bytes.</param>
    /// <returns>List of decoded header fields as <see cref="HpackHeader"/>.</returns>
    /// <exception cref="HpackException">Thrown on any RFC 7541 protocol violation.</exception>
    public List<HpackHeader> Decode(ReadOnlySpan<byte> data)
    {
        _headers.Clear();
        var pos = 0;

        // RFC 9113 §6.5.2: Track cumulative header list size for MAX_HEADER_LIST_SIZE enforcement.
        // Size per entry = name_bytes + value_bytes + 32 (RFC 7541 §4.1 overhead).
        long cumulativeHeaderListSize = 0;

        // RFC 7541 §6.3: Table size updates must appear at the start of a header block.
        // Once a non-update entry is encountered, no further size updates are permitted.
        var tableSizeUpdateAllowed = true;

        while (pos < data.Length)
        {
            var b = data[pos];

            // RFC 7541 §6.1: Indexed Header Field - bit pattern: 1xxxxxxx
            if ((b & 0x80) != 0)
            {
                tableSizeUpdateAllowed = false;
                var idx = ReadInteger(data, ref pos, 7);
                // Use LookupWithSizes to retrieve the cached encoded size —
                // zero GetByteCount calls for both static (pre-computed) and dynamic (cached) entries.
                var (header, _, encodedSize) = LookupWithSizes(idx);
                CheckHeaderListSizeFromEncoded(ref cumulativeHeaderListSize, encodedSize);
                _headers.Add(header);
            }
            // RFC 7541 §6.2.1: Literal with Incremental Indexing - bit pattern: 01xxxxxx
            else if ((b & 0x40) != 0)
            {
                tableSizeUpdateAllowed = false;
                var (header, nbl, vbl) = ReadLiteralHeaderWithLengths(data, ref pos, prefixBits: 6, neverIndex: false);
                CheckHeaderListSize(ref cumulativeHeaderListSize, nbl, vbl);
                _table.Add(header.Name, header.Value);
                _headers.Add(header);
            }
            // RFC 7541 §6.3: Dynamic Table Size Update - bit pattern: 001xxxxx
            else if ((b & 0x20) != 0)
            {
                // RFC 7541 §6.3: Size update after a header field is a protocol error
                if (!tableSizeUpdateAllowed)
                {
                    throw new HpackException(
                        "RFC 7541 §6.3 violation: Dynamic Table Size Update is not allowed after header fields.");
                }

                var newSize = ReadInteger(data, ref pos, 5);

                // RFC 7541 §4.2: New size must not exceed SETTINGS_HEADER_TABLE_SIZE
                if (newSize > _maxAllowedTableSize)
                {
                    throw new HpackException(
                        $"RFC 7541 §4.2 violation: Table Size Update ({newSize}) exceeds " +
                        $"SETTINGS_HEADER_TABLE_SIZE ({_maxAllowedTableSize}).");
                }

                _table.SetMaxSize(newSize);
            }
            // RFC 7541 §6.2.3: Never Indexed - bit pattern: 0001xxxx
            else if ((b & 0x10) != 0)
            {
                tableSizeUpdateAllowed = false;
                // NeverIndex = true: intermediaries must not add this header to any dynamic table
                var (header, nbl, vbl) = ReadLiteralHeaderWithLengths(data, ref pos, prefixBits: 4, neverIndex: true);
                CheckHeaderListSize(ref cumulativeHeaderListSize, nbl, vbl);
                _headers.Add(header);
            }
            // RFC 7541 §6.2.2: Literal without Indexing - bit pattern: 0000xxxx
            else
            {
                tableSizeUpdateAllowed = false;
                var (header, nbl, vbl) = ReadLiteralHeaderWithLengths(data, ref pos, prefixBits: 4, neverIndex: false);
                CheckHeaderListSize(ref cumulativeHeaderListSize, nbl, vbl);
                _headers.Add(header);
            }
        }

        return _headers;
    }

    /// <summary>
    /// Accumulates the entry's contribution to the header list size and throws
    /// <see cref="HpackException"/> if the cumulative total exceeds <see cref="_maxHeaderListSize"/>.
    /// RFC 9113 §6.5.2: size = name_octets + value_octets + 32 per entry.
    /// Uses pre-computed byte lengths from <see cref="ReadStringWithLength"/> to avoid redundant
    /// <see cref="Encoding.UTF8"/> GetByteCount calls.
    /// </summary>
    private void CheckHeaderListSize(ref long cumulative, int nameByteLength, int valueByteLength)
    {
        if (_maxHeaderListSize == int.MaxValue)
        {
            return;
        }

        cumulative += nameByteLength + valueByteLength + 32;

        if (cumulative > _maxHeaderListSize)
        {
            throw new HpackException(
                $"RFC 9113 §6.5.2 violation: Header list size {cumulative} exceeds " +
                $"MAX_HEADER_LIST_SIZE ({_maxHeaderListSize}) — COMPRESSION_ERROR.");
        }
    }

    /// <summary>
    /// Overload for indexed header fields where the total encoded size (nameBytes + valueBytes + 32)
    /// is already known from the pre-computed static table or the cached dynamic table tuple.
    /// Avoids any <see cref="Encoding.UTF8"/> GetByteCount call.
    /// </summary>
    private void CheckHeaderListSizeFromEncoded(ref long cumulative, int encodedSize)
    {
        if (_maxHeaderListSize == int.MaxValue)
        {
            return;
        }

        cumulative += encodedSize;

        if (cumulative > _maxHeaderListSize)
        {
            throw new HpackException(
                $"RFC 9113 §6.5.2 violation: Header list size {cumulative} exceeds " +
                $"MAX_HEADER_LIST_SIZE ({_maxHeaderListSize}) — COMPRESSION_ERROR.");
        }
    }

    private (HpackHeader Header, int NameByteLength, int ValueByteLength) ReadLiteralHeaderWithLengths(
        ReadOnlySpan<byte> data,
        ref int pos,
        int prefixBits,
        bool neverIndex)
    {
        var idx = ReadInteger(data, ref pos, prefixBits);

        string name;
        int nameByteLength;
        if (idx == 0)
        {
            // Name is provided as a new string literal
            (name, nameByteLength) = ReadStringWithLength(data, ref pos);

            // RFC 7541 §7.2: An empty header name is a protocol error
            if (string.IsNullOrEmpty(name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: Empty header name is not allowed.");
            }
        }
        else
        {
            // Name is referenced from the static or dynamic table.
            // Use LookupWithSizes to retrieve the cached name byte length —
            // zero GetByteCount calls for both static (pre-computed) and dynamic (cached) entries.
            var (looked, cachedNameByteLength, _) = LookupWithSizes(idx);
            name = looked.Name;
            nameByteLength = cachedNameByteLength;
        }

        var (value, valueByteLength) = ReadStringWithLength(data, ref pos);
        return (new HpackHeader(name, value, neverIndex), nameByteLength, valueByteLength);
    }

    /// <summary>
    /// Looks up a header entry by absolute HPACK index and returns the header together with
    /// its pre-computed name byte length and total encoded size (nameBytes + valueBytes + 32).
    /// Static table entries use pre-computed arrays; dynamic entries use the cached tuple.
    /// This avoids any <see cref="Encoding.UTF8"/> GetByteCount call after insertion.
    /// </summary>
    private (HpackHeader Header, int NameByteLength, int EncodedSize) LookupWithSizes(int idx)
    {
        if (idx <= 0)
        {
            throw new HpackException($"RFC 7541 §2.3.3 violation: Invalid index {idx}. Index 0 is reserved.");
        }

        if (idx <= HpackStaticTable.StaticCount)
        {
            var entry = HpackStaticTable.Entries[idx];
            return (new HpackHeader(entry.Name, entry.Value),
                HpackStaticTable.NameByteLengths[idx],
                HpackStaticTable.EncodedSizes[idx]);
        }

        var dynIdx = idx - HpackStaticTable.StaticCount;
        return _table.GetEntryWithSizes(dynIdx)
               ?? throw new HpackException(
                   $"RFC 7541 §2.3.3 violation: Dynamic index {idx} (relative: {dynIdx}) " +
                   $"is out of range (table size: {_table.Count}).");
    }

    /// <summary>
    /// RFC 7541 §5.1 - Integer Representation.
    /// Reads an HPACK-encoded integer with overflow and truncation protection.
    /// </summary>
    internal static int ReadInteger(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        if (pos >= data.Length)
        {
            throw new HpackException("RFC 7541 §5.1 violation: Unexpected end of data while reading integer.");
        }

        var mask = (1 << prefixBits) - 1;
        var value = data[pos] & mask;
        pos++;

        // Value fits within the prefix bits - done
        if (value < mask)
        {
            return value;
        }

        // Multi-byte integer decoding — use long to detect overflow before truncating to int
        var shift = 0;
        long lvalue = value;
        while (true)
        {
            // RFC 7541 §5.1: Truncated integer is a protocol error
            if (pos >= data.Length)
            {
                throw new HpackException("RFC 7541 §5.1 violation: Integer is truncated (no stop bit found).");
            }

            // Security: reject excessively long integer encodings before long shift overflows
            if (shift >= 62)
            {
                throw new HpackException("RFC 7541 §5.1 violation: Integer overflow - encoding length exceeded.");
            }

            var b = data[pos++];
            lvalue += (long)(b & 0x7F) << shift;
            shift += 7;

            if (lvalue > MaxIntegerValue)
            {
                throw new HpackException($"RFC 7541 §5.1 violation: Integer overflow - value {lvalue} " +
                                         $"exceeds maximum {MaxIntegerValue}.");
            }

            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return (int)lvalue;
    }

    /// <summary>
    /// RFC 7541 §5.2 - String Literal Representation.
    /// Supports both Huffman-encoded and raw strings.
    /// Returns the decoded string and its UTF-8 byte length (for header list size accounting).
    /// </summary>
    private (string Value, int ByteLength) ReadStringWithLength(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length)
        {
            throw new HpackException("RFC 7541 §5.2 violation: Unexpected end of data while reading string.");
        }

        var huffman = (data[pos] & 0x80) != 0;
        var length = ReadInteger(data, ref pos, 7);

        if (length < 0)
        {
            throw new HpackException($"RFC 7541 §5.2 violation: Invalid string length {length}.");
        }

        // Security: reject string literals that exceed the configured maximum length.
        if (length > _maxStringLength)
        {
            throw new HpackException(
                $"RFC 7541 §5.2 violation: String literal length {length} exceeds maximum {_maxStringLength} " +
                $"— COMPRESSION_ERROR.");
        }

        if (pos + length > data.Length)
        {
            throw new HpackException($"RFC 7541 §5.2 violation: String length {length} exceeds available data " +
                                     $"(available: {data.Length - pos}).");
        }

        var strBytes = data[pos..(pos + length)];
        pos += length;

        if (huffman)
        {
            var maxDecoded = HuffmanCodec.GetMaxDecodedLength(strBytes.Length);
            using var owner = MemoryPool<byte>.Shared.Rent(maxDecoded);
            var decodedLen = HuffmanCodec.Decode(strBytes, owner.Memory.Span[..maxDecoded]);
            return (Encoding.UTF8.GetString(owner.Memory.Span[..decodedLen]), decodedLen);
        }

        // Non-Huffman: use Span overload directly — avoids intermediate byte[] allocation.
        return (Encoding.UTF8.GetString(strBytes), length);
    }
}