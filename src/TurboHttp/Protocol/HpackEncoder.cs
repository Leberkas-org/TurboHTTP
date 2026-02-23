using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace TurboHttp.Protocol;

// ============================================================================
// RFC 7541 – HPACK Encoder
// ============================================================================

/// <summary>
/// Encoding strategy for a single header field.
/// Controls how the encoder serializes a given header.
/// </summary>
public enum HpackEncoding
{
    /// <summary>
    /// RFC 7541 §6.2.1 – Literal with Incremental Indexing.
    /// The header is added to the dynamic table.
    /// Default strategy for most headers.
    /// </summary>
    IncrementalIndexing,

    /// <summary>
    /// RFC 7541 §6.2.2 – Literal without Indexing.
    /// The header is NOT added to any table.
    /// Useful for one-shot values such as Content-Length or Date.
    /// </summary>
    WithoutIndexing,

    /// <summary>
    /// RFC 7541 §6.2.3 – Literal Never Indexed.
    /// The header MUST NOT be indexed by any intermediary.
    /// Mandatory for security-sensitive fields: Authorization, Cookie, Set-Cookie.
    /// </summary>
    NeverIndexed,
}

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
///   - Writes into an <see cref="IBufferWriter{T}"/> → zero-copy, no intermediate allocation
///   - Maintains its own dynamic table in sync with the peer decoder
///   - Sensitive headers (Authorization, Cookie, Set-Cookie, Proxy-Authorization)
///     are automatically promoted to NeverIndexed (RFC 7541 §7.1)
///   - Huffman encoding is opt-in per Encode() call
/// </summary>
public sealed class HpackEncoder
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

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

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
    /// Encodes a list of header fields into the provided <see cref="IBufferWriter{byte}"/>.
    /// </summary>
    /// <param name="headers">Headers to encode.</param>
    /// <param name="output">Destination buffer writer.</param>
    /// <param name="useHuffman">
    ///   When true, string literals are Huffman-encoded (RFC 7541 §5.2).
    ///   Typically saves 20–30 % compared to raw ASCII.
    /// </param>
    public void Encode(IReadOnlyList<HpackHeader> headers, IBufferWriter<byte> output,
        bool useHuffman = true)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(output);

        // RFC 7541 §6.3: emit pending table size update BEFORE any header field
        if (_pendingTableSizeUpdate.HasValue)
        {
            WriteTableSizeUpdate(_pendingTableSizeUpdate.Value, output);
            _pendingTableSizeUpdate = null;
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrEmpty(header.Name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: empty header name is not allowed.");
            }

            EncodeHeader(header, output, useHuffman);
        }
    }

    /// <summary>
    /// Encodes a list of header tuples and returns the encoded bytes.
    /// Backward-compatible overload for Http2Encoder and Http2SizePredictor.
    /// </summary>
    /// <param name="headers">Headers as (name, value) tuples.</param>
    /// <returns>HPACK-encoded header block.</returns>
    public ReadOnlyMemory<byte> Encode(IReadOnlyList<(string Name, string Value)> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var output = new ArrayBufferWriter<byte>(256);

        // RFC 7541 §6.3: emit pending table size update BEFORE any header field
        if (_pendingTableSizeUpdate.HasValue)
        {
            WriteTableSizeUpdate(_pendingTableSizeUpdate.Value, output);
            _pendingTableSizeUpdate = null;
        }

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new HpackException("RFC 7541 §7.2 violation: empty header name is not allowed.");
            }

            var header = new HpackHeader(name, value);
            EncodeHeader(header, output, _defaultUseHuffman);
        }

        return output.WrittenMemory;
    }

    // -------------------------------------------------------------------------
    // Header encoding strategies
    // -------------------------------------------------------------------------
    private void EncodeHeader(HpackHeader header, IBufferWriter<byte> output, bool useHuffman)
    {
        // Automatically upgrade sensitive headers to NeverIndexed (RFC 7541 §7.1)
        var encoding = header.NeverIndex || SensitiveHeaders.Contains(header.Name)
            ? HpackEncoding.NeverIndexed
            : HpackEncoding.IncrementalIndexing;

        // 1. Try full match in static table (name + value)
        var staticFullIdx = FindStaticFullMatch(header.Name, header.Value);
        if (staticFullIdx > 0)
        {
            // RFC 7541 §6.1 – Indexed Header Field: cheapest possible encoding
            // Only emit as indexed if the header is not sensitive
            if (encoding != HpackEncoding.NeverIndexed)
            {
                WriteIndexed(staticFullIdx, output);
                return;
            }
        }

        // 2. Try full match in dynamic table (name + value)
        var dynamicFullIdx = FindDynamicFullMatch(header.Name, header.Value);
        if (dynamicFullIdx > 0 && encoding != HpackEncoding.NeverIndexed)
        {
            WriteIndexed(dynamicFullIdx, output);
            return;
        }

        // 3. Try name-only match to use indexed name + literal value
        var staticNameIdx = staticFullIdx > 0 ? staticFullIdx : FindStaticNameMatch(header.Name);
        var dynamicNameIdx = dynamicFullIdx > 0 ? dynamicFullIdx : FindDynamicNameMatch(header.Name);

        // Prefer the static table index when both match (RFC 7541 §2.3.2)
        var nameIdx = staticNameIdx > 0 ? staticNameIdx : dynamicNameIdx;

        WriteLiteral(header, nameIdx, encoding, output, useHuffman);
    }

    // -------------------------------------------------------------------------
    // Wire format writers
    // -------------------------------------------------------------------------

    /// <summary>
    /// RFC 7541 §6.1 – Indexed Header Field.
    /// Bit pattern: 1xxxxxxx
    /// </summary>
    private static void WriteIndexed(int index, IBufferWriter<byte> output)
    {
        WriteInteger(index, prefixBits: 7, prefixFlags: 0x80, output);
    }

    /// <summary>
    /// RFC 7541 §6.2.1 / §6.2.2 / §6.2.3 – Literal Header Field.
    /// </summary>
    private void WriteLiteral(
        HpackHeader header,
        int nameIndex,
        HpackEncoding encoding,
        IBufferWriter<byte> output,
        bool useHuffman)
    {
        // First byte encodes the representation type and name index prefix
        switch (encoding)
        {
            case HpackEncoding.IncrementalIndexing:
                // RFC 7541 §6.2.1 – bit pattern: 01xxxxxx, prefix 6 bits
                WriteInteger(nameIndex, prefixBits: 6, prefixFlags: 0x40, output);
                break;

            case HpackEncoding.WithoutIndexing:
                // RFC 7541 §6.2.2 – bit pattern: 0000xxxx, prefix 4 bits
                WriteInteger(nameIndex, prefixBits: 4, prefixFlags: 0x00, output);
                break;

            case HpackEncoding.NeverIndexed:
                // RFC 7541 §6.2.3 – bit pattern: 0001xxxx, prefix 4 bits
                WriteInteger(nameIndex, prefixBits: 4, prefixFlags: 0x10, output);
                break;

            default:
                throw new HpackException($"Unknown HpackEncoding value: {encoding}");
        }

        // When nameIndex == 0, emit the name as a string literal
        if (nameIndex == 0)
        {
            WriteString(header.Name, output, useHuffman);
        }

        // Always emit value as a string literal
        WriteString(header.Value, output, useHuffman);

        // Update dynamic table for IncrementalIndexing only (RFC 7541 §6.2.1)
        if (encoding == HpackEncoding.IncrementalIndexing)
        {
            _table.Add(header.Name, header.Value);
        }
    }

    /// <summary>
    /// RFC 7541 §6.3 – Dynamic Table Size Update.
    /// Bit pattern: 001xxxxx, prefix 5 bits.
    /// </summary>
    private static void WriteTableSizeUpdate(int newSize, IBufferWriter<byte> output)
    {
        WriteInteger(newSize, prefixBits: 5, prefixFlags: 0x20, output);
    }

    // -------------------------------------------------------------------------
    // RFC 7541 §5.1 – Integer Representation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encodes a non-negative integer using HPACK integer representation.
    /// </summary>
    /// <param name="value">The integer value to encode.</param>
    /// <param name="prefixBits">Number of bits available in the first byte (1–8).</param>
    /// <param name="prefixFlags">High bits of the first byte (the representation type flags).</param>
    /// <param name="output">Destination buffer writer.</param>
    internal static void WriteInteger(int value, int prefixBits, byte prefixFlags, IBufferWriter<byte> output)
    {
        if (value < 0)
            throw new HpackException($"RFC 7541 §5.1 violation: integer value must be non-negative, got {value}.");

        if (prefixBits is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");

        var mask = (1 << prefixBits) - 1;

        if (value < mask)
        {
            // Value fits in the prefix – single byte
            var span = output.GetSpan(1);
            span[0] = (byte)(prefixFlags | value);
            output.Advance(1);
            return;
        }

        // Value does not fit – emit prefix byte followed by continuation bytes
        var firstSpan = output.GetSpan(1);
        firstSpan[0] = (byte)(prefixFlags | mask);
        output.Advance(1);

        var remaining = value - mask;

        while (remaining >= 0x80)
        {
            var contSpan = output.GetSpan(1);
            contSpan[0] = (byte)((remaining & 0x7F) | 0x80); // set continuation bit
            output.Advance(1);
            remaining >>= 7;
        }

        // Final byte: no continuation bit
        var lastSpan = output.GetSpan(1);
        lastSpan[0] = (byte)remaining;
        output.Advance(1);
    }

    // -------------------------------------------------------------------------
    // RFC 7541 §5.2 – String Literal Representation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encodes a string as an HPACK string literal.
    /// When <paramref name="useHuffman"/> is true, compares the Huffman-encoded
    /// length against the raw length and picks whichever is shorter (RFC 7541 §5.2).
    /// </summary>
    private static void WriteString(string value, IBufferWriter<byte> output, bool useHuffman)
    {
        var rawBytes = Encoding.UTF8.GetBytes(value);

        if (useHuffman)
        {
            var huffmanBytes = HuffmanCodec.Encode(rawBytes);

            // RFC 7541 §5.2: only use Huffman if it actually saves bytes
            if (huffmanBytes.Length < rawBytes.Length)
            {
                // Huffman flag: H bit = 1
                WriteInteger(huffmanBytes.Length, prefixBits: 7, prefixFlags: 0x80, output);
                var span = output.GetSpan(huffmanBytes.Length);
                huffmanBytes.CopyTo(span);
                output.Advance(huffmanBytes.Length);
                return;
            }
        }

        // Raw string: H bit = 0
        WriteInteger(rawBytes.Length, prefixBits: 7, prefixFlags: 0x00, output);
        var rawSpan = output.GetSpan(rawBytes.Length);
        rawBytes.CopyTo(rawSpan);
        output.Advance(rawBytes.Length);
    }

    /// <summary>
    /// Searches the static table for an entry matching both name and value.
    /// Returns the 1-based static table index, or 0 if not found.
    /// </summary>
    private static int FindStaticFullMatch(string name, string value)
    {
        for (var i = 1; i <= HpackStaticTable.StaticCount; i++)
        {
            var entry = HpackStaticTable.Entries[i];
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Searches the static table for an entry matching the name only.
    /// Returns the 1-based static table index of the first match, or 0 if not found.
    /// </summary>
    private static int FindStaticNameMatch(string name)
    {
        for (var i = 1; i <= HpackStaticTable.StaticCount; i++)
        {
            if (string.Equals(HpackStaticTable.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
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