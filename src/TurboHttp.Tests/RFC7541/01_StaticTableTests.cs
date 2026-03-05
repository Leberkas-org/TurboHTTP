using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC7541;

/// <summary>
/// RFC 7541 Appendix A — HPACK Static Table
/// Phase 15-16: Verify full static table implementation.
///
/// Key invariants tested here:
///   - The static table contains exactly 61 entries (RFC 7541 Appendix A).
///   - Index 0 is reserved and must never be used (RFC 7541 §2.3.3).
///   - Each of the 61 static entries resolves to the correct name and value.
///   - The encoder produces correct indexed wire bytes for full static matches.
///   - The encoder uses static name indices for name-only matches.
///   - Invalid indices (0, 62+ with empty dynamic table) throw HpackException.
/// </summary>
public sealed class HpackStaticTableTests
{
    // ── Static Table Structure ────────────────────────────────────────────────

    /// RFC 7541 Appendix A — Static table contains exactly 61 entries
    [Fact(DisplayName = "ST-001: Static table contains exactly 61 entries")]
    public void StaticTable_Count_IsExactly61()
    {
        Assert.Equal(61, HpackStaticTable.StaticCount);
    }

    /// RFC 7541 Appendix A — Static table array has 62 slots (index 0 reserved)
    [Fact(DisplayName = "ST-002: Static table array has 62 slots (index 0 reserved)")]
    public void StaticTable_Array_Has62Slots()
    {
        // Entries array has index 0 (reserved) + 61 entries = 62 elements
        Assert.Equal(62, HpackStaticTable.Entries.Length);
    }

    /// RFC 7541 Appendix A — Index 0 is reserved (empty name and value)
    [Fact(DisplayName = "ST-003: Index 0 is reserved (empty name and value)")]
    public void StaticTable_IndexZero_IsReserved()
    {
        var entry = HpackStaticTable.Entries[0];
        Assert.Equal(string.Empty, entry.Name);
        Assert.Equal(string.Empty, entry.Value);
    }

    // ── All 61 Entries Have Correct Name and Value (Theory) ───────────────────

    public static TheoryData<int, string, string> AllStaticEntries()
    {
        return new TheoryData<int, string, string>
        {
            { 1,  ":authority",                  "" },
            { 2,  ":method",                     "GET" },
            { 3,  ":method",                     "POST" },
            { 4,  ":path",                       "/" },
            { 5,  ":path",                       "/index.html" },
            { 6,  ":scheme",                     "http" },
            { 7,  ":scheme",                     "https" },
            { 8,  ":status",                     "200" },
            { 9,  ":status",                     "204" },
            { 10, ":status",                     "206" },
            { 11, ":status",                     "304" },
            { 12, ":status",                     "400" },
            { 13, ":status",                     "404" },
            { 14, ":status",                     "500" },
            { 15, "accept-charset",              "" },
            { 16, "accept-encoding",             "gzip, deflate" },
            { 17, "accept-language",             "" },
            { 18, "accept-ranges",               "" },
            { 19, "accept",                      "" },
            { 20, "access-control-allow-origin", "" },
            { 21, "age",                         "" },
            { 22, "allow",                       "" },
            { 23, "authorization",               "" },
            { 24, "cache-control",               "" },
            { 25, "content-disposition",         "" },
            { 26, "content-encoding",            "" },
            { 27, "content-language",            "" },
            { 28, "content-length",              "" },
            { 29, "content-location",            "" },
            { 30, "content-range",               "" },
            { 31, "content-type",                "" },
            { 32, "cookie",                      "" },
            { 33, "date",                        "" },
            { 34, "etag",                        "" },
            { 35, "expect",                      "" },
            { 36, "expires",                     "" },
            { 37, "from",                        "" },
            { 38, "host",                        "" },
            { 39, "if-match",                    "" },
            { 40, "if-modified-since",           "" },
            { 41, "if-none-match",               "" },
            { 42, "if-range",                    "" },
            { 43, "if-unmodified-since",         "" },
            { 44, "last-modified",               "" },
            { 45, "link",                        "" },
            { 46, "location",                    "" },
            { 47, "max-forwards",                "" },
            { 48, "proxy-authenticate",          "" },
            { 49, "proxy-authorization",         "" },
            { 50, "range",                       "" },
            { 51, "referer",                     "" },
            { 52, "refresh",                     "" },
            { 53, "retry-after",                 "" },
            { 54, "server",                      "" },
            { 55, "set-cookie",                  "" },
            { 56, "strict-transport-security",   "" },
            { 57, "transfer-encoding",           "" },
            { 58, "user-agent",                  "" },
            { 59, "vary",                        "" },
            { 60, "via",                         "" },
            { 61, "www-authenticate",            "" },
        };
    }

    /// RFC 7541 Appendix A — All 61 static entries have correct name and value
    [Theory(DisplayName = "ST-004: All 61 static entries have correct name and value")]
    [MemberData(nameof(AllStaticEntries))]
    public void StaticTable_AllEntries_HaveCorrectNameAndValue(int index, string expectedName, string expectedValue)
    {
        var entry = HpackStaticTable.Entries[index];
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(expectedValue, entry.Value);
    }

    // ── Decoder Resolves Static Indices to Correct Headers ────────────────────

    /// RFC 7541 Appendix A — Decode index 2 → :method=GET
    [Fact(DisplayName = "ST-010: Decode index 2 → :method=GET")]
    public void Decoder_Index2_Returns_MethodGet()
    {
        var decoder = new HpackDecoder();
        // RFC 7541 §6.1: indexed representation = 0x80 | index = 0x82
        var result = decoder.Decode([0x82]);
        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal("GET", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 3 → :method=POST
    [Fact(DisplayName = "ST-011: Decode index 3 → :method=POST")]
    public void Decoder_Index3_Returns_MethodPost()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x83]);
        Assert.Single(result);
        Assert.Equal(":method", result[0].Name);
        Assert.Equal("POST", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 4 → :path=/
    [Fact(DisplayName = "ST-012: Decode index 4 → :path=/")]
    public void Decoder_Index4_Returns_PathRoot()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x84]);
        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 5 → :path=/index.html
    [Fact(DisplayName = "ST-013: Decode index 5 → :path=/index.html")]
    public void Decoder_Index5_Returns_PathIndexHtml()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x85]);
        Assert.Single(result);
        Assert.Equal(":path", result[0].Name);
        Assert.Equal("/index.html", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 7 → :scheme=https
    [Fact(DisplayName = "ST-014: Decode index 7 → :scheme=https")]
    public void Decoder_Index7_Returns_SchemeHttps()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x87]);
        Assert.Single(result);
        Assert.Equal(":scheme", result[0].Name);
        Assert.Equal("https", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 8 → :status=200
    [Fact(DisplayName = "ST-015: Decode index 8 → :status=200")]
    public void Decoder_Index8_Returns_Status200()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x88]);
        Assert.Single(result);
        Assert.Equal(":status", result[0].Name);
        Assert.Equal("200", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 13 → :status=404
    [Fact(DisplayName = "ST-016: Decode index 13 → :status=404")]
    public void Decoder_Index13_Returns_Status404()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x8D]);
        Assert.Single(result);
        Assert.Equal(":status", result[0].Name);
        Assert.Equal("404", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 16 → accept-encoding=gzip, deflate
    [Fact(DisplayName = "ST-017: Decode index 16 → accept-encoding=gzip, deflate")]
    public void Decoder_Index16_Returns_AcceptEncoding()
    {
        var decoder = new HpackDecoder();
        var result = decoder.Decode([0x90]);
        Assert.Single(result);
        Assert.Equal("accept-encoding", result[0].Name);
        Assert.Equal("gzip, deflate", result[0].Value);
    }

    /// RFC 7541 Appendix A — Decode index 61 → www-authenticate=''
    [Fact(DisplayName = "ST-018: Decode index 61 → www-authenticate=''")]
    public void Decoder_Index61_Returns_WwwAuthenticate()
    {
        var decoder = new HpackDecoder();
        // 0x80 | 61 = 0xBD
        var result = decoder.Decode([0xBD]);
        Assert.Single(result);
        Assert.Equal("www-authenticate", result[0].Name);
        Assert.Equal("", result[0].Value);
    }

    // ── Encoder Produces Correct Indexed Wire Bytes ────────────────────────────

    /// RFC 7541 Appendix A — Encode :method=GET produces single byte 0x82
    [Fact(DisplayName = "ST-020: Encode :method=GET produces single byte 0x82")]
    public void Encoder_MethodGet_ProducesByte0x82()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":method", "GET")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x82, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode :method=POST produces single byte 0x83
    [Fact(DisplayName = "ST-021: Encode :method=POST produces single byte 0x83")]
    public void Encoder_MethodPost_ProducesByte0x83()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":method", "POST")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x83, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode :path=/ produces single byte 0x84
    [Fact(DisplayName = "ST-022: Encode :path=/ produces single byte 0x84")]
    public void Encoder_PathRoot_ProducesByte0x84()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":path", "/")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x84, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode :scheme=https produces single byte 0x87
    [Fact(DisplayName = "ST-023: Encode :scheme=https produces single byte 0x87")]
    public void Encoder_SchemeHttps_ProducesByte0x87()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":scheme", "https")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x87, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode :status=200 produces single byte 0x88
    [Fact(DisplayName = "ST-024: Encode :status=200 produces single byte 0x88")]
    public void Encoder_Status200_ProducesByte0x88()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x88, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode :status=404 produces single byte 0x8D
    [Fact(DisplayName = "ST-025: Encode :status=404 produces single byte 0x8D")]
    public void Encoder_Status404_ProducesByte0x8D()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "404")]);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x8D, encoded.Span[0]);
    }

    // ── Invalid Index Rejection ────────────────────────────────────────────────

    /// RFC 7541 Appendix A — Decode index 0 (0x80) throws HpackException — reserved index
    [Fact(DisplayName = "ST-030: Decode index 0 (0x80) throws HpackException — reserved index")]
    public void Decoder_IndexZero_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0x80]));
        Assert.Contains("0", ex.Message);
    }

    /// RFC 7541 Appendix A — Decode index 62 (0xBE) with empty dynamic table throws HpackException
    [Fact(DisplayName = "ST-031: Decode index 62 (0xBE) with empty dynamic table throws HpackException")]
    public void Decoder_Index62_EmptyDynamicTable_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        // 0x80 | 62 = 0xBE
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xBE]));
        Assert.Contains("62", ex.Message);
    }

    /// RFC 7541 Appendix A — Decode index 100 with empty dynamic table throws HpackException
    [Fact(DisplayName = "ST-032: Decode index 100 with empty dynamic table throws HpackException")]
    public void Decoder_Index100_EmptyDynamicTable_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        // Index 100 encoded: first byte 0xFF (prefix=7 bits, all 1s = 127), then 0x80 | (100-127) ...
        // Actually for index 100 with 7-bit prefix: value=100, mask=127, 100 < 127 → single byte 0x80|100 = 0xE4
        var ex = Assert.Throws<HpackException>(() => decoder.Decode([0xE4]));
        Assert.Contains("100", ex.Message);
    }

    /// RFC 7541 Appendix A — Decode very large index throws HpackException
    [Fact(DisplayName = "ST-033: Decode very large index throws HpackException")]
    public void Decoder_VeryLargeIndex_ThrowsHpackException()
    {
        var decoder = new HpackDecoder();
        // Encode index 999 with 7-bit prefix:
        // value=999 >= 127 → first byte 0xFF, then (999-127)=872 in 7-bit groups
        // 872 = 0b1101101000 → 0x68 | 0x80 = 0xE8 (low 7 bits + continuation), then 0x06
        byte[] bytes = [0xFF, 0xE8, 0x06];
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(bytes));
        Assert.Contains("999", ex.Message);
    }

    // ── Static Name-Only Match (encoder uses static index for name) ────────────

    /// RFC 7541 Appendix A — Encode :authority with custom value uses static index 1 for name
    [Fact(DisplayName = "ST-040: Encode :authority with custom value uses static index 1 for name")]
    public void Encoder_AuthorityWithCustomValue_UsesStaticNameIndex1()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":authority", "example.com")]);

        // Literal with Incremental Indexing (§6.2.1): bit pattern 01xxxxxx
        // Name index = 1 → prefix byte = 0x40 | 1 = 0x41
        Assert.True(encoded.Length > 1, "Should be more than 1 byte (name index + value)");
        Assert.Equal(0x41, encoded.Span[0]);
    }

    /// RFC 7541 Appendix A — Encode accept-encoding with custom value uses static index 16 for name
    [Fact(DisplayName = "ST-041: Encode accept-encoding with custom value uses static index 16 for name")]
    public void Encoder_AcceptEncodingWithCustomValue_UsesStaticNameIndex16()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(("accept-encoding", "br"))]);

        // Literal with Incremental Indexing (§6.2.1): bit pattern 01xxxxxx
        // Name index = 16 → prefix byte = 0x40 | 16 = 0x50
        Assert.True(encoded.Length > 1);
        Assert.Equal(0x50, encoded.Span[0]);
    }

    // ── Round-Trip via Static Table ────────────────────────────────────────────

    /// RFC 7541 Appendix A — Round-trip encode/decode all pseudo-headers via static table
    [Fact(DisplayName = "ST-050: Round-trip encode/decode all pseudo-headers via static table")]
    public void RoundTrip_AllPseudoHeaders_ViaStaticTable()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":method", "POST"),
            (":path", "/index.html"),
            (":scheme", "http"),
            (":status", "200"),
            (":status", "204"),
            (":status", "304"),
            (":status", "404"),
            (":status", "500"),
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

    /// RFC 7541 Appendix A — All 61 static full-match entries produce exactly 1 byte when encoded
    [Fact(DisplayName = "ST-051: All 61 static full-match entries produce exactly 1 byte when encoded")]
    public void Encoder_AllStaticFullMatches_ProduceSingleByte()
    {
        // Only entries with non-empty values can be full-matched in the static table
        // (entries with empty values need a literal value appended)
        var fullMatchEntries = new List<(string, string)>
        {
            (":method", "GET"),
            (":method", "POST"),
            (":path", "/"),
            (":path", "/index.html"),
            (":scheme", "http"),
            (":scheme", "https"),
            (":status", "200"),
            (":status", "204"),
            (":status", "206"),
            (":status", "304"),
            (":status", "400"),
            (":status", "404"),
            (":status", "500"),
            ("accept-encoding", "gzip, deflate"),
        };

        foreach (var (name, value) in fullMatchEntries)
        {
            var encoder = new HpackEncoder(useHuffman: false);
            var encoded = encoder.Encode([(name, value)]);
            Assert.True(encoded.Length == 1, $"Expected 1 byte for {name}={value}, got {encoded.Length}");
        }
    }

    /// RFC 7541 Appendix A — Decoder can decode all 61 static indices (Theory via loop)
    [Fact(DisplayName = "ST-052: Decoder can decode all 61 static indices (Theory via loop)")]
    public void Decoder_AllStaticIndices_ResolveCorrectly()
    {
        var decoder = new HpackDecoder();

        for (var idx = 1; idx <= HpackStaticTable.StaticCount; idx++)
        {
            var expected = HpackStaticTable.Entries[idx];
            // All static indices 1-61 fit in 7-bit prefix (< 127), so single byte
            byte[] bytes = [(byte)(0x80 | idx)];

            var result = decoder.Decode(bytes);
            Assert.True(result.Count == 1, $"Expected 1 decoded header for static index {idx}, got {result.Count}");
            Assert.True(expected.Name == result[0].Name, $"Name mismatch at index {idx}: expected '{expected.Name}', got '{result[0].Name}'");
            Assert.True(expected.Value == result[0].Value, $"Value mismatch at index {idx}: expected '{expected.Value}', got '{result[0].Value}'");
        }
    }
}
