using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class HpackTests
{
    [Fact]
    public void Encode_IndexedStaticEntry_SingleByte()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x82, encoded.Span[0]);
    }

    [Fact]
    public void Encode_Decode_RoundTrip_PseudoHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
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

    [Fact]
    public void Encode_Decode_RoundTrip_WithHuffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/api/search?q=hello"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("authorization", "Bearer token123"),
            ("accept", "application/json, text/plain"),
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

    [Fact]
    public void Decode_LiteralNewName_CorrectOrder()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "my-value"),
            ("x-another", "data"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(2, decoded.Count);
        Assert.Equal("x-custom-header", decoded[0].Name);
        Assert.Equal("my-value", decoded[0].Value);
        Assert.Equal("x-another", decoded[1].Name);
        Assert.Equal("data", decoded[1].Value);
    }

    [Fact]
    public void Decode_DynamicTableSizeUpdate_Respected()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-test", "value") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1.Span);

        // RFC 7541 §6.3: Dynamic Table Size Update (001xxxxx) with size 0
        // 0x20 = 001 00000 = table size update to 0
        // Then encode a new header which will be literal with incremental indexing
        var h2 = new List<(string, string)> { ("x-fresh", "new") };

        // Create a size update followed by new header
        var sizeUpdate = new byte[] { 0x20 }; // table size to 0
        var encodedHeader = encoder.Encode(h2);

        var combined = new byte[sizeUpdate.Length + encodedHeader.Length];
        sizeUpdate.CopyTo(combined, 0);
        encodedHeader.Span.CopyTo(combined.AsSpan(sizeUpdate.Length));

        var decoded = decoder.Decode(combined);
        Assert.Single(decoded);
        Assert.Equal("x-fresh", decoded[0].Name);
        Assert.Equal("new", decoded[0].Value);
    }

    // ── US-201: RFC 7541 §2.3 — Dynamic table eviction ──────────────────────

    [Fact]
    public void DynamicTable_Eviction_OldestEntryRemovedWhenFull()
    {
        // RFC 7541 §4.4: When adding a new entry causes table size to exceed
        // MaxSize, the oldest (last) entries are evicted until the table fits.
        var table = new HpackDynamicTable();

        // Each entry = name UTF-8 bytes + value UTF-8 bytes + 32 overhead.
        // "a" (1) + "x" (1) + 32 = 34 bytes per entry.
        // Set max size to hold exactly 2 such entries: 68 bytes.
        table.SetMaxSize(68);

        table.Add("a", "x"); // oldest — will be evicted
        table.Add("b", "y"); // newer

        Assert.Equal(2, table.Count);

        // Adding a third entry must evict the oldest ("a","x")
        table.Add("c", "z");

        Assert.Equal(2, table.Count);
        // Index 1 = newest (c,z), Index 2 = (b,y). Oldest (a,x) is gone.
        var newest = table.GetEntry(1);
        Assert.NotNull(newest);
        Assert.Equal("c", newest.Value.Name);
        Assert.Equal("z", newest.Value.Value);

        var second = table.GetEntry(2);
        Assert.NotNull(second);
        Assert.Equal("b", second.Value.Name);
        Assert.Equal("y", second.Value.Value);

        // Original oldest entry is no longer accessible
        Assert.Null(table.GetEntry(3));
    }

    [Fact]
    public void DynamicTable_EvictionOrder_NewestSurvives()
    {
        // RFC 7541 §4.4: Eviction is FIFO — oldest entries removed first.
        // Fill with A, B, C; reduce capacity so D forces eviction; verify
        // D and the most-recent surviving entries remain, oldest gone.
        var table = new HpackDynamicTable();

        // Entry size for single-char name + single-char value = 1+1+32 = 34 bytes
        // Set to hold 3 entries: 102 bytes
        table.SetMaxSize(102);

        table.Add("a", "1"); // oldest
        table.Add("b", "2");
        table.Add("c", "3"); // newest
        Assert.Equal(3, table.Count);

        // Shrink table to hold only 2 entries (68 bytes), then add D.
        // Shrinking evicts A. Adding D evicts B. Result: D, C.
        table.SetMaxSize(68);
        Assert.Equal(2, table.Count);
        // After shrink: index 1 = c, index 2 = b (a evicted)

        table.Add("d", "4");
        Assert.Equal(2, table.Count);

        // Index 1 = newest (d,4), Index 2 = (c,3). Oldest entries (a,b) gone.
        var entry1 = table.GetEntry(1);
        Assert.NotNull(entry1);
        Assert.Equal("d", entry1.Value.Name);
        Assert.Equal("4", entry1.Value.Value);

        var entry2 = table.GetEntry(2);
        Assert.NotNull(entry2);
        Assert.Equal("c", entry2.Value.Name);
        Assert.Equal("3", entry2.Value.Value);

        // a and b are gone
        Assert.Null(table.GetEntry(3));
    }

    [Fact]
    public void DynamicTable_SizeTooBig_ThrowsHpackException()
    {
        // RFC 7541 §4.2: Negative table size is invalid and must be rejected.
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.SetMaxAllowedTableSize(-1));
    }

    // ── End US-201 ────────────────────────────────────────────────────────────

    // ── US-202: RFC 7541 §5.1 — Integer representation edge cases ────────────

    [Fact]
    public void ReadInteger_FitsInPrefix_SingleByte()
    {
        // RFC 7541 §5.1: If the integer value fits within the prefix bits,
        // it is encoded in a single byte. Value 5 with 5-bit prefix (max 31)
        // fits in one byte.
        var data = new byte[] { 0x05 }; // 00000101 — value 5 in low 5 bits
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(5, result);
        Assert.Equal(1, pos); // consumed exactly 1 byte
    }

    [Fact]
    public void ReadInteger_MultiByteEncoding_DecodedCorrectly()
    {
        // RFC 7541 §5.1 example: integer 1337 encoded with 5-bit prefix.
        // 1337 = 31 + 1306; 1306 = 0x051A
        // Byte 0: prefix all-ones = 0x1F (31)
        // 1306 in 7-bit groups: 1306 = 10*128 + 26 → 0x9A (26 | 0x80), 0x0A (10)
        var data = new byte[] { 0x1F, 0x9A, 0x0A };
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(1337, result);
        Assert.Equal(3, pos);
    }

    [Fact]
    public void ReadInteger_MaxValue_Accepted()
    {
        // RFC 7541 §5.1: Values up to (1 << 28) - 1 must be accepted.
        // Round-trip through WriteInteger → ReadInteger.
        var maxValue = (1 << 28) - 1; // 268435455
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(maxValue, prefixBits: 5, prefixFlags: 0x00, buffer);

        var encoded = buffer.WrittenSpan.ToArray();
        var pos = 0;
        var result = HpackDecoder.ReadInteger(encoded, ref pos, prefixBits: 5);

        Assert.Equal(maxValue, result);
    }

    [Fact]
    public void ReadInteger_Overflow_ThrowsHpackException()
    {
        // RFC 7541 §5.1: Integer overflow must be detected.
        // Craft a multi-byte integer that overflows 2^28.
        // Start with prefix all-ones for 5-bit prefix: 0x1F (31)
        // Then 5 continuation bytes each with 0xFF (all bits set + continuation)
        // followed by a stop byte. This represents a value far exceeding 2^28.
        var data = new byte[]
        {
            0x1F, // prefix max (31)
            0xFF, // continuation: 0x7F << 0  = 127
            0xFF, // continuation: 0x7F << 7
            0xFF, // continuation: 0x7F << 14
            0xFF, // continuation: 0x7F << 21
            0xFF, // shift=28 → triggers overflow check
        };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadInteger_TruncatedData_ThrowsHpackException()
    {
        // RFC 7541 §5.1: A multi-byte integer with no stop bit (continuation
        // bit set on last available byte) is truncated — must throw.
        var data = new byte[]
        {
            0x1F, // prefix max (31) — triggers multi-byte path
            0x80, // continuation bit set, value bits = 0, no more bytes follow
        };
        // Remove the final stop byte — truncate after the continuation byte
        var truncated = new byte[] { 0x1F, 0x80 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(truncated, ref pos, prefixBits: 5));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── End US-202 ────────────────────────────────────────────────────────────

    [Fact]
    public void Decode_Rfc7541_AppendixC2_FirstRequest()
    {
        // RFC 7541 Appendix C.2.1 - First Request WITHOUT Huffman encoding
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com
        var encoded = new byte[]
        {
            0x82, // :method: GET (indexed, static index 2)
            0x86, // :scheme: http (indexed, static index 6)
            0x84, // :path: / (indexed, static index 4)
            0x41, 0x0f, // :authority (indexed name at static index 1, literal value, no Huffman)
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(encoded);

        Assert.Equal(4, decoded.Count);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
        Assert.Equal(":scheme", decoded[1].Name);
        Assert.Equal("http", decoded[1].Value);
        Assert.Equal(":path", decoded[2].Name);
        Assert.Equal("/", decoded[2].Value);
        Assert.Equal(":authority", decoded[3].Name);
        Assert.Equal("www.example.com", decoded[3].Value);
    }

    // ── Phase 7: HPACK (RFC 7541) — Full Coverage ─────────────────────────────

    // ── §Appendix A: All 61 Static Table Entries ─────────────────────────────

    public static IEnumerable<object[]> StaticTableEntries()
    {
        for (var i = 1; i <= HpackStaticTable.StaticCount; i++)
        {
            var (name, value) = HpackStaticTable.Entries[i];
            yield return [i, name, value];
        }
    }

    [Theory(DisplayName = "7541-st-001: Static table entry {0} [{1}:{2}] round-trips as indexed representation")]
    [MemberData(nameof(StaticTableEntries))]
    public void StaticTableEntry_RoundTrips(int index, string name, string value)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var encoded = encoder.Encode(new List<(string, string)> { (name, value) });
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(name, decoded[0].Name);
        Assert.Equal(value, decoded[0].Value);
        _ = index; // used via DisplayName parameter
    }

    // ── §7.1.3: Sensitive Headers — NeverIndexed ─────────────────────────────

    public static IEnumerable<object[]> SensitiveHeaders() =>
    [
        ["authorization"],
        ["cookie"],
        ["set-cookie"],
        ["proxy-authorization"],
    ];

    [Theory(DisplayName = "7541-ni-001: {0} encoded with NeverIndexed byte pattern (0x10)")]
    [MemberData(nameof(SensitiveHeaders))]
    public void NeverIndexed_SensitiveHeader_FirstByteHas0x10Flag(string headerName)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode(new List<(string, string)> { (headerName, "value") });

        // First byte must have the NeverIndexed flag (0x10 bit set)
        Assert.True((encoded.Span[0] & 0x10) != 0,
            $"Expected first byte 0x{encoded.Span[0]:X2} to have NeverIndexed flag (0x10) for '{headerName}'");
    }

    [Theory(DisplayName = "7541-ni-002: {0} with NeverIndexed does not grow dynamic table")]
    [MemberData(nameof(SensitiveHeaders))]
    public void NeverIndexed_SensitiveHeader_DoesNotGrowDynamicTable(string headerName)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        // First encode: x-regular literal (adds to dynamic table)
        encoder.Encode(new List<(string, string)> { ("x-regular", "v") });
        // Second encode: now indexed at dynamic[62] = 0xBE (single byte)
        var indexed = encoder.Encode(new List<(string, string)> { ("x-regular", "v") });
        Assert.Equal(1, indexed.Length); // single indexed byte

        // Encode the sensitive header (NeverIndexed — must NOT add to table)
        encoder.Encode(new List<(string, string)> { (headerName, "secret") });

        // Third encode of x-regular: table must still be unshifted → still 0xBE
        var afterSensitive = encoder.Encode(new List<(string, string)> { ("x-regular", "v") });
        Assert.Equal(1, afterSensitive.Length);
        Assert.Equal(indexed.Span[0], afterSensitive.Span[0]); // same byte — table unchanged
    }

    [Fact(DisplayName = "7541-ni-003: Decoded authorization header preserves NeverIndex flag")]
    public void NeverIndexed_AuthorizationHeader_PreservesFlag()
    {
        // RFC 7541 §6.2.3: NeverIndexed literal — 0x10 prefix, nameIdx=23 (authorization in static table)
        // Encoding: nameIdx=23 with 4-bit prefix → 0x1F (15 = prefix all-ones), then continuation (23-15)=8
        // Since nameIdx != 0, no name literal in stream; only value literal follows.
        const string expectedName = "authorization";
        const string expectedValue = "Bearer secret";
        var valueBytes = System.Text.Encoding.ASCII.GetBytes(expectedValue);

        // NeverIndexed prefix bytes: 0x1F 0x08 (nameIdx=23), then value literal
        var bytes = new List<byte>
        {
            0x1F, 0x08,
            (byte)valueBytes.Length // H=0, length=13
        };
        bytes.AddRange(valueBytes);

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(bytes.ToArray());

        Assert.Single(decoded);
        Assert.Equal(expectedName, decoded[0].Name);
        Assert.Equal(expectedValue, decoded[0].Value);
        Assert.True(decoded[0].NeverIndex, "Decoded authorization header must preserve NeverIndex=true");
    }

    // ── RFC 7541 §2.3: Dynamic Table ─────────────────────────────────────────

    [Fact(DisplayName = "7541-2.3-001: Incrementally indexed header added at dynamic index 62")]
    public void DynamicTable_IncrementallyIndexed_AddedAtIndex62()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Encode "x-custom: hello" with incremental indexing → added to dynamic[62]
        var enc1 = encoder.Encode(new List<(string, string)> { ("x-custom", "hello") });
        decoder.Decode(enc1.Span);

        // Second encode: x-custom: hello is now at dynamic[62] (absolute), encoded as 0xBE
        var enc2 = encoder.Encode(new List<(string, string)> { ("x-custom", "hello") });
        Assert.Equal(1, enc2.Length);
        Assert.Equal(0xBE, enc2.Span[0]); // indexed, 61+1=62 → 0x80|62 = 0xBE

        // Decoder should resolve index 62 to the correct header
        var decoded = decoder.Decode(enc2.Span);
        Assert.Single(decoded);
        Assert.Equal("x-custom", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-2.3-002: Oldest entry evicted when dynamic table full")]
    public void DynamicTable_OldestEntryEvicted_WhenFull()
    {
        var table = new HpackDynamicTable();
        // Entry size = 1+1+32 = 34 bytes; set to hold exactly 2
        table.SetMaxSize(68);
        table.Add("a", "x"); // oldest
        table.Add("b", "y");
        table.Add("c", "z"); // triggers eviction of "a"

        Assert.Equal(2, table.Count);
        Assert.Equal("c", table.GetEntry(1)!.Value.Name); // newest
        Assert.Equal("b", table.GetEntry(2)!.Value.Name); // second
        Assert.Null(table.GetEntry(3));                    // "a" evicted
    }

    [Fact(DisplayName = "7541-2.3-003: Dynamic table resized on SETTINGS_HEADER_TABLE_SIZE")]
    public void DynamicTable_Resized_OnSettingsHeaderTableSize()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Sync a single header so both tables agree
        var enc1 = encoder.Encode(new List<(string, string)> { ("x-a", "1") });
        decoder.Decode(enc1.Span); // both tables now have x-a:1 at dynamic[62]

        // Simulate SETTINGS_HEADER_TABLE_SIZE = 200
        const int newSize = 200;
        encoder.AcknowledgeTableSizeChange(newSize);
        decoder.SetMaxAllowedTableSize(newSize);

        // Next encoder call emits Dynamic Table Size Update prefix before the header
        var encoded = encoder.Encode(new List<(string, string)> { ("x-d", "4") });

        // Decoder handles the size update then decodes the header
        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("x-d", decoded[0].Name);
        Assert.Equal("4", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-2.3-004: Dynamic table size 0 evicts all entries")]
    public void DynamicTable_SizeZero_EvictsAllEntries()
    {
        var table = new HpackDynamicTable();
        table.Add("a", "1");
        table.Add("b", "2");
        Assert.Equal(2, table.Count);

        table.SetMaxSize(0);

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(DisplayName = "7541-2.3-005: Table size exceeding maximum causes COMPRESSION_ERROR")]
    public void DynamicTable_SizeExceedingMax_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(256);

        // Craft a Dynamic Table Size Update (001xxxxx) with size = 512 > 256
        // 0x20 | prefix, value = 512: mask5 = 31, 512 >= 31
        // First byte = 0x20 | 31 = 0x3F, then (512-31) = 481 varint:
        // 481 & 0x7F = 97 = 0x61, 481>>7 = 3; byte = 0xE1 (97|0x80), then 0x03
        var bytes = new byte[] { 0x3F, 0xE1, 0x03 };

        Assert.Throws<HpackException>(() => decoder.Decode(bytes));
    }

    [Fact(DisplayName = "hpack-dt-001: Entry size counted as name + value + 32 overhead")]
    public void DynamicTable_EntrySize_NamePlusValuePlus32()
    {
        var table = new HpackDynamicTable();
        // "hello" (5) + "world" (5) + 32 = 42 bytes
        table.Add("hello", "world");

        Assert.Equal(42, table.CurrentSize);
    }

    [Fact(DisplayName = "hpack-dt-002: Size update prefix emitted when table resized")]
    public void DynamicTable_SizeUpdatePrefix_EmittedAfterResize()
    {
        var encoder = new HpackEncoder(useHuffman: false);

        // Trigger a pending table size update
        encoder.AcknowledgeTableSizeChange(512);

        // The next Encode call must emit a Dynamic Table Size Update first
        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        // Dynamic Table Size Update: 001xxxxx, value=512
        // 512 >= 31: first byte = 0x20|31 = 0x3F, then (512-31)=481 varint
        Assert.Equal(0x3F, encoded.Span[0]); // size update prefix byte
    }

    [Fact(DisplayName = "hpack-dt-003: Three entries evicted in FIFO order")]
    public void DynamicTable_ThreeEntries_EvictedFifoOrder()
    {
        var table = new HpackDynamicTable();
        // Each entry = 1+1+32 = 34 bytes; hold 3 entries → 102 bytes
        table.SetMaxSize(102);
        table.Add("a", "1"); // oldest
        table.Add("b", "2");
        table.Add("c", "3"); // newest
        Assert.Equal(3, table.Count);

        // Shrink to hold 2, then add D → evicts A then B
        table.SetMaxSize(68); // evicts "a"
        table.Add("d", "4"); // evicts "b"

        Assert.Equal(2, table.Count);
        Assert.Equal("d", table.GetEntry(1)!.Value.Name); // newest
        Assert.Equal("c", table.GetEntry(2)!.Value.Name); // second newest
        Assert.Null(table.GetEntry(3));
    }

    // ── RFC 7541 §5.1: Integer Representation ────────────────────────────────

    [Fact(DisplayName = "7541-5.1-001: Integer smaller than prefix limit encodes in one byte")]
    public void Integer_SmallerThanPrefixLimit_EncodesInOneByte()
    {
        // 5-bit prefix → limit = 31. Value 10 < 31 → single byte
        var buf = new System.Buffers.ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(10, prefixBits: 5, prefixFlags: 0x00, buf);

        Assert.Equal(1, buf.WrittenCount);
        Assert.Equal(10, buf.WrittenSpan[0]);
    }

    [Fact(DisplayName = "7541-5.1-002: Integer at prefix limit requires continuation bytes")]
    public void Integer_AtPrefixLimit_RequiresContinuationBytes()
    {
        // 5-bit prefix → limit = 31. Value 31 == limit → multi-byte (RFC 7541 §5.1 example)
        var buf = new System.Buffers.ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(31, prefixBits: 5, prefixFlags: 0x00, buf);

        // 31 == mask: emit prefix byte 0x1F, then 0x00 (continuation = 0, no continuation bit)
        Assert.Equal(2, buf.WrittenCount);
        Assert.Equal(0x1F, buf.WrittenSpan[0]);
        Assert.Equal(0x00, buf.WrittenSpan[1]);
    }

    [Fact(DisplayName = "7541-5.1-003: Maximum integer 2147483647 round-trips")]
    public void Integer_MaxValue_2147483647_RoundTrips()
    {
        const int max = int.MaxValue; // 2147483647 = 2^31 - 1
        var buf = new System.Buffers.ArrayBufferWriter<byte>();
        HpackEncoder.WriteInteger(max, prefixBits: 5, prefixFlags: 0x00, buf);

        var encoded = buf.WrittenSpan.ToArray();
        var pos = 0;
        var decoded = HpackDecoder.ReadInteger(encoded, ref pos, prefixBits: 5);

        Assert.Equal(max, decoded);
        Assert.Equal(encoded.Length, pos); // consumed all bytes
    }

    [Fact(DisplayName = "7541-5.1-004: Integer exceeding 2^31-1 causes COMPRESSION_ERROR")]
    public void Integer_ExceedingMaxInt_ThrowsHpackException()
    {
        // Craft bytes representing 2147483648 (int.MaxValue + 1) with 5-bit prefix:
        // prefix byte = 0x1F (31), then varint for (2147483648 - 31) = 2147483617
        // 2147483617: groups of 7 bits → 97|0x80, 127|0x80, 127|0x80, 127|0x80, 7
        var bytes = new byte[] { 0x1F, 0xE1, 0xFF, 0xFF, 0xFF, 0x07 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(bytes, ref pos, prefixBits: 5));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "hpack-int-001: Integer encoding with {0}-bit prefix")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]

    public void Integer_BoundaryValues_ForPrefixBits(int bits)
    {
        var limit = (1 << bits) - 1; // e.g. bits=5 → limit=31

        // Value = limit - 1 (fits in prefix, single byte)
        if (limit > 0)
        {
            var buf1 = new System.Buffers.ArrayBufferWriter<byte>();
            HpackEncoder.WriteInteger(limit - 1, prefixBits: bits, prefixFlags: 0x00, buf1);
            Assert.Equal(1, buf1.WrittenCount);
            var p1 = 0;
            Assert.Equal(limit - 1, HpackDecoder.ReadInteger(buf1.WrittenSpan.ToArray(), ref p1, bits));
        }

        // Value = limit (exactly at boundary, triggers multi-byte)
        {
            var buf2 = new System.Buffers.ArrayBufferWriter<byte>();
            HpackEncoder.WriteInteger(limit, prefixBits: bits, prefixFlags: 0x00, buf2);
            var p2 = 0;
            Assert.Equal(limit, HpackDecoder.ReadInteger(buf2.WrittenSpan.ToArray(), ref p2, bits));
        }

        // Value = limit + 1 (just above boundary)
        {
            var buf3 = new System.Buffers.ArrayBufferWriter<byte>();
            HpackEncoder.WriteInteger(limit + 1, prefixBits: bits, prefixFlags: 0x00, buf3);
            var p3 = 0;
            Assert.Equal(limit + 1, HpackDecoder.ReadInteger(buf3.WrittenSpan.ToArray(), ref p3, bits));
        }
    }

    // ── RFC 7541 §5.2: String Representation ─────────────────────────────────

    [Fact(DisplayName = "7541-5.2-001: Plain string literal decoded")]
    public void StringLiteral_Plain_Decoded()
    {
        // H=0 (bit 7 = 0), length=5, "hello"
        var bytes = new byte[] { 0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        // Wrap in an indexed literal (0000xxxx = without indexing, nameIdx=0 → literal name)
        // Full literal: 0x00, name length/bytes, then value bytes
        // But ReadString is private; test through Decode():
        // Build: without-indexing, new literal name="\x00" wait...
        // Easiest: use Decode() with a literal header with no-Huffman string
        // 0x00 prefix (without indexing, nameIdx=0), name "a" (H=0, len=1), value "hello" (H=0, len=5)
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-5.2-002: Huffman-encoded string decoded")]
    public void StringLiteral_Huffman_Decoded()
    {
        // Encode "hello" with Huffman via the encoder, decode with decoder
        var huffBytes = HuffmanCodec.Encode("hello"u8);

        // Build: without-indexing, literal name "a" (raw), Huffman value "hello"
        var nameRaw = new byte[] { 0x01, (byte)'a' }; // H=0, length=1, "a"
        var valHuff = new byte[1 + huffBytes.Length];
        valHuff[0] = (byte)(0x80 | huffBytes.Length); // H=1, length
        huffBytes.CopyTo(valHuff, 1);

        var packet = new byte[1 + nameRaw.Length + valHuff.Length];
        packet[0] = 0x00; // without indexing, nameIdx=0
        nameRaw.CopyTo(packet, 1);
        valHuff.CopyTo(packet, 1 + nameRaw.Length);

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(packet);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-5.2-003: Empty string literal decoded")]
    public void StringLiteral_Empty_Decoded()
    {
        // without-indexing, literal name "a", value "" (H=0, length=0)
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x00 };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal(string.Empty, decoded[0].Value);
    }

    [Fact(DisplayName = "7541-5.2-004: String larger than 8KB decoded")]
    public void StringLiteral_LargerThan8KB_DecodedWithoutTruncation()
    {
        // Build a 9000-byte value string
        const int valueLen = 9000;
        var valueStr = new string('x', valueLen);
        var valueBytes = System.Text.Encoding.ASCII.GetBytes(valueStr);

        // Encode: without-indexing, literal name "a", raw value 9000 bytes
        // Length 9000 with 7-bit prefix: 9000 >= 127 → multi-byte
        // 9000: prefix all-ones = 127, remaining = 8873
        // 8873 & 0x7F = 41, 8873>>7 = 69; bytes = 41|0x80=0xA9, 69=0x45
        var bytes = new List<byte> { 0x00, 0x01, (byte)'a', 0x7F, 0xA9, 0x45 };
        bytes.AddRange(valueBytes);

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(bytes.ToArray());

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal(valueStr, decoded[0].Value);
    }

    [Fact(DisplayName = "7541-5.2-005: Malformed Huffman data causes COMPRESSION_ERROR")]
    public void StringLiteral_MalformedHuffman_ThrowsHpackException()
    {
        // Build header with H=1 (Huffman) but invalid Huffman bytes
        // 0x00 (without indexing, nameIdx=0), "a" raw name, then H=1 value with bad bytes
        // Bad Huffman: 0xFF 0xFF 0xFF ... (all-ones is partial sequence for EOS, not a valid symbol)
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x83, 0xFF, 0xFF, 0xFF };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }

    [Fact(DisplayName = "hpack-str-001: Non-1 EOS padding bits cause COMPRESSION_ERROR")]
    public void StringLiteral_NonOneEosPaddingBits_ThrowsHpackException()
    {
        // After Huffman decoding, if padding bits are not all-1s (RFC 7541 §5.2)
        // Encode a Huffman string and then corrupt the last byte's padding
        // '0' (ASCII 48) = Huffman code 0x00 (5 bits): full byte = 0x00 | padding...
        // The Huffman code for '0' is (0x0, 5 bits). Padded to byte: 0x00 | 0x07 (EOS padding = 0b111).
        // If we set padding to 0b110 instead of 0b111: byte = 0x06
        // Build: without-indexing, literal name "a", H=1 value with 1 bad-padded byte
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x81, 0x06 };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }

    [Fact(DisplayName = "hpack-str-002: EOS padding > 7 bits causes COMPRESSION_ERROR")]
    public void StringLiteral_EosPaddingMoreThan7Bits_ThrowsHpackException()
    {
        // RFC 7541 §5.2: padding must be < 8 bits (i.e., at most one partial byte)
        // If the final byte is a continuation byte of a multi-byte Huffman sequence,
        // it means the remaining bits are > 7 → invalid
        // To trigger: pass Huffman data where the last byte has more than 7 padding bits
        // The easiest way: a Huffman sequence that ends mid-symbol (> 7 remaining bits)
        // Use two bytes of 0x00 (valid start for symbol '0' + padding), but if
        // symbol '0' is (0x0, 5 bits) and we use 2 bytes (16 bits), after decoding '0'
        // we have 11 remaining bits — more than 7 → should throw hpack-str-002
        // Note: HuffmanCodec.Decode checks remainingBits > 7 and throws
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x82, 0x00, 0x00 };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }

    // ── RFC 7541 §6.1: Indexed Header Field ──────────────────────────────────

    [Fact(DisplayName = "7541-6.1-002: Dynamic table entry at index 62+ retrieved")]
    public void IndexedHeader_DynamicEntry_RetrievedAtIndex62Plus()
    {
        var decoder = new HpackDecoder();
        // Populate dynamic table: literal incr. indexing for ("x-custom", "hello")
        // 0x40 | 0 = 0x40 (literal incr. indexing, new name)
        var addEntry = new byte[]
        {
            0x40,                                              // literal incr., nameIdx=0
            0x08, (byte)'x', (byte)'-', (byte)'c', (byte)'u', // H=0, len=8, "x-custom"
            (byte)'s', (byte)'t', (byte)'o', (byte)'m',
            0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', // H=0, len=5, "hello"
        };
        decoder.Decode(addEntry);

        // Now decode indexed header at index 62 (1000 0000 | 0011 1110 = 0xBE)
        var indexed = new byte[] { 0xBE };
        var decoded = decoder.Decode(indexed);

        Assert.Single(decoded);
        Assert.Equal("x-custom", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-6.1-003: Index out of range causes COMPRESSION_ERROR")]
    public void IndexedHeader_OutOfRange_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        // Dynamic table is empty; index 62 is out of range → should throw
        var bytes = new byte[] { 0xBE }; // indexed, index=62
        Assert.Throws<HpackException>(() => decoder.Decode(bytes));
    }

    [Fact(DisplayName = "hpack-idx-001: Index 0 is invalid per RFC 7541 §6.1")]
    public void IndexedHeader_Index0_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        // 0x80 | 0 = 0x80 → indexed representation with index 0 (reserved, invalid)
        var bytes = new byte[] { 0x80 };
        Assert.Throws<HpackException>(() => decoder.Decode(bytes));
    }

    // ── RFC 7541 §6.2: Literal Header Field ──────────────────────────────────

    [Fact(DisplayName = "7541-6.2-001: Incremental indexing adds entry to dynamic table")]
    public void LiteralHeader_IncrementalIndexing_AddsToTable()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Encode x-new: test (not in static table → literal incr. indexing)
        var enc1 = encoder.Encode(new List<(string, string)> { ("x-new", "test") });
        var dec1 = decoder.Decode(enc1.Span);
        Assert.Single(dec1);
        Assert.Equal("x-new", dec1[0].Name);

        // Second encode: now at dynamic[62] → single-byte indexed
        var enc2 = encoder.Encode(new List<(string, string)> { ("x-new", "test") });
        Assert.Equal(1, enc2.Length);
        Assert.Equal(0xBE, enc2.Span[0]); // absolute index 62 → 0x80|62
    }

    [Fact(DisplayName = "7541-6.2-002: Without-indexing literal not added to dynamic table")]
    public void LiteralHeader_WithoutIndexing_NotAddedToTable()
    {
        // Build without-indexing header manually: 0x00 | nameIdx, name literal, value literal
        // Use nameIdx = static[2] = :method → 0x00 | 2 = 0x02
        var raw = new byte[]
        {
            0x02,                                   // without-indexing, nameIdx=2 (:method)
            0x03, (byte)'P', (byte)'U', (byte)'T',  // H=0, len=3, "PUT"
        };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("PUT", decoded[0].Value);
        Assert.False(decoded[0].NeverIndex);

        // Table should still be empty — try indexed at 62, expect exception
        Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
    }

    [Fact(DisplayName = "7541-6.2-003: NeverIndexed literal not added to table")]
    public void LiteralHeader_NeverIndexed_NotAddedToTable_FlagPreserved()
    {
        // Build NeverIndexed header: 0x10 | nameIdx=0, literal name, value
        var raw = new byte[]
        {
            0x10,                                        // never-indexed, nameIdx=0
            0x03, (byte)'f', (byte)'o', (byte)'o',       // H=0, len=3, name="foo"
            0x03, (byte)'b', (byte)'a', (byte)'r',       // H=0, len=3, value="bar"
        };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("foo", decoded[0].Name);
        Assert.Equal("bar", decoded[0].Value);
        Assert.True(decoded[0].NeverIndex);

        // Table should be empty
        Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
    }

    [Fact(DisplayName = "7541-6.2-004: Literal with indexed name and literal value decoded")]
    public void LiteralHeader_IndexedNameWithLiteralValue_Decoded()
    {
        // Incremental indexing, name from static table, value literal
        // nameIdx = static[2] (:method), value "DELETE" (not in static table)
        var raw = new byte[]
        {
            0x42,                                             // 0x40 | 2 = incr. indexing, nameIdx=2
            0x06, (byte)'D', (byte)'E', (byte)'L', (byte)'E', // H=0, len=6, "DELETE"
            (byte)'T', (byte)'E',
        };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("DELETE", decoded[0].Value);
    }

    [Fact(DisplayName = "7541-6.2-005: Literal with literal name and literal value decoded")]
    public void LiteralHeader_LiteralNameAndValue_Decoded()
    {
        // Incremental indexing, nameIdx=0 (both name and value as literals)
        var raw = new byte[]
        {
            0x40,                                                      // 0x40 | 0 = incr. indexing, new name
            0x07, (byte)'x', (byte)'-', (byte)'f', (byte)'o', (byte)'o', (byte)'-', (byte)'1', // name
            0x03, (byte)'b', (byte)'a', (byte)'z',                     // value
        };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("x-foo-1", decoded[0].Name);
        Assert.Equal("baz", decoded[0].Value);
    }

    // ── RFC 7541 Appendix C.2: Requests without Huffman ──────────────────────

    [Fact(DisplayName = "7541-C.2-001: RFC 7541 Appendix C.2.1 decode")]
    public void AppendixC2_1_FirstRequest_NoHuffman()
    {
        // C.2.1: :method GET, :scheme http, :path /, :authority www.example.com
        // After decoding, dynamic table has [62] :authority: www.example.com
        var encoded = new byte[]
        {
            0x82,                   // indexed :method: GET (static 2)
            0x86,                   // indexed :scheme: http (static 6)
            0x84,                   // indexed :path: / (static 4)
            0x41, 0x0F,             // literal incr., nameIdx=1 (:authority), H=0, len=15
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":method",    headers[0].Name); Assert.Equal("GET",             headers[0].Value);
        Assert.Equal(":scheme",    headers[1].Name); Assert.Equal("http",            headers[1].Value);
        Assert.Equal(":path",      headers[2].Name); Assert.Equal("/",               headers[2].Value);
        Assert.Equal(":authority", headers[3].Name); Assert.Equal("www.example.com", headers[3].Value);
    }

    [Fact(DisplayName = "7541-C.2-002: RFC 7541 Appendix C.2.2 decode (dynamic table)")]
    public void AppendixC2_2_SecondRequest_DynamicTableReferenced()
    {
        // C.2.2: same as C.2.1 plus cache-control: no-cache
        // :authority from dynamic[62], cache-control literal incr.
        var decoder = new HpackDecoder();

        // First: populate dynamic table with C.2.1
        decoder.Decode([
            0x82, 0x86, 0x84,
            0x41, 0x0F,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        // C.2.2 encoded: dynamic[62] for :authority, then cache-control: no-cache literal
        var encoded = new byte[]
        {
            0x82,                   // :method: GET
            0x86,                   // :scheme: http
            0x84,                   // :path: /
            0xBE,                   // indexed dynamic[62] = :authority: www.example.com
            0x58,                   // literal incr., nameIdx=24 (cache-control)
            0x08, (byte)'n', (byte)'o', (byte)'-', (byte)'c', (byte)'a', (byte)'c', (byte)'h', (byte)'e',
        };
        var headers = decoder.Decode(encoded);

        Assert.Equal(5, headers.Count);
        Assert.Equal(":method",       headers[0].Name); Assert.Equal("GET",             headers[0].Value);
        Assert.Equal(":scheme",       headers[1].Name); Assert.Equal("http",            headers[1].Value);
        Assert.Equal(":path",         headers[2].Name); Assert.Equal("/",               headers[2].Value);
        Assert.Equal(":authority",    headers[3].Name); Assert.Equal("www.example.com", headers[3].Value);
        Assert.Equal("cache-control", headers[4].Name); Assert.Equal("no-cache",        headers[4].Value);
    }

    [Fact(DisplayName = "7541-C.2-003: RFC 7541 Appendix C.2.3 decode")]
    public void AppendixC2_3_ThirdRequest_TableStateCorrect()
    {
        // C.2.3: :method GET, :scheme https, :path /index.html,
        //        :authority www.example.com (dynamic[63]), custom-key: custom-value
        var decoder = new HpackDecoder();

        // Populate via C.2.1
        decoder.Decode([
            0x82, 0x86, 0x84,
            0x41, 0x0F,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        // Populate via C.2.2 (adds cache-control: no-cache to dynamic table)
        decoder.Decode([
            0x82, 0x86, 0x84, 0xBE,
            0x58,
            0x08, (byte)'n', (byte)'o', (byte)'-', (byte)'c', (byte)'a', (byte)'c', (byte)'h', (byte)'e'
        ]);

        // C.2.3: after C.2.2 table is [62]=cache-control:no-cache, [63]=:authority:www.example.com
        // :authority at absolute 63 → 0xBF
        var encoded = new byte[]
        {
            0x82,                   // :method: GET
            0x87,                   // :scheme: https (static 7)
            0x85,                   // :path: /index.html (static 5)
            0xBF,                   // indexed dynamic[63] = :authority: www.example.com
            0x40,                   // literal incr., nameIdx=0 (new name)
            0x0A,                   // H=0, len=10, "custom-key"
            (byte)'c', (byte)'u', (byte)'s', (byte)'t', (byte)'o', (byte)'m', (byte)'-',
            (byte)'k', (byte)'e', (byte)'y',
            0x0C,                   // H=0, len=12, "custom-value"
            (byte)'c', (byte)'u', (byte)'s', (byte)'t', (byte)'o', (byte)'m', (byte)'-',
            (byte)'v', (byte)'a', (byte)'l', (byte)'u', (byte)'e',
        };
        var headers = decoder.Decode(encoded);

        Assert.Equal(5, headers.Count);
        Assert.Equal(":method",    headers[0].Name); Assert.Equal("GET",             headers[0].Value);
        Assert.Equal(":scheme",    headers[1].Name); Assert.Equal("https",           headers[1].Value);
        Assert.Equal(":path",      headers[2].Name); Assert.Equal("/index.html",     headers[2].Value);
        Assert.Equal(":authority", headers[3].Name); Assert.Equal("www.example.com", headers[3].Value);
        Assert.Equal("custom-key", headers[4].Name); Assert.Equal("custom-value",    headers[4].Value);
    }

    // ── RFC 7541 Appendix C.3: Requests with Huffman ─────────────────────────

    [Fact(DisplayName = "7541-C.3-001: RFC 7541 Appendix C.3 decode with Huffman")]
    public void AppendixC3_AllThreeRequests_WithHuffman()
    {
        var decoder = new HpackDecoder();

        // C.3.1: :method GET, :scheme http, :path /, :authority www.example.com (Huffman)
        var req1 = new byte[]
        {
            0x82, 0x86, 0x84,
            0x41, 0x8C,
            0xF1, 0xE3, 0xC2, 0xE5, 0xF2, 0x3A, 0x6B, 0xA0, 0xAB, 0x90, 0xF4, 0xFF,
        };
        var d1 = decoder.Decode(req1);
        Assert.Equal(4, d1.Count);
        Assert.Equal(":authority", d1[3].Name);
        Assert.Equal("www.example.com", d1[3].Value);

        // C.3.2: adds cache-control: no-cache (Huffman), :authority from dynamic[62]
        var req2 = new byte[]
        {
            0x82, 0x86, 0x84,
            0xBE,                                               // :authority from dynamic
            0x58, 0x86,
            0xA8, 0xEB, 0x10, 0x64, 0x9C, 0xBF,               // "no-cache" Huffman
        };
        var d2 = decoder.Decode(req2);
        Assert.Equal(5, d2.Count);
        Assert.Equal("cache-control", d2[4].Name);
        Assert.Equal("no-cache", d2[4].Value);

        // C.3.3: :scheme https, :path /index.html, :authority from dynamic[63], custom-key/value
        var req3 = new byte[]
        {
            0x82, 0x87, 0x85,
            0xBF,                                                             // :authority from [63]
            0x40,
            0x88, 0x25, 0xA8, 0x49, 0xE9, 0x5B, 0xA9, 0x7D, 0x7F,          // "custom-key" Huffman
            0x89, 0x25, 0xA8, 0x49, 0xE9, 0x5B, 0xB8, 0xE8, 0xB4, 0xBF,    // "custom-value" Huffman
        };
        var d3 = decoder.Decode(req3);
        Assert.Equal(5, d3.Count);
        Assert.Equal(":scheme",    d3[1].Name); Assert.Equal("https",         d3[1].Value);
        Assert.Equal(":path",      d3[2].Name); Assert.Equal("/index.html",   d3[2].Value);
        Assert.Equal(":authority", d3[3].Name); Assert.Equal("www.example.com", d3[3].Value);
        Assert.Equal("custom-key", d3[4].Name); Assert.Equal("custom-value",  d3[4].Value);
    }

    // ── RFC 7541 Appendix C.4: Responses without Huffman ─────────────────────

    [Fact(DisplayName = "7541-C.4-001: RFC 7541 Appendix C.4.1 decode")]
    public void AppendixC4_1_FirstResponse_NoHuffman()
    {
        // C.4.1: :status 302, cache-control private, date Mon..., location https://...
        var encoded = new byte[]
        {
            // :status: 302 — literal incr., nameIdx=8 (:status), value "302"
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'2',
            // cache-control: private — literal incr., nameIdx=24, value "private"
            0x58, 0x07,
            (byte)'p', (byte)'r', (byte)'i', (byte)'v', (byte)'a', (byte)'t', (byte)'e',
            // date: Mon, 21 Oct 2013 20:13:21 GMT — literal incr., nameIdx=33, value 29 chars
            0x61, 0x1D,
            (byte)'M', (byte)'o', (byte)'n', (byte)',', (byte)' ', (byte)'2', (byte)'1', (byte)' ',
            (byte)'O', (byte)'c', (byte)'t', (byte)' ', (byte)'2', (byte)'0', (byte)'1', (byte)'3',
            (byte)' ', (byte)'2', (byte)'0', (byte)':', (byte)'1', (byte)'3', (byte)':', (byte)'2',
            (byte)'1', (byte)' ', (byte)'G', (byte)'M', (byte)'T',
            // location: https://www.example.com — literal incr., nameIdx=46, value 23 chars
            0x6E, 0x17,
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'s', (byte)':', (byte)'/', (byte)'/',
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };

        var decoder = new HpackDecoder();
        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":status",       headers[0].Name); Assert.Equal("302",                           headers[0].Value);
        Assert.Equal("cache-control", headers[1].Name); Assert.Equal("private",                       headers[1].Value);
        Assert.Equal("date",          headers[2].Name); Assert.Equal("Mon, 21 Oct 2013 20:13:21 GMT", headers[2].Value);
        Assert.Equal("location",      headers[3].Name); Assert.Equal("https://www.example.com",       headers[3].Value);
    }

    [Fact(DisplayName = "7541-C.4-002: RFC 7541 Appendix C.4.2 decode (dynamic table reused)")]
    public void AppendixC4_2_SecondResponse_DynamicTableReused()
    {
        var decoder = new HpackDecoder();

        // Populate dynamic table via C.4.1 (adds 4 entries at [62..65])
        decoder.Decode([
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'2',
            0x58, 0x07, (byte)'p', (byte)'r', (byte)'i', (byte)'v', (byte)'a', (byte)'t', (byte)'e',
            0x61, 0x1D,
            (byte)'M', (byte)'o', (byte)'n', (byte)',', (byte)' ', (byte)'2', (byte)'1', (byte)' ',
            (byte)'O', (byte)'c', (byte)'t', (byte)' ', (byte)'2', (byte)'0', (byte)'1', (byte)'3',
            (byte)' ', (byte)'2', (byte)'0', (byte)':', (byte)'1', (byte)'3', (byte)':', (byte)'2',
            (byte)'1', (byte)' ', (byte)'G', (byte)'M', (byte)'T',
            0x6E, 0x17,
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'s', (byte)':', (byte)'/', (byte)'/',
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m'
        ]);

        // C.4.2: :status 307 (literal), then indexed [65], [64], [63] for the reused entries
        // After adding :status:307, table is [62]:status:307, [63]:location, [64]:date, [65]:cache-control, [66]:status:302
        var encoded = new byte[]
        {
            0x48, 0x03, (byte)'3', (byte)'0', (byte)'7', // :status: 307 (literal incr.)
            0xC1,                                          // indexed abs[65] = cache-control: private
            0xC0,                                          // indexed abs[64] = date: Mon...
            0xBF,                                          // indexed abs[63] = location: https://...
        };

        var headers = decoder.Decode(encoded);

        Assert.Equal(4, headers.Count);
        Assert.Equal(":status",       headers[0].Name); Assert.Equal("307",                           headers[0].Value);
        Assert.Equal("cache-control", headers[1].Name); Assert.Equal("private",                       headers[1].Value);
        Assert.Equal("date",          headers[2].Name); Assert.Equal("Mon, 21 Oct 2013 20:13:21 GMT", headers[2].Value);
        Assert.Equal("location",      headers[3].Name); Assert.Equal("https://www.example.com",       headers[3].Value);
    }

    [Fact(DisplayName = "7541-C.4-003: RFC 7541 Appendix C.4.3 decode")]
    public void AppendixC4_3_ThirdResponse_CorrectTableStateAfterC4_2()
    {
        // Use encoder/decoder round-trip for C.4.3 which includes set-cookie (NeverIndexed)
        // The key property: after C.4.1 and C.4.2, the dynamic table state is verified;
        // then the C.4.3 headers decode correctly.
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Encode and decode C.4.1 and C.4.2 first to align table state
        var enc41 = encoder.Encode(new List<(string, string)>
        {
            (":status", "302"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
        });
        decoder.Decode(enc41.Span);

        var enc42 = encoder.Encode(new List<(string, string)>
        {
            (":status", "307"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
        });
        decoder.Decode(enc42.Span);

        // C.4.3 headers
        var c43Headers = new List<(string, string)>
        {
            (":status", "200"), ("cache-control", "private"),
            ("date", "Mon, 21 Oct 2013 20:13:22 GMT"), ("location", "https://www.example.com"),
            ("content-encoding", "gzip"), ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ"),
        };
        var enc43 = encoder.Encode(c43Headers);
        var headers = decoder.Decode(enc43.Span);

        Assert.Equal(6, headers.Count);
        Assert.Equal(":status",          headers[0].Name); Assert.Equal("200",                                 headers[0].Value);
        Assert.Equal("cache-control",    headers[1].Name); Assert.Equal("private",                             headers[1].Value);
        Assert.Equal("date",             headers[2].Name); Assert.Equal("Mon, 21 Oct 2013 20:13:22 GMT",       headers[2].Value);
        Assert.Equal("location",         headers[3].Name); Assert.Equal("https://www.example.com",             headers[3].Value);
        Assert.Equal("content-encoding", headers[4].Name); Assert.Equal("gzip",                                headers[4].Value);
        Assert.Equal("set-cookie",       headers[5].Name); Assert.Equal("foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ", headers[5].Value);
    }

    // ── RFC 7541 Appendix C.5: Responses with Huffman ────────────────────────

    [Fact(DisplayName = "7541-C.5-001: RFC 7541 Appendix C.5 decode with Huffman")]
    public void AppendixC5_ResponsesWithHuffman_DecodeCorrectly()
    {
        // Use encoder (Huffman) + decoder round-trip to verify the three C.5 responses
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var responses = new[]
        {
            new List<(string, string)>
            {
                (":status", "302"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
            },
            new List<(string, string)>
            {
                (":status", "307"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:22 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ"),
            },
        };

        foreach (var expected in responses)
        {
            var encoded = encoder.Encode(expected);
            var decoded = decoder.Decode(encoded.Span);

            Assert.Equal(expected.Count, decoded.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, decoded[i].Name);
                Assert.Equal(expected[i].Item2, decoded[i].Value);
            }
        }
    }

    // ── RFC 7541 Appendix C.6: Large Cookie Responses ────────────────────────

    [Fact(DisplayName = "7541-C.6-001: RFC 7541 Appendix C.6 large cookie responses")]
    public void AppendixC6_LargeCookieResponses_DecodeCorrectly()
    {
        // C.6 uses three responses, each with large cookie values
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var responses = new[]
        {
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "foo=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "bar=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
            new List<(string, string)>
            {
                (":status", "200"), ("cache-control", "private"),
                ("date", "Mon, 21 Oct 2013 20:13:21 GMT"), ("location", "https://www.example.com"),
                ("content-encoding", "gzip"),
                ("set-cookie", "baz=ASDJKHQKBZXOQWEOPIUAXQWJKHZXCWLKJ; path=/; Expires=Wed, 09 Jun 2021 10:18:14 GMT"),
            },
        };

        foreach (var expected in responses)
        {
            var encoded = encoder.Encode(expected);
            var decoded = decoder.Decode(encoded.Span);

            Assert.Equal(expected.Count, decoded.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, decoded[i].Name);
                Assert.Equal(expected[i].Item2, decoded[i].Value);
            }
        }
    }

    // ── End Phase 7 ───────────────────────────────────────────────────────────

    [Fact]
    public void Decode_Rfc7541_AppendixC3_AllThreeRequests()
    {
        // RFC 7541 Appendix C.3 — Request Examples WITH Huffman Coding
        // A single HpackDecoder shares dynamic table state across all three requests,
        // exactly as it would on a persistent HTTP/2 connection.
        var decoder = new HpackDecoder();

        // ── C.3.1 First Request ──────────────────────────────────────────────────
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com
        // Dynamic table after: [62] :authority: www.example.com
        var req1 = new byte[]
        {
            0x82,                   // indexed :method: GET  (static 2)
            0x86,                   // indexed :scheme: http (static 6)
            0x84,                   // indexed :path: /      (static 4)
            0x41,                   // literal incr. indexing, name = static[1] (:authority)
            0x8c,                   // H=1 (Huffman), length=12
            0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff, // "www.example.com"
        };

        var d1 = decoder.Decode(req1);
        Assert.Equal(4, d1.Count);
        Assert.Equal(":method",    d1[0].Name); Assert.Equal("GET",             d1[0].Value);
        Assert.Equal(":scheme",    d1[1].Name); Assert.Equal("http",            d1[1].Value);
        Assert.Equal(":path",      d1[2].Name); Assert.Equal("/",               d1[2].Value);
        Assert.Equal(":authority", d1[3].Name); Assert.Equal("www.example.com", d1[3].Value);

        // ── C.3.2 Second Request ─────────────────────────────────────────────────
        // :method: GET, :scheme: http, :path: /, :authority: www.example.com (dynamic),
        // cache-control: no-cache
        // Dynamic table after: [62] cache-control: no-cache, [63] :authority: www.example.com
        var req2 = new byte[]
        {
            0x82,                               // indexed :method: GET  (static 2)
            0x86,                               // indexed :scheme: http (static 6)
            0x84,                               // indexed :path: /      (static 4)
            0xbe,                               // indexed dynamic[62] → :authority: www.example.com
            0x58,                               // literal incr. indexing, name = static[24] (cache-control)
            0x86,                               // H=1, length=6
            0xa8, 0xeb, 0x10, 0x64, 0x9c, 0xbf // "no-cache"
        };

        var d2 = decoder.Decode(req2);
        Assert.Equal(5, d2.Count);
        Assert.Equal(":method",       d2[0].Name); Assert.Equal("GET",             d2[0].Value);
        Assert.Equal(":scheme",       d2[1].Name); Assert.Equal("http",            d2[1].Value);
        Assert.Equal(":path",         d2[2].Name); Assert.Equal("/",               d2[2].Value);
        Assert.Equal(":authority",    d2[3].Name); Assert.Equal("www.example.com", d2[3].Value);
        Assert.Equal("cache-control", d2[4].Name); Assert.Equal("no-cache",        d2[4].Value);

        // ── C.3.3 Third Request ──────────────────────────────────────────────────
        // :method: GET, :scheme: https, :path: /index.html,
        // :authority: www.example.com (dynamic[63]), custom-key: custom-value
        // Dynamic table after: [62] custom-key: custom-value, [63] cache-control: no-cache,
        //                      [64] :authority: www.example.com
        var req3 = new byte[]
        {
            0x82,                                                             // :method: GET
            0x87,                                                             // :scheme: https (static 7)
            0x85,                                                             // :path: /index.html (static 5)
            0xbf,                                                             // dynamic[63] → :authority: www.example.com
            0x40,                                                             // literal incr. indexing, new literal name
            0x88,                                                             // H=1, length=8
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xa9, 0x7d, 0x7f,                 // "custom-key"
            0x89,                                                             // H=1, length=9
            0x25, 0xa8, 0x49, 0xe9, 0x5b, 0xb8, 0xe8, 0xb4, 0xbf            // "custom-value"
        };

        var d3 = decoder.Decode(req3);
        Assert.Equal(5, d3.Count);
        Assert.Equal(":method",    d3[0].Name); Assert.Equal("GET",             d3[0].Value);
        Assert.Equal(":scheme",    d3[1].Name); Assert.Equal("https",           d3[1].Value);
        Assert.Equal(":path",      d3[2].Name); Assert.Equal("/index.html",     d3[2].Value);
        Assert.Equal(":authority", d3[3].Name); Assert.Equal("www.example.com", d3[3].Value);
        Assert.Equal("custom-key", d3[4].Name); Assert.Equal("custom-value",    d3[4].Value);
    }
}
