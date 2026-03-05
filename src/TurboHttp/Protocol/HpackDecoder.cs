using System;
using System.Collections.Generic;
using System.Text;

namespace TurboHttp.Protocol;

// ============================================================================
// RFC 7541 - HPACK: Header Compression for HTTP/2
// ============================================================================

/// <summary>
/// Represents a decoded HPACK header field.
/// NeverIndex = true means this header MUST NEVER be added to a dynamic table
/// (RFC 7541 §6.2.3). Applies to security-sensitive fields like Authorization,
/// Cookie, etc.
/// </summary>
public readonly record struct HpackHeader(string Name, string Value, bool NeverIndex = false);

/// <summary>
/// RFC 7541 §4.1 - Dynamic Table.
/// FIFO queue: newest entries at the front, oldest evicted on overflow.
/// Each entry costs: Name.Length + Value.Length + 32 bytes overhead (RFC 7541 §4.1).
/// </summary>
public sealed class HpackDynamicTable
{
    // RFC 7541 §4.2 - Default max size: 4096 bytes
    private int _maxSize = 4096;
    private int _currentSize;
    private readonly LinkedList<HpackHeader> _entries = [];

    /// <summary>Currently configured maximum table size in bytes.</summary>
    public int MaxSize => _maxSize;

    /// <summary>Currently occupied table size in bytes.</summary>
    public int CurrentSize => _currentSize;

    /// <summary>
    /// RFC 7541 §4.2 - Sets the maximum table size.
    /// Triggers eviction of oldest entries if the new limit is exceeded.
    /// </summary>
    public void SetMaxSize(int newMax)
    {
        if (newMax < 0)
        {
            throw new HpackException($"Invalid HPACK table size: {newMax}");
        }

        _maxSize = newMax;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §4.4 - Adds a new entry to the front of the table.
    /// If the entry alone exceeds MaxSize, the entire table is cleared.
    /// </summary>
    public void Add(string name, string value)
    {
        var entrySize = EntrySize(name, value);

        // RFC 7541 §4.4: Entry larger than MaxSize -> evict everything
        if (entrySize > _maxSize)
        {
            Clear();
            return;
        }

        _entries.AddFirst(new HpackHeader(name, value));
        _currentSize += entrySize;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §2.3.3 - Dynamic index is 1-based (relative to the table).
    /// Index 1 = most recently added entry.
    /// </summary>
    public HpackHeader? GetEntry(int dynamicIndex)
    {
        if (dynamicIndex <= 0 || dynamicIndex > _entries.Count)
        {
            return null;
        }

        var node = _entries.First;
        for (var i = 1; i < dynamicIndex; i++)
        {
            node = node!.Next;
        }

        return node!.Value;
    }

    /// <summary>Number of entries currently in the dynamic table.</summary>
    public int Count => _entries.Count;

    private void Evict()
    {
        while (_currentSize > _maxSize && _entries.Count > 0)
        {
            var last = _entries.Last!.Value;
            _currentSize -= EntrySize(last.Name, last.Value);
            _entries.RemoveLast();
        }
    }

    private void Clear()
    {
        _entries.Clear();
        _currentSize = 0;
    }

    // RFC 7541 §4.1: Per-entry overhead is always 32 bytes
    private static int EntrySize(string name, string value)
        => Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) + 32;
}

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
public sealed class HpackDecoder
{
    // RFC 7541 §5.1: Maximum integer value = int.MaxValue (2^31-1 = 2147483647)
    private const int MaxIntegerValue = int.MaxValue;

    // RFC 7541 §4.2: Maximum table size is negotiated via SETTINGS_HEADER_TABLE_SIZE
    private int _maxAllowedTableSize = 4096;

    // RFC 7540 §6.5.2 / RFC 7541: MAX_HEADER_LIST_SIZE — maximum cumulative decoded header list size.
    // Size is computed as: sum of (name_bytes + value_bytes + 32) per entry.
    // Default: int.MaxValue (no limit enforced until SETTINGS is received).
    private int _maxHeaderListSize = int.MaxValue;

    // Security: Maximum string literal length for header names and values (prevents resource exhaustion).
    private int _maxStringLength = 65535;

    private readonly HpackDynamicTable _table = new();

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
    /// Sets the MAX_HEADER_LIST_SIZE limit (RFC 7540 §6.5.2).
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
        var result = new List<HpackHeader>();
        var pos = 0;

        // RFC 7540 §6.5.2: Track cumulative header list size for MAX_HEADER_LIST_SIZE enforcement.
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
                var header = Lookup(idx);
                CheckHeaderListSize(ref cumulativeHeaderListSize, header);
                result.Add(header);
            }
            // RFC 7541 §6.2.1: Literal with Incremental Indexing - bit pattern: 01xxxxxx
            else if ((b & 0x40) != 0)
            {
                tableSizeUpdateAllowed = false;
                var header = ReadLiteralHeader(data, ref pos, prefixBits: 6, neverIndex: false);
                CheckHeaderListSize(ref cumulativeHeaderListSize, header);
                _table.Add(header.Name, header.Value);
                result.Add(header);
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
                var header = ReadLiteralHeader(data, ref pos, prefixBits: 4, neverIndex: true);
                CheckHeaderListSize(ref cumulativeHeaderListSize, header);
                result.Add(header);
            }
            // RFC 7541 §6.2.2: Literal without Indexing - bit pattern: 0000xxxx
            else
            {
                tableSizeUpdateAllowed = false;
                var header = ReadLiteralHeader(data, ref pos, prefixBits: 4, neverIndex: false);
                CheckHeaderListSize(ref cumulativeHeaderListSize, header);
                result.Add(header);
            }
        }

        return result;
    }

    /// <summary>
    /// Accumulates the entry's contribution to the header list size and throws
    /// <see cref="HpackException"/> if the cumulative total exceeds <see cref="_maxHeaderListSize"/>.
    /// RFC 7540 §6.5.2: size = name_octets + value_octets + 32 per entry.
    /// </summary>
    private void CheckHeaderListSize(ref long cumulative, HpackHeader header)
    {
        if (_maxHeaderListSize == int.MaxValue)
        {
            return;
        }

        var entrySize = Encoding.UTF8.GetByteCount(header.Name)
                        + Encoding.UTF8.GetByteCount(header.Value)
                        + 32;
        cumulative += entrySize;

        if (cumulative > _maxHeaderListSize)
        {
            throw new HpackException(
                $"RFC 7540 §6.5.2 violation: Header list size {cumulative} exceeds " +
                $"MAX_HEADER_LIST_SIZE ({_maxHeaderListSize}) — COMPRESSION_ERROR.");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private HpackHeader ReadLiteralHeader(
        ReadOnlySpan<byte> data,
        ref int pos,
        int prefixBits,
        bool neverIndex)
    {
        var idx = ReadInteger(data, ref pos, prefixBits);

        string name;
        if (idx == 0)
        {
            // Name is provided as a new string literal
            name = ReadString(data, ref pos);

            // RFC 7541 §7.2: An empty header name is a protocol error
            if (string.IsNullOrEmpty(name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: Empty header name is not allowed.");
            }
        }
        else
        {
            // Name is referenced from the static or dynamic table
            name = Lookup(idx).Name;
        }

        var value = ReadString(data, ref pos);
        return new HpackHeader(name, value, neverIndex);
    }

    private HpackHeader Lookup(int idx)
    {
        // RFC 7541 §2.3.3: Index 0 is reserved and must never be used
        if (idx <= 0)
        {
            throw new HpackException($"RFC 7541 §2.3.3 violation: Invalid index {idx}. Index 0 is reserved.");
        }

        if (idx <= HpackStaticTable.StaticCount)
        {
            return new HpackHeader(HpackStaticTable.Entries[idx].Name, HpackStaticTable.Entries[idx].Value);
        }

        var dynIdx = idx - HpackStaticTable.StaticCount;
        return _table.GetEntry(dynIdx)
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
    /// </summary>
    private string ReadString(ReadOnlySpan<byte> data, ref int pos)
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

        var rawBytes = huffman
            ? HuffmanCodec.Decode(strBytes)
            : strBytes.ToArray();

        // RFC 7541 §5.2: Header names and values are encoded as UTF-8
        return Encoding.UTF8.GetString(rawBytes);
    }
}

/// <summary>
/// RFC 7541 Appendix A - Static Table.
/// 61 predefined header entries at indices 1-61.
/// Index 0 is reserved and must never be referenced.
/// </summary>
public static class HpackStaticTable
{
    public const int StaticCount = 61;

    // Index 0 is intentionally empty (reserved, RFC 7541 §2.3.3)
    public static readonly (string Name, string Value)[] Entries =
    [
        (string.Empty, string.Empty), // [0]  reserved
        (":authority", string.Empty), // [1]
        (":method", "GET"), // [2]
        (":method", "POST"), // [3]
        (":path", "/"), // [4]
        (":path", "/index.html"), // [5]
        (":scheme", "http"), // [6]
        (":scheme", "https"), // [7]
        (":status", "200"), // [8]
        (":status", "204"), // [9]
        (":status", "206"), // [10]
        (":status", "304"), // [11]
        (":status", "400"), // [12]
        (":status", "404"), // [13]
        (":status", "500"), // [14]
        ("accept-charset", string.Empty), // [15]
        ("accept-encoding", "gzip, deflate"), // [16]
        ("accept-language", string.Empty), // [17]
        ("accept-ranges", string.Empty), // [18]
        ("accept", string.Empty), // [19]
        ("access-control-allow-origin", string.Empty), // [20]
        ("age", string.Empty), // [21]
        ("allow", string.Empty), // [22]
        ("authorization", string.Empty), // [23]
        ("cache-control", string.Empty), // [24]
        ("content-disposition", string.Empty), // [25]
        ("content-encoding", string.Empty), // [26]
        ("content-language", string.Empty), // [27]
        ("content-length", string.Empty), // [28]
        ("content-location", string.Empty), // [29]
        ("content-range", string.Empty), // [30]
        ("content-type", string.Empty), // [31]
        ("cookie", string.Empty), // [32]
        ("date", string.Empty), // [33]
        ("etag", string.Empty), // [34]
        ("expect", string.Empty), // [35]
        ("expires", string.Empty), // [36]
        ("from", string.Empty), // [37]
        ("host", string.Empty), // [38]
        ("if-match", string.Empty), // [39]
        ("if-modified-since", string.Empty), // [40]
        ("if-none-match", string.Empty), // [41]
        ("if-range", string.Empty), // [42]
        ("if-unmodified-since", string.Empty), // [43]
        ("last-modified", string.Empty), // [44]
        ("link", string.Empty), // [45]
        ("location", string.Empty), // [46]
        ("max-forwards", string.Empty), // [47]
        ("proxy-authenticate", string.Empty), // [48]
        ("proxy-authorization", string.Empty), // [49]
        ("range", string.Empty), // [50]
        ("referer", string.Empty), // [51]
        ("refresh", string.Empty), // [52]
        ("retry-after", string.Empty), // [53]
        ("server", string.Empty), // [54]
        ("set-cookie", string.Empty), // [55]
        ("strict-transport-security", string.Empty), // [56]
        ("transfer-encoding", string.Empty), // [57]
        ("user-agent", string.Empty), // [58]
        ("vary", string.Empty), // [59]
        ("via", string.Empty), // [60]
        ("www-authenticate", string.Empty) // [61]
    ];
}

/// <summary>
/// HPACK-specific exception for RFC 7541 protocol violations.
/// </summary>
public sealed class HpackException : Exception
{
    public HpackException(string message) : base(message)
    {
    }

    public HpackException(string message, Exception inner) : base(message, inner)
    {
    }
}