using System.Text;
using TurboHttp.Protocol.RFC7541;

namespace TurboHttp.Tests.RFC7541;

public sealed class HpackHeaderBlockDecodingTests
{

    /// <summary>Encodes a raw (non-Huffman) HPACK string literal.</summary>
    private static byte[] RawString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var result = new byte[1 + bytes.Length];
        result[0] = (byte)bytes.Length; // H-bit = 0, length in 7 bits
        bytes.CopyTo(result, 1);
        return result;
    }

    /// <summary>Concatenates multiple byte arrays into one.</summary>
    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var pos = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, pos);
            pos += part.Length;
        }
        return result;
    }


    /// RFC 7541 §6.1 — Static index 2 decodes to :method GET
    [Fact(DisplayName = "RFC7541-6-HD-001: Static index 2 decodes to :method GET")]
    public void Should_DecodeMethodGet_When_StaticIndex2()
    {
        var decoder = new HpackDecoder();
        // 0x82 = 10000010 → index 2 (:method, GET)
        var result = decoder.Decode([0x82]);

        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal("GET", result[0].Value);
    }

    /// RFC 7541 §6.1 — Static index 4 decodes to :path /
    [Fact(DisplayName = "RFC7541-6-HD-002: Static index 4 decodes to :path /")]
    public void Should_DecodePathSlash_When_StaticIndex4()
    {
        var decoder = new HpackDecoder();
        // 0x84 = 10000100 → index 4 (:path, /)
        var result = decoder.Decode([0x84]);

        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/", result[0].Value);
    }

    /// RFC 7541 §6.1 — Static index 7 decodes to :scheme https
    [Fact(DisplayName = "RFC7541-6-HD-003: Static index 7 decodes to :scheme https")]
    public void Should_DecodeSchemeHttps_When_StaticIndex7()
    {
        var decoder = new HpackDecoder();
        // 0x87 = 10000111 → index 7 (:scheme, https)
        var result = decoder.Decode([0x87]);

        Assert.Single(result);
        Assert.Equal(":scheme", result[0].Name);
        Assert.Equal("https", result[0].Value);
    }

    /// RFC 7541 §6.1 — Static index 61 (last static entry) decodes correctly
    [Fact(DisplayName = "RFC7541-6-HD-004: Static index 61 (last static entry) decodes correctly")]
    public void Should_DecodeWwwAuthenticate_When_StaticIndex61()
    {
        var decoder = new HpackDecoder();
        // Index 61 needs multi-byte encoding: 0x7F (prefix full) + 0x02 (61-127=-66... wait)
        // 61 in 7-bit prefix: mask = 0x7F = 127, 61 < 127, so single byte 0x80 | 61 = 0xBD
        var result = decoder.Decode([0xBD]);

        Assert.Single(result);
        Assert.Equal("www-authenticate", result[0].Name);
        Assert.Equal(string.Empty, result[0].Value);
    }

    /// RFC 7541 §6.1 — Multiple indexed entries decoded in sequence
    [Fact(DisplayName = "RFC7541-6-HD-005: Multiple indexed entries decoded in sequence")]
    public void Should_DecodeAllEntries_When_MultipleIndexedHeaders()
    {
        var decoder = new HpackDecoder();
        // 0x82 (:method GET), 0x84 (:path /), 0x87 (:scheme https)
        var result = decoder.Decode([0x82, 0x84, 0x87]);

        Assert.Equal(3, result.Count);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal(":path", result[1].Name);
        Assert.Equal(":scheme", result[2].Name);
    }

    /// RFC 7541 §6.1 — Index 0 in indexed representation throws HpackException (§2.3.3)
    [Fact(DisplayName = "RFC7541-6-HD-006: Index 0 in indexed representation throws HpackException (§2.3.3)")]
    public void Should_ThrowHpackException_When_IndexIsZero()
    {
        var decoder = new HpackDecoder();
        // 0x80 = 10000000 → index 0 (reserved, must throw)
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x80]));
        Assert.Contains("0", ex.Message);
    }

    /// RFC 7541 §6.1 — Index beyond static+dynamic table throws HpackException (§2.3.3)
    [Fact(DisplayName = "RFC7541-6-HD-007: Index beyond static+dynamic table throws HpackException (§2.3.3)")]
    public void Should_ThrowHpackException_When_IndexIsOutOfRange()
    {
        var decoder = new HpackDecoder();
        // Index 100 (dynamic table is empty, so any index > 61 is invalid)
        // Encode index 100: 0x80 | mask, then continuation
        // 7-bit prefix: mask = 127, 100 < 127 → single byte 0x80 | 100 = 0xE4
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xE4]));
        Assert.Contains("out of range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    /// RFC 7541 §6.1 — Literal+indexing new name/value → header decoded and added to dynamic table
    [Fact(DisplayName = "RFC7541-6-HD-010: Literal+indexing new name/value → header decoded and added to dynamic table")]
    public void Should_AddToDynamicTable_When_LiteralIncrementalIndexingNewName()
    {
        var decoder = new HpackDecoder();
        // 0x40 = 01000000 → index 0 (new name), then name string + value string
        var bytes = Concat([0x40], RawString("x-test"), RawString("hello"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal("x-test", result[0].Name);
        Assert.Equal("hello", result[0].Value);
        Assert.False(result[0].NeverIndex);

        // Verify it was added to the dynamic table by referencing it in a second block
        // Dynamic index 62 (first dynamic entry) should resolve to "x-test"
        var result2 = decoder.Decode([0xBE]); // 0x80 | 62 = 0xBE
        Assert.Single(result2);
        Assert.Equal("x-test", result2[0].Name);
        Assert.Equal("hello", result2[0].Value);
    }

    /// RFC 7541 §6.1 — Literal+indexing static name index → name resolved from static table
    [Fact(DisplayName = "RFC7541-6-HD-011: Literal+indexing static name index → name resolved from static table")]
    public void Should_ResolveNameFromStaticTable_When_LiteralIncrementalIndexingStaticNameIndex()
    {
        var decoder = new HpackDecoder();
        // 0x44 = 01000100 → index 4 (:path), then value string "/api"
        var bytes = Concat([0x44], RawString("/api"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/api", result[0].Value);
    }

    /// RFC 7541 §6.1 — After Literal+indexing, dynamic entry indexed in subsequent block
    [Fact(DisplayName = "RFC7541-6-HD-012: After Literal+indexing, dynamic entry indexed in subsequent block")]
    public void Should_ResolveDynamicIndex_When_SubsequentBlockAfterIncrementalIndexing()
    {
        var decoder = new HpackDecoder();

        // First block: add "custom-header: value1" with incremental indexing
        var block1 = Concat([0x40], RawString("custom-header"), RawString("value1"));
        decoder.Decode(block1);

        // Second block: reference dynamic index 62 (first dynamic entry)
        var block2 = new byte[] { 0xBE }; // 0x80 | 62
        var result = decoder.Decode(block2);

        Assert.Single(result);
        Assert.Equal("custom-header", result[0].Name);
        Assert.Equal("value1", result[0].Value);
    }

    /// RFC 7541 §6.1 — Multiple Literal+indexing entries build dynamic table in FIFO order
    [Fact(DisplayName = "RFC7541-6-HD-013: Multiple Literal+indexing entries build dynamic table in FIFO order")]
    public void Should_BuildDynamicTableInFifoOrder_When_MultipleIncrementalEntries()
    {
        var decoder = new HpackDecoder();

        // Add two headers: "h1: v1" then "h2: v2"
        // After: dynamic index 62 = h2, index 63 = h1 (FIFO: newest first)
        var bytes = Concat(
            [0x40], RawString("h1"), RawString("v1"),
            [0x40], RawString("h2"), RawString("v2"));
        decoder.Decode(bytes);

        var result62 = decoder.Decode([0xBE]); // index 62 = h2 (newest)
        var result63 = decoder.Decode([0xBF]); // 0x80 | 63 = 0xBF → index 63 = h1 (older)

        Assert.Equal("h2", result62[0].Name);
        Assert.Equal("h1", result63[0].Name);
    }


    /// RFC 7541 §6.1 — Literal without indexing new name → decoded but NOT in dynamic table
    [Fact(DisplayName = "RFC7541-6-HD-020: Literal without indexing new name → decoded but NOT in dynamic table")]
    public void Should_NotAddToDynamicTable_When_LiteralWithoutIndexingNewName()
    {
        var decoder = new HpackDecoder();
        // 0x00 = 00000000 → index 0 (new name), no indexing
        var bytes = Concat([0x00], RawString("x-temp"), RawString("temp-val"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal("x-temp", result[0].Name);
        Assert.Equal("temp-val", result[0].Value);
        Assert.False(result[0].NeverIndex);

        // Verify NOT in dynamic table: index 62 should throw
        Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
    }

    /// RFC 7541 §6.1 — Literal without indexing static name index → name from static table, not added
    [Fact(DisplayName = "RFC7541-6-HD-021: Literal without indexing static name index → name from static table, not added")]
    public void Should_ResolveNameFromStaticTableAndNotAdd_When_LiteralWithoutIndexingStaticName()
    {
        var decoder = new HpackDecoder();
        // 0x04 = 00000100 → index 4 (:path), no indexing
        var bytes = Concat([0x04], RawString("/no-index"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/no-index", result[0].Value);
        Assert.False(result[0].NeverIndex);
    }

    /// RFC 7541 §6.1 — Literal without indexing sets NeverIndex = false
    [Fact(DisplayName = "RFC7541-6-HD-022: Literal without indexing sets NeverIndex = false")]
    public void Should_SetNeverIndexFalse_When_LiteralWithoutIndexing()
    {
        var decoder = new HpackDecoder();
        var bytes = Concat([0x00], RawString("x-header"), RawString("value"));
        var result = decoder.Decode(bytes);

        Assert.False(result[0].NeverIndex);
    }


    /// RFC 7541 §6.1 — Never indexed new name → NeverIndex = true
    [Fact(DisplayName = "RFC7541-6-HD-030: Never indexed new name → NeverIndex = true")]
    public void Should_SetNeverIndexTrue_When_NeverIndexedNewName()
    {
        var decoder = new HpackDecoder();
        // 0x10 = 00010000 → index 0 (new name), never index
        var bytes = Concat([0x10], RawString("authorization"), RawString("Bearer token"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal("authorization", result[0].Name);
        Assert.Equal("Bearer token", result[0].Value);
        Assert.True(result[0].NeverIndex);
    }

    /// RFC 7541 §6.1 — Never indexed → NOT added to dynamic table
    [Fact(DisplayName = "RFC7541-6-HD-031: Never indexed → NOT added to dynamic table")]
    public void Should_NotAddToDynamicTable_When_NeverIndexed()
    {
        var decoder = new HpackDecoder();
        var bytes = Concat([0x10], RawString("cookie"), RawString("secret=abc"));
        decoder.Decode(bytes);

        // Dynamic table must remain empty — index 62 should throw
        Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
    }

    /// RFC 7541 §6.1 — Never indexed static name index → name from static table, NeverIndex = true
    [Fact(DisplayName = "RFC7541-6-HD-032: Never indexed static name index → name from static table, NeverIndex = true")]
    public void Should_SetNeverIndexTrue_When_NeverIndexedWithStaticNameIndex()
    {
        var decoder = new HpackDecoder();
        // 0x14 = 00010100 → index 4 (:path), never index
        var bytes = Concat([0x14], RawString("/secret"));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/secret", result[0].Value);
        Assert.True(result[0].NeverIndex);
    }


    /// RFC 7541 §6.1 — Table size update to 0 at start → dynamic table cleared
    [Fact(DisplayName = "RFC7541-6-HD-040: Table size update to 0 at start → dynamic table cleared")]
    public void Should_ClearDynamicTable_When_TableSizeUpdateIsZero()
    {
        var decoder = new HpackDecoder();
        // First add an entry
        decoder.Decode(Concat([0x40], RawString("h1"), RawString("v1")));

        // Size update to 0 (0x20 = 001 | 00000)
        decoder.Decode([0x20]);

        // Dynamic table should now be empty — index 62 throws
        Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
    }

    /// RFC 7541 §6.1 — Table size update at start of block is accepted
    [Fact(DisplayName = "RFC7541-6-HD-041: Table size update at start of block is accepted")]
    public void Should_AcceptTableSizeUpdate_When_AtStartOfBlock()
    {
        var decoder = new HpackDecoder();
        // 0x28 = 001 | 01000 → size update to 8 bytes
        // Then an indexed header (must use empty table with 8-byte limit)
        var result = decoder.Decode([0x28, 0x82]); // size=8, then :method GET

        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
    }

    /// RFC 7541 §6.1 — Two table size updates at start of block are both accepted (RFC allows)
    [Fact(DisplayName = "RFC7541-6-HD-042: Two table size updates at start of block are both accepted (RFC allows)")]
    public void Should_AcceptBothUpdates_When_TwoTableSizeUpdatesAtStartOfBlock()
    {
        var decoder = new HpackDecoder();
        // Two size updates before any header: [0x20 (size=0), 0x3F, 0x01 (size=32), 0x82]
        // 0x3F = 001|11111 = prefix full (31), 0x01 = continuation → 31 + 1 = 32
        var result = decoder.Decode([0x20, 0x3F, 0x01, 0x82]);

        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
    }

    /// RFC 7541 §6.1 — Table size update after indexed header throws HpackException (§6.3)
    [Fact(DisplayName = "RFC7541-6-HD-043: Table size update after indexed header throws HpackException (§6.3)")]
    public void Should_ThrowHpackException_When_TableSizeUpdateAfterIndexedHeader()
    {
        var decoder = new HpackDecoder();
        // 0x82 (indexed), then 0x20 (size update) — size update after header field is forbidden
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x82, 0x20]));
        Assert.Contains("§6.3", ex.Message);
    }

    /// RFC 7541 §6.1 — Table size update after literal header throws HpackException (§6.3)
    [Fact(DisplayName = "RFC7541-6-HD-044: Table size update after literal header throws HpackException (§6.3)")]
    public void Should_ThrowHpackException_When_TableSizeUpdateAfterLiteralHeader()
    {
        var decoder = new HpackDecoder();
        var bytes = Concat(
            [0x00], RawString("x-h"), RawString("v"),   // literal without indexing
            [0x20]);                                      // size update (too late)

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(bytes));
        Assert.Contains("§6.3", ex.Message);
    }

    /// RFC 7541 §6.1 — Table size update exceeding SETTINGS HEADER TABLE SIZE throws (§4.2)
    [Fact(DisplayName = "RFC7541-6-HD-045: Table size update exceeding SETTINGS_HEADER_TABLE_SIZE throws (§4.2)")]
    public void Should_ThrowHpackException_When_TableSizeUpdateExceedsSettings()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(1024);

        // Encode size update to 2048: prefix 5-bit, max = 31, then multi-byte
        // 2048 - 31 = 2017 → 2017 in base-128: 2017 & 0x7F = 97, 2017 >> 7 = 15
        // Bytes: [0x3F, 0xE1, 0x0F] → 31 + (97 + 15*128) = 31 + 97 + 1920 = 2048
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x3F, 0xE1, 0x0F]));
        Assert.Contains("§4.2", ex.Message);
    }


    /// RFC 7541 §5.1 — Single-byte integer value 0 decodes correctly
    [Fact(DisplayName = "RFC7541-5.1-PI-001: Single-byte integer value 0 decodes correctly")]
    public void Should_ReadZero_When_SingleByteIntegerIsZero()
    {
        var data = new byte[] { 0x00 };
        var pos = 0;
        var value = HpackDecoder.ReadInteger(data, ref pos, 8);
        Assert.Equal(0, value);
        Assert.Equal(1, pos);
    }

    /// RFC 7541 §5.1 — Single-byte integer value fits within 7-bit prefix
    [Fact(DisplayName = "RFC7541-5.1-PI-002: Single-byte integer value fits within 7-bit prefix")]
    public void Should_ReadValue_When_SingleByteIntegerFitsInPrefix()
    {
        // Prefix 7 bits: values 0..126 fit in one byte
        var data = new byte[] { 0x7E }; // 0x7E = 126 (fits in 7-bit prefix, mask = 127)
        var pos = 0;
        var value = HpackDecoder.ReadInteger(data, ref pos, 7);
        Assert.Equal(126, value);
        Assert.Equal(1, pos);
    }

    /// RFC 7541 §5.1 — Multi-byte integer 300 decoded from 5-bit prefix
    [Fact(DisplayName = "RFC7541-5.1-PI-003: Multi-byte integer 300 decoded from 5-bit prefix")]
    public void Should_Read300_When_MultiByteWith5BitPrefix()
    {
        // 5-bit prefix: mask = 31, value 300 > 31
        // First byte: 31 (prefix full), then 300-31=269
        // 269 in base-128 LE: 269 & 0x7F = 13, 269 >> 7 = 2
        // [0x3F, 0x8D, 0x02] where 0x3F = 0x20 | 0x1F (table size update prefix + full value)
        // But we test ReadInteger directly:
        var data = new byte[] { 0x1F, 0x8D, 0x02 }; // 0x1F = prefix bits all set (5-bit)
        var pos = 0;
        var value = HpackDecoder.ReadInteger(data, ref pos, 5);
        Assert.Equal(300, value);
        Assert.Equal(3, pos);
    }

    /// RFC 7541 §5.1 — Multi-byte integer 1337 decoded from 5-bit prefix
    [Fact(DisplayName = "RFC7541-5.1-PI-004: Multi-byte integer 1337 decoded from 5-bit prefix")]
    public void Should_Read1337_When_MultiByteWith5BitPrefix()
    {
        // RFC 7541 Appendix C.1.2 example: 1337 with 5-bit prefix
        // First byte: 0x1F (31), remaining: 1337-31=1306
        // 1306 in base-128 LE: 1306 & 0x7F = 26, 1306 >> 7 = 10
        // Bytes: [0x1F, 0x9A, 0x0A]
        var data = new byte[] { 0x1F, 0x9A, 0x0A };
        var pos = 0;
        var value = HpackDecoder.ReadInteger(data, ref pos, 5);
        Assert.Equal(1337, value);
        Assert.Equal(3, pos);
    }

    /// RFC 7541 §5.1 — Truncated integer (no stop bit) throws HpackException (§5.1)
    [Fact(DisplayName = "RFC7541-5.1-PI-005: Truncated integer (no stop bit) throws HpackException (§5.1)")]
    public void Should_ThrowHpackException_When_IntegerIsTruncated()
    {
        // Prefix full but continuation byte has MSB set (more bytes expected) then data ends
        var data = new byte[] { 0x7F, 0x80 }; // 7-bit prefix: 127, then 0x80 (MSB set = more)
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() => HpackDecoder.ReadInteger(data, ref pos, 7));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 7541 §5.1 — Integer overflow exceeding int.MaxValue throws HpackException (§5.1)
    [Fact(DisplayName = "RFC7541-5.1-PI-006: Integer overflow exceeding int.MaxValue throws HpackException (§5.1)")]
    public void Should_ThrowHpackException_When_IntegerOverflows()
    {
        // Craft a multi-byte integer that overflows int.MaxValue
        // Use 8-bit prefix with max value, then many continuation bytes
        // Start with prefix full (0xFF), then bytes that accumulate > 2^31-1
        var data = new byte[]
        {
            0x7F,       // 7-bit prefix full (127)
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F // huge continuation
        };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() => HpackDecoder.ReadInteger(data, ref pos, 7));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 7541 §5.1 — Reading integer at end of data throws HpackException (§5.1)
    /// RFC 7541 §5.1 — ReadInteger with prefixBits=0 is an invalid call (must be 1-8)
    [Fact(DisplayName = "RFC7541-5.1-PI-008: ReadInteger with prefixBits=0 throws ArgumentOutOfRangeException (§5.1)")]
    public void Should_ThrowArgumentOutOfRange_When_PrefixBitsIsZero()
    {
        var data = new byte[] { 0x00 };
        var pos = 0;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => HpackDecoder.ReadInteger(data, ref pos, 0));
        Assert.Contains("prefixBits", ex.ParamName);
    }

    /// RFC 7541 §5.1 — ReadInteger with prefixBits=9 is an invalid call (must be 1-8)
    [Fact(DisplayName = "RFC7541-5.1-PI-009: ReadInteger with prefixBits=9 throws ArgumentOutOfRangeException (§5.1)")]
    public void Should_ThrowArgumentOutOfRange_When_PrefixBitsIsNine()
    {
        var data = new byte[] { 0x00 };
        var pos = 0;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => HpackDecoder.ReadInteger(data, ref pos, 9));
        Assert.Contains("prefixBits", ex.ParamName);
    }

    /// RFC 7541 §5.1 — Excessively long multi-byte integer (shift >= 62) must throw HpackException.
    /// Encoding: 1-bit prefix byte (0x01 = all-ones for prefix=1) + 9 continuation bytes (MSB=1, value bits=0).
    /// At the 10th loop iteration, shift reaches 63 >= 62 → encoding length exceeded.
    [Fact(DisplayName = "RFC7541-5.1-PI-010: Integer with 10 continuation bytes triggers shift>=62 overflow guard (§5.1)")]
    public void Should_ThrowHpackException_When_TenContinuationBytesExceedShiftLimit()
    {
        // Build: [0x01, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00]
        //   0x01 = prefix=1, value=1=mask → multi-byte
        //   9 × 0x80 = continuation bytes (MSB=1, value bits=0) — iterations 1-9
        //   At start of iteration 10: shift=63 >= 62 → throw
        //   The trailing 0x00 is a terminal byte (never reached)
        var data = new byte[] { 0x01, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() => HpackDecoder.ReadInteger(data, ref pos, 1));
        Assert.Contains("encoding length exceeded", ex.Message);
    }

    [Fact(DisplayName = "RFC7541-5.1-PI-007: Reading integer at end of data throws HpackException (§5.1)")]
    public void Should_ThrowHpackException_When_IntegerReadAtEndOfData()
    {
        var data = Array.Empty<byte>();
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() => HpackDecoder.ReadInteger(data, ref pos, 7));
        Assert.Contains("end of data", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    /// RFC 7541 §5.2 — String length exceeds available data throws HpackException (§5.2)
    /// RFC 7541 — HpackException(string, Exception) constructor sets InnerException correctly.
    [Fact(DisplayName = "RFC7541-5.2-LF-006: HpackException two-arg constructor sets InnerException")]
    public void Should_SetInnerException_When_TwoArgConstructor()
    {
        var inner = new InvalidOperationException("cause");
        var ex = new HpackException("HPACK error", inner);
        Assert.Equal("HPACK error", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact(DisplayName = "RFC7541-5.2-LF-001: String length exceeds available data throws HpackException (§5.2)")]
    public void Should_ThrowHpackException_When_StringLengthExceedsAvailableData()
    {
        var decoder = new HpackDecoder();
        // 0x00 (literal no-index, new name), then name [0x05, 'h'] → only 1 byte when 5 expected
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x00, 0x05, (byte)'h']));
        Assert.Contains("§5.2", ex.Message);
    }

    /// RFC 7541 §5.2 — Empty string literal (length 0) is accepted
    [Fact(DisplayName = "RFC7541-5.2-LF-002: Empty string literal (length 0) is accepted")]
    public void Should_AcceptEmptyString_When_StringLengthIsZero()
    {
        var decoder = new HpackDecoder();
        // Literal without indexing: [0x00, 0x01, 'x', 0x00] → name="x", value=""
        var result = decoder.Decode([0x00, 0x01, (byte)'x', 0x00]);

        Assert.Single(result);
        Assert.Equal("x", result[0].Name);
        Assert.Equal(string.Empty, result[0].Value);
    }

    /// RFC 7541 §5.2 — String length exceeding maxStringLength throws HpackException
    [Fact(DisplayName = "RFC7541-5.2-LF-003: String length exceeding maxStringLength throws HpackException")]
    public void Should_ThrowHpackException_When_StringLengthExceedsMaxStringLength()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxStringLength(4);

        // Try to decode a name of length 5 ("hello")
        var bytes = Concat([0x00], RawString("hello"), RawString("v"));
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(bytes));
        Assert.Contains("§5.2", ex.Message);
    }

    /// RFC 7541 §5.2 — String value length exceeding maxStringLength throws HpackException
    [Fact(DisplayName = "RFC7541-5.2-LF-005: String value length exceeding maxStringLength throws HpackException")]
    public void Should_ThrowHpackException_When_StringValueLengthExceedsMaxStringLength()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxStringLength(10);

        // Literal Header Field with Incremental Indexing (0x40 | 1 = name from static index 1 = :authority)
        // Value of length 11 (exceeds limit of 10)
        var valueBytes = new byte[11];
        for (var i = 0; i < valueBytes.Length; i++) { valueBytes[i] = (byte)'v'; }

        var block = new byte[1 + 1 + valueBytes.Length];
        block[0] = 0x41; // 0x40 | 1 = literal+indexing, name from static index 1
        block[1] = (byte)valueBytes.Length;
        valueBytes.CopyTo(block, 2);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("exceeds maximum", ex.Message);
    }

    /// RFC 7541 §5.2 — Non-Huffman string with multi-byte content decoded correctly
    [Fact(DisplayName = "RFC7541-5.2-LF-004: Non-Huffman string with multi-byte content decoded correctly")]
    public void Should_DecodeMultiByteContent_When_NonHuffmanStringLiteral()
    {
        var decoder = new HpackDecoder();
        var longName = "x-long-header-name";
        var longValue = "some-fairly-long-header-value-here";
        var bytes = Concat([0x00], RawString(longName), RawString(longValue));
        var result = decoder.Decode(bytes);

        Assert.Single(result);
        Assert.Equal(longName, result[0].Name);
        Assert.Equal(longValue, result[0].Value);
    }


    /// RFC 7541 §6 — Empty byte array returns empty header list
    [Fact(DisplayName = "RFC7541-6-ME-001: Empty byte array returns empty header list")]
    public void Should_ReturnEmptyList_When_InputIsEmpty()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([]);
        Assert.Empty(result);
    }

    /// RFC 7541 §6 — Index 0 in indexed representation throws HpackException (§2.3.3)
    [Fact(DisplayName = "RFC7541-6-ME-002: Index 0 in indexed representation throws HpackException (§2.3.3)")]
    public void Should_ThrowHpackException_When_Index0InIndexedRepresentation()
    {
        var decoder = new HpackDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x80]));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 7541 §6 — Dynamic index out of range (table empty) throws HpackException (§2.3.3)
    [Fact(DisplayName = "RFC7541-6-ME-003: Dynamic index out of range (table empty) throws HpackException (§2.3.3)")]
    public void Should_ThrowHpackException_When_DynamicIndexAndTableIsEmpty()
    {
        var decoder = new HpackDecoder();
        // Index 62 (first dynamic slot) with empty table → out of range
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
        Assert.Contains("out of range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 7541 §6 — Empty header name in literal representation throws HpackException (§7.2)
    [Fact(DisplayName = "RFC7541-6-ME-004: Empty header name in literal representation throws HpackException (§7.2)")]
    public void Should_ThrowHpackException_When_HeaderNameIsEmpty()
    {
        var decoder = new HpackDecoder();
        // Literal without indexing, new name (index 0), name length = 0 → empty name
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x00, 0x00, 0x00]));
        Assert.Contains("§7.2", ex.Message);
    }

    /// RFC 7541 §6 — Truncated indexed field (no data after prefix byte) throws HpackException
    [Fact(DisplayName = "RFC7541-6-ME-005: Truncated indexed field (no data after prefix byte) throws HpackException")]
    public void Should_ThrowHpackException_When_IndexedFieldIsTruncated()
    {
        var decoder = new HpackDecoder();
        // 0xFF = 11111111 → indexed, 7-bit prefix full (127), expects continuation
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xFF]));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 7541 §6 — Truncated string data (fewer bytes than declared length) throws HpackException
    [Fact(DisplayName = "RFC7541-6-ME-006: Truncated string data (fewer bytes than declared length) throws HpackException")]
    public void Should_ThrowHpackException_When_StringDataIsTruncated()
    {
        var decoder = new HpackDecoder();
        // Literal no-index, new name: name length=5, only 2 bytes follow
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x00, 0x05, (byte)'h', (byte)'e']));
        Assert.Contains("§5.2", ex.Message);
    }

    /// RFC 7541 §6 — Mixed representation types decoded correctly in one block
    [Fact(DisplayName = "RFC7541-6-ME-007: Mixed representation types decoded correctly in one block")]
    public void Should_DecodeAll_When_MixedRepresentationTypes()
    {
        var decoder = new HpackDecoder();
        // Block: indexed :method GET, literal+indexing x-custom:val, never-indexed cookie:xyz
        var bytes = Concat(
            [0x82],                                                  // indexed :method GET
            [0x40], RawString("x-custom"), RawString("val"),        // literal+indexing
            [0x10], RawString("cookie"), RawString("xyz"));         // never-indexed

        var result = decoder.Decode(bytes);

        Assert.Equal(3, result.Count);
        Assert.Equal(":method", result[0].Name); Assert.False(result[0].NeverIndex);
        Assert.Equal("x-custom", result[1].Name); Assert.False(result[1].NeverIndex);
        Assert.Equal("cookie", result[2].Name); Assert.True(result[2].NeverIndex);
    }


    /// RFC 7541 §6 — Encoder/decoder round-trip — all static-only headers
    [Fact(DisplayName = "RFC7541-6-RT-001: Encoder/decoder round-trip — all static-only headers")]
    public void Should_MatchAfterDecode_When_StaticOnlyHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    /// RFC 7541 §6 — Encoder/decoder round-trip — dynamic table populated correctly
    [Fact(DisplayName = "RFC7541-6-RT-002: Encoder/decoder round-trip — dynamic table populated correctly")]
    public void Should_RepopulateDynamicTable_When_CustomHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-request-id", "abc123"),
            ("accept", "text/html"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    /// RFC 7541 §6 — Encoder/decoder round-trip — second request reuses dynamic table
    [Fact(DisplayName = "RFC7541-6-RT-003: Encoder/decoder round-trip — second request reuses dynamic table")]
    public void Should_ReusesDynamicTable_When_SecondRequest()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // First request
        var headers1 = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("content-type", "application/json"),
        };
        var enc1 = encoder.Encode(headers1);
        var dec1 = decoder.Decode(enc1.Span);
        Assert.Equal(headers1.Count, dec1.Count);

        // Second request with same headers — encoder should use dynamic table entries
        var enc2 = encoder.Encode(headers1);
        var dec2 = decoder.Decode(enc2.Span);

        Assert.Equal(headers1.Count, dec2.Count);
        for (var i = 0; i < headers1.Count; i++)
        {
            Assert.Equal(headers1[i].Item1, dec2[i].Name);
            Assert.Equal(headers1[i].Item2, dec2[i].Value);
        }
    }

    /// RFC 7541 §6 — Encoder/decoder round-trip — Huffman encoding enabled
    [Fact(DisplayName = "RFC7541-6-RT-004: Encoder/decoder round-trip — Huffman encoding enabled")]
    public void Should_DecodeCorrectly_When_HuffmanEncodingEnabled()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "POST"),
            (":path", "/api/v1/resource"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("authorization", "Bearer eyJhbGciOiJIUzI1NiJ9"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    /// RFC 7541 §6 — Sensitive headers (authorization, cookie) are automatically NeverIndexed
    [Fact(DisplayName = "RFC7541-6-RT-005: Sensitive headers (authorization, cookie) are automatically NeverIndexed")]
    public void Should_AutomaticallyNeverIndex_When_SensitiveHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // The encoder automatically promotes authorization and cookie to NeverIndexed (RFC 7541 §7.1)
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("authorization", "Bearer token"),
            ("cookie", "session=abc"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        // Authorization and cookie should have NeverIndex = true (auto-promoted by encoder)
        Assert.True(decoded[4].NeverIndex);
        Assert.True(decoded[5].NeverIndex);
        // Non-sensitive headers should not be never-indexed
        Assert.False(decoded[0].NeverIndex);
    }
}
