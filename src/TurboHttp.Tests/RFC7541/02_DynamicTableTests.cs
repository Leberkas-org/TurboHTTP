using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC7541;

/// <summary>
/// RFC 7541 §4 — HPACK Dynamic Table Engine
/// Phase 17-18: Verify dynamic table FIFO eviction, size tracking,
/// HEADER_TABLE_SIZE enforcement, and size update position rules.
///
/// Key invariants:
///   §4.1  Entry size = name.UTF8 + value.UTF8 + 32 bytes overhead
///   §4.2  Table size must not exceed SETTINGS_HEADER_TABLE_SIZE
///   §4.4  Entry larger than MaxSize → evict entire table (no partial add)
///   §6.3  Dynamic Table Size Update must appear before any header field
///   FIFO: newest entry is at index 1; oldest is at index Count; oldest evicted first
/// </summary>
public sealed class HpackDynamicTableTests
{
    // ── DT-00x: Empty table invariants ────────────────────────────────────────

    /// RFC 7541 §4 — Empty table has CurrentSize = 0
    [Fact(DisplayName = "DT-001: Empty table has CurrentSize = 0")]
    public void DynamicTable_Empty_HasSizeZero()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(0, table.CurrentSize);
    }

    /// RFC 7541 §4 — Empty table has Count = 0
    [Fact(DisplayName = "DT-002: Empty table has Count = 0")]
    public void DynamicTable_Empty_HasCountZero()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(0, table.Count);
    }

    /// RFC 7541 §4 — Default MaxSize = 4096 (RFC 7541 §4.2)
    [Fact(DisplayName = "DT-003: Default MaxSize = 4096 (RFC 7541 §4.2)")]
    public void DynamicTable_Default_MaxSizeIs4096()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(4096, table.MaxSize);
    }

    /// RFC 7541 §4 — GetEntry(1) returns null on empty table
    [Fact(DisplayName = "DT-004: GetEntry(1) returns null on empty table")]
    public void DynamicTable_GetEntry_EmptyTable_ReturnsNull()
    {
        var table = new HpackDynamicTable();
        Assert.Null(table.GetEntry(1));
    }

    /// RFC 7541 §4 — GetEntry(0) returns null (out of range)
    [Fact(DisplayName = "DT-005: GetEntry(0) returns null (out of range)")]
    public void DynamicTable_GetEntry_IndexZero_ReturnsNull()
    {
        var table = new HpackDynamicTable();
        Assert.Null(table.GetEntry(0));
    }

    // ── DT-01x: Size tracking ─────────────────────────────────────────────────

    /// RFC 7541 §4 — Single entry size = UTF8(name) + UTF8(value) + 32
    [Fact(DisplayName = "DT-010: Single entry size = UTF8(name) + UTF8(value) + 32")]
    public void DynamicTable_Add_SingleEntry_SizeIsCorrect()
    {
        var table = new HpackDynamicTable();
        // "via" = 3 bytes, "proxy1" = 6 bytes → 3+6+32 = 41
        table.Add("via", "proxy1");
        Assert.Equal(41, table.CurrentSize);
    }

    /// RFC 7541 §4 — Two entries sum their individual sizes
    [Fact(DisplayName = "DT-011: Two entries sum their individual sizes")]
    public void DynamicTable_Add_TwoEntries_SizeAccumulates()
    {
        var table = new HpackDynamicTable();
        // "via"=3, "proxy1"=6 → 41
        // "age"=3, "100"=3   → 38
        // total = 79
        table.Add("via", "proxy1");
        table.Add("age", "100");
        Assert.Equal(79, table.CurrentSize);
    }

    /// RFC 7541 §4 — Empty name and value still adds 32 bytes overhead
    [Fact(DisplayName = "DT-012: Empty name and value still adds 32 bytes overhead")]
    public void DynamicTable_Add_EmptyNameValue_Adds32Bytes()
    {
        var table = new HpackDynamicTable();
        table.Add(string.Empty, string.Empty);
        Assert.Equal(32, table.CurrentSize);
    }

    /// RFC 7541 §4 — UTF-8 multi-byte name counted in bytes, not chars
    [Fact(DisplayName = "DT-013: UTF-8 multi-byte name counted in bytes, not chars")]
    public void DynamicTable_Add_MultiByteName_SizeCounted_AsUtf8Bytes()
    {
        var table = new HpackDynamicTable();
        // "café" in UTF-8 = 5 bytes (c=1, a=1, f=1, é=2), value="" = 0
        // Size = 5 + 0 + 32 = 37
        var name = "café";
        var expected = Encoding.UTF8.GetByteCount(name) + 0 + 32;
        table.Add(name, string.Empty);
        Assert.Equal(expected, table.CurrentSize);
    }

    /// RFC 7541 §4 — UTF-8 multi-byte value counted in bytes, not chars
    [Fact(DisplayName = "DT-014: UTF-8 multi-byte value counted in bytes, not chars")]
    public void DynamicTable_Add_MultiByteValue_SizeCounted_AsUtf8Bytes()
    {
        var table = new HpackDynamicTable();
        // name="x"=1, value="héllo"=6 bytes UTF-8
        var value = "héllo";
        var expected = 1 + Encoding.UTF8.GetByteCount(value) + 32;
        table.Add("x", value);
        Assert.Equal(expected, table.CurrentSize);
    }

    // ── DT-02x: FIFO ordering ─────────────────────────────────────────────────

    /// RFC 7541 §4 — GetEntry(1) returns most recently added entry
    [Fact(DisplayName = "DT-020: GetEntry(1) returns most recently added entry")]
    public void DynamicTable_GetEntry1_ReturnsMostRecent()
    {
        var table = new HpackDynamicTable();
        table.Add("first", "v1");
        table.Add("second", "v2");  // most recent

        var entry = table.GetEntry(1);
        Assert.NotNull(entry);
        Assert.Equal("second", entry.Value.Name);
        Assert.Equal("v2", entry.Value.Value);
    }

    /// RFC 7541 §4 — GetEntry(2) returns second-most-recently added entry
    [Fact(DisplayName = "DT-021: GetEntry(2) returns second-most-recently added entry")]
    public void DynamicTable_GetEntry2_ReturnsSecondMostRecent()
    {
        var table = new HpackDynamicTable();
        table.Add("first", "v1");
        table.Add("second", "v2");

        var entry = table.GetEntry(2);
        Assert.NotNull(entry);
        Assert.Equal("first", entry.Value.Name);
        Assert.Equal("v1", entry.Value.Value);
    }

    /// RFC 7541 §4 — FIFO — oldest entry is at index Count
    [Fact(DisplayName = "DT-022: FIFO — oldest entry is at index Count")]
    public void DynamicTable_FifoOrdering_OldestIsAtHighestIndex()
    {
        var table = new HpackDynamicTable();
        table.Add("a", "1");
        table.Add("b", "2");
        table.Add("c", "3");  // newest

        Assert.Equal("c", table.GetEntry(1)!.Value.Name);
        Assert.Equal("b", table.GetEntry(2)!.Value.Name);
        Assert.Equal("a", table.GetEntry(3)!.Value.Name);
        Assert.Equal(3, table.Count);
    }

    /// RFC 7541 §4 — GetEntry beyond Count returns null
    [Fact(DisplayName = "DT-023: GetEntry beyond Count returns null")]
    public void DynamicTable_GetEntry_BeyondCount_ReturnsNull()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        Assert.Null(table.GetEntry(2));
        Assert.Null(table.GetEntry(99));
    }

    // ── DT-03x: FIFO eviction ─────────────────────────────────────────────────

    /// RFC 7541 §4 — Eviction removes oldest entry first (FIFO)
    [Fact(DisplayName = "DT-030: Eviction removes oldest entry first (FIFO)")]
    public void DynamicTable_Eviction_RemovesOldestFirst()
    {
        // MaxSize=4096. Add 3 entries that fit. Then reduce MaxSize to force eviction.
        var table = new HpackDynamicTable();
        table.Add("alpha", "1");   // added first (oldest)
        table.Add("beta",  "2");
        table.Add("gamma", "3");   // newest

        // Reduce max to fit only the two newest
        var gammaSize = "gamma".Length + "3".Length + 32;  // 40
        var betaSize  = "beta".Length  + "2".Length + 32;  // 37
        var newMax = gammaSize + betaSize; // 77

        table.SetMaxSize(newMax);

        Assert.Equal(2, table.Count);
        Assert.Equal("gamma", table.GetEntry(1)!.Value.Name);
        Assert.Equal("beta",  table.GetEntry(2)!.Value.Name);
    }

    /// RFC 7541 §4 — Entry larger than MaxSize clears entire table (RFC §4.4)
    [Fact(DisplayName = "DT-031: Entry larger than MaxSize clears entire table (RFC §4.4)")]
    public void DynamicTable_AddOversizedEntry_ClearsTable()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        Assert.Equal(1, table.Count);

        // Reduce MaxSize so next entry (alone) exceeds it
        table.SetMaxSize(10); // "longname"+"longvalue"+32 = 48 > 10

        table.Add("longname", "longvalue");  // 8+9+32=49 > 10

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    /// RFC 7541 §4 — SetMaxSize(0) evicts all entries
    [Fact(DisplayName = "DT-032: SetMaxSize(0) evicts all entries")]
    public void DynamicTable_SetMaxSizeZero_EvictsAll()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        table.Add("a", "b");
        table.SetMaxSize(0);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    /// RFC 7541 §4 — Adding to full table evicts oldest to make room
    [Fact(DisplayName = "DT-033: Adding to full table evicts oldest to make room")]
    public void DynamicTable_AddToFullTable_EvictsOldestToFit()
    {
        // Size of one entry: "k"=1, "v"=1, +32 = 34
        // MaxSize=68 fits exactly two entries
        var table = new HpackDynamicTable();
        table.SetMaxSize(68);

        table.Add("k", "1");  // 34 bytes
        table.Add("k", "2");  // 34 bytes → exactly full

        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);

        table.Add("k", "3");  // 34 bytes → must evict "k:1" to fit
        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);
        Assert.Equal("3", table.GetEntry(1)!.Value.Value); // newest
        Assert.Equal("2", table.GetEntry(2)!.Value.Value); // second newest
    }

    /// RFC 7541 §4 — Multiple evictions until size fits new entry
    [Fact(DisplayName = "DT-034: Multiple evictions until size fits new entry")]
    public void DynamicTable_AddEntry_EvictsMultipleOldEntries()
    {
        // Fill table with 5 small entries, then add one large entry
        var table = new HpackDynamicTable();
        table.SetMaxSize(200);

        for (var i = 0; i < 5; i++)
        {
            table.Add("h", i.ToString()); // "h"=1, "i"=1..5 = 34 each → total 170
        }

        Assert.Equal(5, table.Count);

        // Add a large entry: "bigname"=7, "bigvalue"=8, +32 = 47
        // To fit 47, must evict entries until currentSize <= 200-47=153
        // 5 × 34 = 170 → evict oldest until room
        table.Add("bigname", "bigvalue"); // 47 bytes

        Assert.Equal(47, table.GetEntry(1)!.Value.Name.Length + table.GetEntry(1)!.Value.Value.Length + 32);
        Assert.True(table.CurrentSize <= 200);
    }

    // ── DT-04x: SetMaxSize behavior ───────────────────────────────────────────

    /// RFC 7541 §4 — SetMaxSize updates MaxSize property
    [Fact(DisplayName = "DT-040: SetMaxSize updates MaxSize property")]
    public void DynamicTable_SetMaxSize_UpdatesMaxSize()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(1024);
        Assert.Equal(1024, table.MaxSize);
    }

    /// RFC 7541 §4 — SetMaxSize to same value is idempotent
    [Fact(DisplayName = "DT-041: SetMaxSize to same value is idempotent")]
    public void DynamicTable_SetMaxSize_SameValue_NoChange()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        var sizeBefore = table.CurrentSize;
        table.SetMaxSize(4096);
        Assert.Equal(sizeBefore, table.CurrentSize);
        Assert.Equal(1, table.Count);
    }

    /// RFC 7541 §4 — Negative MaxSize throws HpackException
    [Fact(DisplayName = "DT-042: Negative MaxSize throws HpackException")]
    public void DynamicTable_SetMaxSize_Negative_Throws()
    {
        var table = new HpackDynamicTable();
        Assert.Throws<HpackException>(() => table.SetMaxSize(-1));
    }

    /// RFC 7541 §4 — SetMaxSize to exact entry size keeps that entry
    [Fact(DisplayName = "DT-043: SetMaxSize to exact entry size keeps that entry")]
    public void DynamicTable_SetMaxSize_ExactEntrySize_Keeps()
    {
        var table = new HpackDynamicTable();
        // "via"=3, "proxy"=5 → 3+5+32 = 40
        table.Add("via", "proxy");
        table.SetMaxSize(40);
        Assert.Equal(1, table.Count);
        Assert.Equal(40, table.CurrentSize);
    }

    /// RFC 7541 §4 — SetMaxSize to one less than entry size evicts it
    [Fact(DisplayName = "DT-044: SetMaxSize to one less than entry size evicts it")]
    public void DynamicTable_SetMaxSize_OneLessThanEntry_EvictsIt()
    {
        var table = new HpackDynamicTable();
        // "via"=3, "proxy"=5 → 40 bytes
        table.Add("via", "proxy");
        table.SetMaxSize(39);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    // ── TS-00x: Table size update position (RFC 7541 §6.3) ───────────────────

    /// RFC 7541 §6.3 — Table size update at start of block is accepted
    [Fact(DisplayName = "TS-001: Table size update at start of block is accepted")]
    public void Decoder_TableSizeUpdate_AtStart_Accepted()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(512);

        // Build: [size-update=512] [indexed: :method=GET = 0x82]
        var buf = new ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(512, prefixBits: 5, prefixFlags: 0x20, buf);
        buf.GetSpan(1)[0] = 0x82;
        buf.Advance(1);

        var headers = decoder.Decode(buf.WrittenSpan);
        Assert.Single(headers);
        Assert.Equal(":method", headers[0].Name);
    }

    /// RFC 7541 §6.3 — Two size updates at start of block are both accepted
    [Fact(DisplayName = "TS-002: Two size updates at start of block are both accepted")]
    public void Decoder_TwoTableSizeUpdates_AtStart_BothAccepted()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        // Two size updates then a header
        var buf = new ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, buf);
        HpackEncoder.WriteInteger(4096, prefixBits: 5, prefixFlags: 0x20, buf);
        buf.GetSpan(1)[0] = 0x82; // :method=GET
        buf.Advance(1);

        var headers = decoder.Decode(buf.WrittenSpan);
        Assert.Single(headers);
    }

    /// RFC 7541 §6.3 — Table size update after indexed header throws HpackException
    [Fact(DisplayName = "TS-003: Table size update after indexed header throws HpackException")]
    public void Decoder_TableSizeUpdate_AfterIndexedHeader_Throws()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        // [indexed :method=GET][size-update=256]
        var buf = new ArrayBufferWriter<byte>();
        buf.GetSpan(1)[0] = 0x82; // indexed :method=GET
        buf.Advance(1);
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, buf);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buf.WrittenSpan));
        Assert.Contains("6.3", ex.Message);
    }

    /// RFC 7541 §6.3 — Table size update after literal-with-indexing throws HpackException
    [Fact(DisplayName = "TS-004: Table size update after literal-with-indexing throws HpackException")]
    public void Decoder_TableSizeUpdate_AfterLiteralWithIndexing_Throws()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        var encoder = new HpackEncoder(useHuffman: false);
        var headerBlock = encoder.Encode(new List<(string, string)> { ("x-custom", "val") });

        var buf = new List<byte>(headerBlock.ToArray());
        // Append size update after the header
        var writer = new ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(512, prefixBits: 5, prefixFlags: 0x20, writer);
        buf.AddRange(writer.WrittenSpan.ToArray());

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buf.ToArray()));
        Assert.Contains("6.3", ex.Message);
    }

    /// RFC 7541 §6.3 — Table size update exceeding SETTINGS throws HpackException
    [Fact(DisplayName = "TS-005: Table size update exceeding SETTINGS throws HpackException")]
    public void Decoder_TableSizeUpdate_ExceedingSettings_Throws()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(256);

        // Size update of 257 > 256
        var buf = new ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(257, prefixBits: 5, prefixFlags: 0x20, buf);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buf.WrittenSpan));
        Assert.Contains("4.2", ex.Message);
    }

    /// RFC 7541 §6.3 — Size update to exact SETTINGS value is accepted
    [Fact(DisplayName = "TS-006: Size update to exact SETTINGS value is accepted")]
    public void Decoder_TableSizeUpdate_ExactSettings_Accepted()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(256);

        var buf = new ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, buf);
        // Add a valid header after so we can confirm decoding succeeded
        buf.GetSpan(1)[0] = 0x82;
        buf.Advance(1);

        var headers = decoder.Decode(buf.WrittenSpan);
        Assert.Single(headers);
    }

    // ── ET-00x: Encoder AcknowledgeTableSizeChange ────────────────────────────

    /// RFC 7541 §6.3 — AcknowledgeTableSizeChange emits size update prefix at next encode
    [Fact(DisplayName = "ET-001: AcknowledgeTableSizeChange emits size update prefix at next encode")]
    public void Encoder_AcknowledgeTableSizeChange_EmitsSizeUpdateBeforeHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(512);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        // First byte must be a size update: 001xxxxx prefix
        var firstByte = encoded.Span[0];
        Assert.Equal(0x20, firstByte & 0xE0); // top 3 bits = 001
    }

    /// RFC 7541 §6.3 — After AcknowledgeTableSizeChange, next encode contains header after update
    [Fact(DisplayName = "ET-002: After AcknowledgeTableSizeChange, next encode contains header after update")]
    public void Encoder_AcknowledgeTableSizeChange_SizeUpdateThenHeader()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        encoder.AcknowledgeTableSizeChange(512);
        decoder.SetMaxAllowedTableSize(512);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
    }

    /// RFC 7541 §6.3 — AcknowledgeTableSizeChange(0) emits zero-size update
    [Fact(DisplayName = "ET-003: AcknowledgeTableSizeChange(0) emits zero-size update")]
    public void Encoder_AcknowledgeTableSizeChange_Zero_EmitsZeroUpdate()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(0);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        // 0x20 = 001 00000 = size update with value 0
        var firstByte = encoded.Span[0];
        Assert.Equal(0x20, firstByte); // prefix byte for size=0
    }

    /// RFC 7541 §6.3 — AcknowledgeTableSizeChange with negative size throws HpackException
    [Fact(DisplayName = "ET-004: AcknowledgeTableSizeChange with negative size throws HpackException")]
    public void Encoder_AcknowledgeTableSizeChange_Negative_Throws()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        Assert.Throws<HpackException>(() => encoder.AcknowledgeTableSizeChange(-1));
    }

    /// RFC 7541 §6.3 — Second encode after AcknowledgeTableSizeChange does NOT re-emit update
    [Fact(DisplayName = "ET-005: Second encode after AcknowledgeTableSizeChange does NOT re-emit update")]
    public void Encoder_AcknowledgeTableSizeChange_OnlyEmittedOnce()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(512);

        var firstBlock = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var secondBlock = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        // First block has size-update prefix → larger
        // Second block should not have a size-update prefix
        var secondFirstByte = secondBlock.Span[0];
        Assert.NotEqual(0x20, secondFirstByte & 0xE0); // top 3 bits must NOT be 001
    }

    // ── ES-00x: Encoder/Decoder synchronization ───────────────────────────────

    /// RFC 7541 §7.1 — Dynamic entry added by encode is accessible via index on decode
    [Fact(DisplayName = "ES-001: Dynamic entry added by encode is accessible via index on decode")]
    public void EncoderDecoder_DynamicEntry_AccessibleViaIndex()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // First block: add "x-custom: myval" to dynamic table (index 62)
        var block1 = encoder.Encode(new List<(string, string)> { ("x-custom", "myval") });
        decoder.Decode(block1.Span);

        // Second block: "x-custom: myval" should now encode as a single indexed byte
        var block2 = encoder.Encode(new List<(string, string)> { ("x-custom", "myval") });
        var decoded2 = decoder.Decode(block2.Span);

        Assert.Single(decoded2);
        Assert.Equal("x-custom", decoded2[0].Name);
        Assert.Equal("myval", decoded2[0].Value);

        // Indexed representation = single byte with high bit set (1xxxxxxx)
        Assert.Equal(0x80, block2.Span[0] & 0x80);
    }

    /// RFC 7541 §7.1 — Multiple dynamic entries maintain FIFO indexing on both sides
    [Fact(DisplayName = "ES-002: Multiple dynamic entries maintain FIFO indexing on both sides")]
    public void EncoderDecoder_MultipleEntries_MaintainFifoIndexing()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Add two entries to the dynamic table
        var h1 = new List<(string, string)> { ("x-first", "v1") };
        var h2 = new List<(string, string)> { ("x-second", "v2") };

        decoder.Decode(encoder.Encode(h1).Span);
        decoder.Decode(encoder.Encode(h2).Span);

        // x-second was added last → index 62 (newest)
        // x-first was added first → index 63
        // Encode x-first: should use index 63
        var block3 = encoder.Encode(h1);
        var decoded = decoder.Decode(block3.Span);
        Assert.Single(decoded);
        Assert.Equal("x-first", decoded[0].Name);
        Assert.Equal("v1", decoded[0].Value);
    }

    /// RFC 7541 §7.1 — Encoder and decoder stay in sync across multiple header blocks
    [Fact(DisplayName = "ES-003: Encoder and decoder stay in sync across multiple header blocks")]
    public void EncoderDecoder_MultipleBlocks_StayInSync()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var allHeaders = new List<(string, string)>
        {
            (":method",    "GET"),
            (":path",      "/api/data"),
            (":scheme",    "https"),
            (":authority", "example.com"),
            ("accept",     "application/json"),
            ("x-trace-id", "abc-123"),
        };

        // First encode/decode: all go into dynamic table (those not in static)
        var block1 = encoder.Encode(allHeaders);
        var decoded1 = decoder.Decode(block1.Span);
        Assert.Equal(allHeaders.Count, decoded1.Count);

        // Second encode/decode: should use indexed refs for cached entries
        var block2 = encoder.Encode(allHeaders);
        var decoded2 = decoder.Decode(block2.Span);
        Assert.Equal(allHeaders.Count, decoded2.Count);

        for (var i = 0; i < allHeaders.Count; i++)
        {
            Assert.Equal(allHeaders[i].Item1, decoded2[i].Name);
            Assert.Equal(allHeaders[i].Item2, decoded2[i].Value);
        }

        // Second block should be smaller (using indexed refs) than first
        Assert.True(block2.Length <= block1.Length,
            $"Expected block2 ({block2.Length}B) <= block1 ({block1.Length}B)");
    }

    /// RFC 7541 §7.1 — Synchronized table size change via AcknowledgeTableSizeChange
    [Fact(DisplayName = "ES-004: Synchronized table size change via AcknowledgeTableSizeChange")]
    public void EncoderDecoder_TableSizeChange_Synchronized()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Add entries with default 4096 limit
        var h = new List<(string, string)> { ("x-custom", "value") };
        decoder.Decode(encoder.Encode(h).Span);

        // Both sides reduce to 0 (clear) then back to 4096
        encoder.AcknowledgeTableSizeChange(0);
        decoder.SetMaxAllowedTableSize(4096);

        var block = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var decoded = decoder.Decode(block.Span);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
    }

    /// RFC 7541 §7.1 — Never-indexed headers not added to dynamic table on either side
    [Fact(DisplayName = "ES-005: Never-indexed headers not added to dynamic table on either side")]
    public void EncoderDecoder_NeverIndexed_NotAddedToDynamicTable()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // authorization is a sensitive header → NeverIndexed
        var headers = new List<HpackHeader>
        {
            new HpackHeader("authorization", "Bearer token123", NeverIndex: true),
        };

        var buf = new ArrayBufferWriter<byte>();
        encoder.Encode(headers, buf, useHuffman: false);

        var decoded = decoder.Decode(buf.WrittenSpan);
        Assert.Single(decoded);
        Assert.True(decoded[0].NeverIndex, "authorization must be decoded with NeverIndex=true");

        // Wire format: first byte must have 0001xxxx prefix (never-indexed)
        Assert.Equal(0x10, buf.WrittenSpan[0] & 0xF0);
    }

    // ── DT-05x: Boundary and stress tests ────────────────────────────────────

    /// RFC 7541 §4 — Table fills exactly to MaxSize without eviction
    [Fact(DisplayName = "DT-050: Table fills exactly to MaxSize without eviction")]
    public void DynamicTable_FillsExactlyToMaxSize_NoEviction()
    {
        var table = new HpackDynamicTable();
        // Entry: "k"=1, "v"=1 → 34 bytes. MaxSize=68 → exactly 2 entries
        table.SetMaxSize(68);
        table.Add("k", "1");
        table.Add("k", "2");
        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);
    }

    /// RFC 7541 §4 — Adding one more byte beyond MaxSize evicts oldest
    [Fact(DisplayName = "DT-051: Adding one more byte beyond MaxSize evicts oldest")]
    public void DynamicTable_OneByteBeyondMaxSize_EvictsOldest()
    {
        var table = new HpackDynamicTable();
        // "k"=1, "v"=1 → 34 each; MaxSize=67 (one less than two entries)
        table.SetMaxSize(67);
        table.Add("k", "1");
        // CurrentSize=34. Add second entry would be 68 > 67 → evict first
        table.Add("k", "2");
        Assert.Equal(1, table.Count);
        Assert.Equal("2", table.GetEntry(1)!.Value.Value);
    }

    /// RFC 7541 §4 — 100 sequential adds with small MaxSize keeps size bounded
    [Fact(DisplayName = "DT-052: 100 sequential adds with small MaxSize keeps size bounded")]
    public void DynamicTable_HighVolumeAdds_SizeRemainsWithinMaxSize()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(200);

        for (var i = 0; i < 100; i++)
        {
            table.Add("h", i.ToString());
        }

        Assert.True(table.CurrentSize <= 200, $"CurrentSize {table.CurrentSize} exceeds MaxSize 200");
    }

    /// RFC 7541 §4 — After clear via SetMaxSize(0), new entries can be added again
    [Fact(DisplayName = "DT-053: After clear via SetMaxSize(0), new entries can be added again")]
    public void DynamicTable_AfterClear_CanAddNewEntries()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        table.SetMaxSize(0);
        Assert.Equal(0, table.Count);

        table.SetMaxSize(4096);
        table.Add("new", "entry");
        Assert.Equal(1, table.Count);
        Assert.Equal("new", table.GetEntry(1)!.Value.Name);
    }

    /// RFC 7541 §4 — Negative index returns null without throwing
    [Fact(DisplayName = "DT-054: Negative index returns null without throwing")]
    public void DynamicTable_NegativeIndex_ReturnsNull()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y");
        Assert.Null(table.GetEntry(-1));
    }
}
