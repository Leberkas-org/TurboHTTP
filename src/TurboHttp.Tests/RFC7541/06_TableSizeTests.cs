using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC7541;

/// <summary>
/// RFC 7540 §6.5.2 — SETTINGS_MAX_HEADER_LIST_SIZE enforcement in the HPACK decoder.
///
/// Per RFC 7541 §4.1 / RFC 7540 §6.5.2:
///   Header List Size = sum of (name_octets + value_octets + 32) for every decoded entry.
/// When the cumulative total exceeds MAX_HEADER_LIST_SIZE, a COMPRESSION_ERROR
/// (connection-level error) MUST be generated.
///
/// Phase 23: Enforce MAX_HEADER_LIST_SIZE / Abort stream if exceeded.
/// </summary>
public sealed class HpackHeaderListSizeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Encodes a raw (non-Huffman) HPACK string literal: [length | raw bytes].</summary>
    private static byte[] RawStr(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        var result = new byte[1 + bytes.Length];
        result[0] = (byte)bytes.Length;
        bytes.CopyTo(result, 1);
        return result;
    }

    /// <summary>Literal without Indexing, new name (§6.2.2): 0x00 + name + value.</summary>
    private static byte[] LiteralNoIndex(string name, string value)
        => [0x00, .. RawStr(name), .. RawStr(value)];

    /// <summary>Literal with Incremental Indexing, new name (§6.2.1): 0x40 + name + value.</summary>
    private static byte[] LiteralIncrementalNew(string name, string value)
        => [0x40, .. RawStr(name), .. RawStr(value)];

    /// <summary>Literal Never Indexed, new name (§6.2.3): 0x10 + name + value.</summary>
    private static byte[] LiteralNeverIndex(string name, string value)
        => [0x10, .. RawStr(name), .. RawStr(value)];

    /// <summary>Indexed header representation (§6.1): single byte with high bit set.</summary>
    private static byte[] Indexed(int idx)
        => [(byte)(0x80 | idx)];

    /// <summary>
    /// Computes the RFC 7540 §6.5.2 header list size for a single entry:
    /// name_octets + value_octets + 32.
    /// </summary>
    private static int EntrySize(string name, string value)
        => Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) + 32;

    private static HpackDecoder NewDecoder() => new();

    // ── HLS-00x: Default (no limit) ──────────────────────────────────────────

    /// RFC 7540 §6.5.2 — Should DecodeHeaders When NoLimitConfigured
    [Fact(DisplayName = "HLS-001: Should_DecodeHeaders_When_NoLimitConfigured")]
    public void HLS_001_Default_NoLimit_ManyHeadersSucceed()
    {
        var decoder = NewDecoder();

        // Build 50 small literal headers — each 34 bytes → total 1700 bytes
        // Default limit is int.MaxValue so none should throw
        var block = new List<byte>();
        for (var i = 0; i < 50; i++)
        {
            block.AddRange(LiteralNoIndex("a", "b"));
        }

        var headers = decoder.Decode(block.ToArray());
        Assert.Equal(50, headers.Count);
    }

    /// RFC 7540 §6.5.2 — Should Throw When LimitIsZeroAndAnyHeaderDecoded
    [Fact(DisplayName = "HLS-002: Should_Throw_When_LimitIsZeroAndAnyHeaderDecoded")]
    public void HLS_002_LimitZero_AnyHeaderThrows()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(0);

        // "a" / "b" → size = 1+1+32 = 34 → exceeds 0
        var block = LiteralNoIndex("a", "b");

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    // ── HLS-01x: Exact limit boundary ────────────────────────────────────────

    /// RFC 7540 §6.5.2 — Should Succeed When HeaderSizeExactlyEqualsLimit
    [Fact(DisplayName = "HLS-010: Should_Succeed_When_HeaderSizeExactlyEqualsLimit")]
    public void HLS_010_HeaderSizeEqualsLimit_Succeeds()
    {
        var decoder = NewDecoder();
        var limit = EntrySize("name", "value"); // 4+5+32 = 41
        decoder.SetMaxHeaderListSize(limit);

        var block = LiteralNoIndex("name", "value");
        var headers = decoder.Decode(block);

        Assert.Single(headers);
        Assert.Equal("name", headers[0].Name);
        Assert.Equal("value", headers[0].Value);
    }

    /// RFC 7540 §6.5.2 — Should Throw When HeaderSizeOneBelowLimit
    [Fact(DisplayName = "HLS-011: Should_Throw_When_HeaderSizeOneBelowLimit")]
    public void HLS_011_HeaderSizeBelowLimitByOne_Throws()
    {
        var decoder = NewDecoder();
        var limit = EntrySize("name", "value") - 1; // 40
        decoder.SetMaxHeaderListSize(limit);

        var block = LiteralNoIndex("name", "value");

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should Succeed When TwoHeadersSumToExactLimit
    [Fact(DisplayName = "HLS-012: Should_Succeed_When_TwoHeadersSumToExactLimit")]
    public void HLS_012_TwoHeadersExactlyAtLimit_Succeeds()
    {
        var decoder = NewDecoder();
        var oneSize = EntrySize("a", "b"); // 1+1+32 = 34
        decoder.SetMaxHeaderListSize(oneSize * 2); // 68

        byte[] block = [.. LiteralNoIndex("a", "b"), .. LiteralNoIndex("a", "b")];

        var headers = decoder.Decode(block);
        Assert.Equal(2, headers.Count);
    }

    /// RFC 7540 §6.5.2 — Should Throw When SecondHeaderExceedsLimit
    [Fact(DisplayName = "HLS-013: Should_Throw_When_SecondHeaderExceedsLimit")]
    public void HLS_013_SecondHeaderExceedsLimit_Throws()
    {
        var decoder = NewDecoder();
        var oneSize = EntrySize("a", "b"); // 34
        decoder.SetMaxHeaderListSize(oneSize * 2 - 1); // 67 (one short of two full entries)

        byte[] block = [.. LiteralNoIndex("a", "b"), .. LiteralNoIndex("a", "b")];

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    // ── HLS-02x: All representation types counted ─────────────────────────────

    /// RFC 7540 §6.5.2 — Should CountIndexedStaticHeader Toward Limit
    [Fact(DisplayName = "HLS-020: Should_CountIndexedStaticHeader_Toward_Limit")]
    public void HLS_020_IndexedStaticHeader_CountedTowardLimit()
    {
        // Static index 8 = ":status" / "200" → 7+3+32 = 42 bytes
        var decoder = NewDecoder();
        var expectedSize = EntrySize(":status", "200"); // 42
        decoder.SetMaxHeaderListSize(expectedSize - 1); // 41 → should throw

        // Indexed header §6.1: 0x80 | 8 = 0x88
        var block = Indexed(8);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should CountIndexedStaticHeader WhenExactlyAtLimit
    [Fact(DisplayName = "HLS-021: Should_CountIndexedStaticHeader_WhenExactlyAtLimit")]
    public void HLS_021_IndexedStaticHeader_AtExactLimit_Succeeds()
    {
        // Static index 8 = ":status" / "200" → 42 bytes
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(EntrySize(":status", "200")); // 42

        var block = Indexed(8);
        var headers = decoder.Decode(block);

        Assert.Single(headers);
        Assert.Equal(":status", headers[0].Name);
        Assert.Equal("200", headers[0].Value);
    }

    /// RFC 7540 §6.5.2 — Should CountLiteralIncrementalIndexing Toward Limit
    [Fact(DisplayName = "HLS-022: Should_CountLiteralIncrementalIndexing_Toward_Limit")]
    public void HLS_022_LiteralIncrementalIndexing_CountedTowardLimit()
    {
        var decoder = NewDecoder();
        var limit = EntrySize("x", "y") - 1; // 33
        decoder.SetMaxHeaderListSize(limit);

        var block = LiteralIncrementalNew("x", "y");

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should CountLiteralNeverIndex Toward Limit
    [Fact(DisplayName = "HLS-023: Should_CountLiteralNeverIndex_Toward_Limit")]
    public void HLS_023_LiteralNeverIndex_CountedTowardLimit()
    {
        var decoder = NewDecoder();
        var limit = EntrySize("secret", "token") - 1;
        decoder.SetMaxHeaderListSize(limit);

        var block = LiteralNeverIndex("secret", "token");

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should CountIndexedDynamicHeader Toward Limit
    [Fact(DisplayName = "HLS-024: Should_CountIndexedDynamicHeader_Toward_Limit")]
    public void HLS_024_IndexedDynamicHeader_CountedTowardLimit()
    {
        var decoder = NewDecoder();

        // First decode: add "custom-header"/"custom-value" to dynamic table at index 62.
        // Allow enough room for this first decode to succeed.
        decoder.SetMaxHeaderListSize(EntrySize("custom-header", "custom-value"));

        var addToTable = LiteralIncrementalNew("custom-header", "custom-value");
        var headers1 = decoder.Decode(addToTable);
        Assert.Single(headers1);

        // Reduce limit so the indexed reference also fails.
        decoder.SetMaxHeaderListSize(EntrySize("custom-header", "custom-value") - 1);

        // Second decode: reference dynamic table index 62 (0x80 | 62 = 0xBE)
        var block = Indexed(62);
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should CountLiteralNoIndexing Toward Limit
    [Fact(DisplayName = "HLS-025: Should_CountLiteralNoIndexing_Toward_Limit")]
    public void HLS_025_LiteralNoIndexing_CountedTowardLimit()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(EntrySize("foo", "bar") - 1);

        var block = LiteralNoIndex("foo", "bar");

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    // ── HLS-03x: Cumulative behavior ──────────────────────────────────────────

    /// RFC 7540 §6.5.2 — Should AccumulateSizeAcrossAllHeaders
    [Fact(DisplayName = "HLS-030: Should_AccumulateSizeAcrossAllHeaders")]
    public void HLS_030_CumulativeSizeAcrossMultipleHeaders()
    {
        var decoder = NewDecoder();
        var oneSize = EntrySize("k", "v"); // 1+1+32 = 34

        // Allow exactly 4 headers (136 bytes) but decode 5
        decoder.SetMaxHeaderListSize(oneSize * 4); // 136

        byte[] block = [
            .. LiteralNoIndex("k", "v"),
            .. LiteralNoIndex("k", "v"),
            .. LiteralNoIndex("k", "v"),
            .. LiteralNoIndex("k", "v"),
            .. LiteralNoIndex("k", "v"), // 5th → exceeds 136
        ];

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should ResetCumulativeSize BetweenDecodeInvocations
    [Fact(DisplayName = "HLS-031: Should_ResetCumulativeSize_BetweenDecodeInvocations")]
    public void HLS_031_CumulativeResets_BetweenDecodeInvocations()
    {
        var decoder = NewDecoder();
        var oneSize = EntrySize("a", "b"); // 34
        decoder.SetMaxHeaderListSize(oneSize * 2); // 68 → two headers per call OK

        byte[] block = [.. LiteralNoIndex("a", "b"), .. LiteralNoIndex("a", "b")];

        // First call: 2 headers = 68 bytes → exactly at limit
        var h1 = decoder.Decode(block);
        Assert.Equal(2, h1.Count);

        // Second call: same 2 headers → cumulative must start fresh → also OK
        var h2 = decoder.Decode(block);
        Assert.Equal(2, h2.Count);
    }

    /// RFC 7540 §6.5.2 — Should Throw When SingleLargeValueExceedsLimit
    [Fact(DisplayName = "HLS-032: Should_Throw_When_SingleLargeValueExceedsLimit")]
    public void HLS_032_LargeValueHeader_ExceedsLimit()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(100); // small limit

        var longValue = new string('x', 100); // 100 chars → "x" name + 100-char value + 32 = 133
        var block = LiteralNoIndex("x", longValue);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    // ── HLS-04x: SetMaxHeaderListSize argument validation ────────────────────

    /// RFC 7540 §6.5.2 — Should Throw When NegativeSizeProvided
    [Fact(DisplayName = "HLS-040: Should_Throw_When_NegativeSizeProvided")]
    public void HLS_040_NegativeSize_ThrowsHpackException()
    {
        var decoder = NewDecoder();

        var ex = Assert.Throws<HpackException>(() => decoder.SetMaxHeaderListSize(-1));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should Accept ZeroSizeLimit
    [Fact(DisplayName = "HLS-041: Should_Accept_ZeroSizeLimit")]
    public void HLS_041_ZeroSize_IsValidAndEnforced()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(0); // valid: no headers accepted

        var block = LiteralNoIndex("a", "b");
        Assert.Throws<HpackException>(() => decoder.Decode(block));
    }

    /// RFC 7540 §6.5.2 — Should Accept MaxIntSizeLimit AsUnlimited
    [Fact(DisplayName = "HLS-042: Should_Accept_MaxIntSizeLimit_AsUnlimited")]
    public void HLS_042_MaxIntSize_EffectivelyUnlimited()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(int.MaxValue); // explicit set to max

        var block = new List<byte>();
        for (var i = 0; i < 100; i++)
        {
            block.AddRange(LiteralNoIndex("header-name", "header-value"));
        }

        var headers = decoder.Decode(block.ToArray());
        Assert.Equal(100, headers.Count);
    }

    /// RFC 7540 §6.5.2 — Should RaiseLimit AllowingPreviouslyFailingDecodes
    [Fact(DisplayName = "HLS-043: Should_RaiseLimit_AllowingPreviouslyFailingDecodes")]
    public void HLS_043_RaiseLimit_PreviouslyFailingDecodesNowSucceed()
    {
        var decoder = NewDecoder();
        var oneSize = EntrySize("a", "b"); // 34

        // Tight limit: only one header allowed
        decoder.SetMaxHeaderListSize(oneSize);

        byte[] twoHeaders = [.. LiteralNoIndex("a", "b"), .. LiteralNoIndex("a", "b")];

        Assert.Throws<HpackException>(() => decoder.Decode(twoHeaders));

        // Raise limit: two headers now fit
        decoder.SetMaxHeaderListSize(oneSize * 2);
        var headers = decoder.Decode(twoHeaders);
        Assert.Equal(2, headers.Count);
    }

    // ── HLS-05x: Message quality and edge cases ───────────────────────────────

    /// RFC 7540 §6.5.2 — Should ThrowWithRfcReference InExceptionMessage
    [Fact(DisplayName = "HLS-050: Should_ThrowWithRfcReference_InExceptionMessage")]
    public void HLS_050_ExceptionMessage_ContainsRfcReference()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(0);

        var block = LiteralNoIndex("x", "y");
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));

        Assert.Contains("RFC 7540", ex.Message);
        Assert.Contains("6.5.2", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should ThrowWithCompressionError InExceptionMessage
    [Fact(DisplayName = "HLS-051: Should_ThrowWithCompressionError_InExceptionMessage")]
    public void HLS_051_ExceptionMessage_ContainsCompressionError()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(0);

        var block = LiteralNoIndex("x", "y");
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("COMPRESSION_ERROR", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should Handle EmptyHeaderBlock UnderAnyLimit
    [Fact(DisplayName = "HLS-052: Should_Handle_EmptyHeaderBlock_UnderAnyLimit")]
    public void HLS_052_EmptyBlock_AlwaysSucceeds()
    {
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(0); // even with zero limit

        // Empty block → nothing to count → no exception
        var headers = decoder.Decode([]);
        Assert.Empty(headers);
    }

    /// RFC 7540 §6.5.2 — Should CountStaticEntry UsingCorrectOctetSize
    [Fact(DisplayName = "HLS-053: Should_CountStaticEntry_UsingCorrectOctetSize")]
    public void HLS_053_StaticEntry_SizeCorrectlyCalculated()
    {
        // Index 2 = ":method" / "GET" → 7+3+32 = 42
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(EntrySize(":method", "GET")); // 42 → exactly fits

        var block = Indexed(2);
        var headers = decoder.Decode(block);

        Assert.Single(headers);
        Assert.Equal(":method", headers[0].Name);
        Assert.Equal("GET", headers[0].Value);
    }

    /// RFC 7540 §6.5.2 — Should Throw When StaticEntryExceedsExactLimit
    [Fact(DisplayName = "HLS-054: Should_Throw_When_StaticEntryExceedsExactLimit")]
    public void HLS_054_StaticEntry_ExceedsExactLimit_Throws()
    {
        // Index 2 = ":method" / "GET" → 42; limit 41 → throws
        var decoder = NewDecoder();
        decoder.SetMaxHeaderListSize(EntrySize(":method", "GET") - 1); // 41

        var block = Indexed(2);
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should Succeed When MixedRepresentationsUnderLimit
    [Fact(DisplayName = "HLS-055: Should_Succeed_When_MixedRepresentationsUnderLimit")]
    public void HLS_055_MixedRepresentations_UnderLimit_Succeeds()
    {
        var decoder = NewDecoder();

        // Three entries:
        //   Indexed static ":status 200" → 7+3+32 = 42
        //   Literal no-index "a"/"b"     → 1+1+32 = 34
        //   Literal never-index "c"/"d"  → 1+1+32 = 34
        // Total = 110
        decoder.SetMaxHeaderListSize(110);

        byte[] block = [.. Indexed(8), .. LiteralNoIndex("a", "b"), .. LiteralNeverIndex("c", "d")];

        var headers = decoder.Decode(block);
        Assert.Equal(3, headers.Count);
    }

    /// RFC 7541 §5.2 — SetMaxStringLength with a negative value must throw HpackException.
    [Fact(DisplayName = "HLS-057: SetMaxStringLength with negative value throws HpackException")]
    public void HLS_057_SetMaxStringLength_NegativeValue_ThrowsHpackException()
    {
        var decoder = NewDecoder();
        var ex = Assert.Throws<HpackException>(() => decoder.SetMaxStringLength(-1));
        Assert.Contains("Invalid max string length", ex.Message);
    }

    /// RFC 7540 §6.5.2 — Should Throw When MixedRepresentationsExceedLimit
    [Fact(DisplayName = "HLS-056: Should_Throw_When_MixedRepresentationsExceedLimit")]
    public void HLS_056_MixedRepresentations_ExceedLimit_Throws()
    {
        var decoder = NewDecoder();

        // Same three entries totaling 110 bytes; limit 109 → fails on third
        decoder.SetMaxHeaderListSize(109);

        byte[] block = [.. Indexed(8), .. LiteralNoIndex("a", "b"), .. LiteralNeverIndex("c", "d")];

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(block));
        Assert.Contains("MAX_HEADER_LIST_SIZE", ex.Message);
    }
}
