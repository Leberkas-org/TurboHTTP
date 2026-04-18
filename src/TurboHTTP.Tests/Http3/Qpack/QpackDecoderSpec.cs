using System.Buffers;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_DecodeStaticIndexed_When_StaticTableMatch()
    {
        // Encode :method GET using encoder, then decode
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_DecodeMultipleStaticIndexed()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(3, decoded.Count);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
        Assert.Equal(":path", decoded[1].Name);
        Assert.Equal("/", decoded[1].Value);
        Assert.Equal(":scheme", decoded[2].Name);
        Assert.Equal("https", decoded[2].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_DecodeDynamicIndexed_When_DynamicTablePopulated()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var encoded = encoder.Encode(headers);

        // Decoder must have the same dynamic table state
        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        decoder.DynamicTable.Insert("x-custom", "value1");

        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-custom", decoded[0].Name);
        Assert.Equal("value1", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.4")]
    public void Should_DecodeLiteralWithStaticNameRef()
    {
        // Use a tiny table so the entry can't be inserted → falls back to literal with static name ref
        var encoder = new QpackEncoder(maxTableCapacity: 32);
        var headers = new List<(string, string)> { (":path", "/very/long/path/that/exceeds/capacity") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 32);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(":path", decoded[0].Name);
        Assert.Equal("/very/long/path/that/exceeds/capacity", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.6")]
    public void Should_DecodeLiteralWithoutNameRef()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { ("x-custom", "my-value") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-custom", decoded[0].Name);
        Assert.Equal("my-value", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_DecodeSensitiveHeader_When_NeverIndexed()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("authorization", "Bearer token123") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("authorization", decoded[0].Name);
        Assert.Equal("Bearer token123", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_DecodeHuffman_When_StringIsHuffmanEncoded()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { ("x-test", "www.example.com") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-test", decoded[0].Name);
        Assert.Equal("www.example.com", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.1")]
    public void Should_Throw_When_RequiredInsertCountExceedsKnown()
    {
        // Create an encoded block that references dynamic table (RIC > 0)
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var encoded = encoder.Encode(headers);

        // Decoder has empty dynamic table (InsertCount = 0) but encoded block has RIC = 1
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        Assert.Throws<QpackException>(() => decoder.Decode(encoded.Span));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_ReturnBlocked_When_RicExceedsKnownInsertCount()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 10);

        var result = decoder.TryDecode(encoded.Span, streamId: 4);

        Assert.True(result.IsBlocked);
        Assert.Equal(1, result.RequiredInsertCount);
        Assert.Null(result.Headers);
        Assert.Equal(1, decoder.BlockedStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_Throw_When_BlockedStreamLimitReached()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var encoded = encoder.Encode(headers);

        // Only allow 1 blocked stream
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 1);

        // First blocked stream is OK
        var result1 = decoder.TryDecode(encoded.Span, streamId: 4);
        Assert.True(result1.IsBlocked);

        // Second blocked stream exceeds limit
        Assert.Throws<QpackException>(() => decoder.TryDecode(encoded.Span, streamId: 8));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_EmitSectionAcknowledgment_When_DynamicTableReferenced()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        decoder.DynamicTable.Insert("x-custom", "value1");

        decoder.Decode(encoded.Span, streamId: 4);

        // Should have emitted a Section Acknowledgment instruction
        var instructions = decoder.DecoderInstructions;
        Assert.True(instructions.Length > 0);

        // Parse it: should be Section Acknowledgment with stream ID 4
        var instrDecoder = new QpackInstructionDecoder();
        var status = instrDecoder.TryDecodeDecoderInstruction(instructions.Span, out var instruction);
        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instruction.Type);
        Assert.Equal(4, instruction.IntValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_EmitNoInstructions_When_StaticOnly()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        decoder.Decode(encoded.Span);

        Assert.Equal(0, decoder.DecoderInstructions.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.3")]
    public void Should_DecodePostBaseIndexed()
    {
        // Manually construct a header block with post-base indexed field.
        // Prefix: RIC=1, S=1 (base < RIC), deltaBase=0 → base = RIC - 0 - 1 = 0
        // Post-base indexed: 0001xxxx with post-base index 0 → absolute index = base(0) + 0 = 0
        var buf = new ArrayBufferWriter<byte>();

        // RIC: MaxEntries = 4096/32 = 128, EncodedRIC = (1 % 256) + 1 = 2
        WriteInt(2, 8, 0x00, buf); // Encoded RIC = 2
        WriteInt(0, 7, 0x80, buf); // S=1, deltaBase=0 → base = 0

        // Post-base indexed: 0001xxxx, 4-bit prefix, postBaseIndex=0
        WriteInt(0, 4, 0x10, buf);

        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        decoder.DynamicTable.Insert("x-post-base", "pb-value");

        var decoded = decoder.Decode(buf.WrittenSpan, streamId: 1);

        Assert.Single(decoded);
        Assert.Equal("x-post-base", decoded[0].Name);
        Assert.Equal("pb-value", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.5")]
    public void Should_DecodeLiteralWithPostBaseNameRef()
    {
        // Prefix: RIC=1, S=1, deltaBase=0 → base = 0
        // Literal with post-base name ref: 0000Nxxx, 3-bit prefix, postBaseIndex=0
        // → absolute name index = base(0) + 0 = 0
        var buf = new ArrayBufferWriter<byte>();

        WriteInt(2, 8, 0x00, buf); // Encoded RIC = 2
        WriteInt(0, 7, 0x80, buf); // S=1, deltaBase=0 → base = 0

        // Literal with post-base name ref: 0000N=0 xxx, 3-bit prefix, postBaseIndex=0
        WriteInt(0, 3, 0x00, buf);

        // Value string: "new-value" (plain, 7-bit prefix)
        var valueBytes = "new-value"u8.ToArray();
        WriteStr(valueBytes.AsSpan(), 7, 0x00, false, buf);

        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        decoder.DynamicTable.Insert("x-post-name", "original");

        var decoded = decoder.Decode(buf.WrittenSpan, streamId: 1);

        Assert.Single(decoded);
        Assert.Equal("x-post-name", decoded[0].Name);
        Assert.Equal("new-value", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_DecodeEmptyHeaderBlock()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)>();
        var encoded = encoder.Encode(headers);

        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Empty(decoded);
    }

    private static void WriteInt(int value, int prefixBits, byte prefixFlags, ArrayBufferWriter<byte> buf)
    {
        var tmp = new byte[16];
        var span = tmp.AsSpan();
        var n = QpackIntegerCodec.Encode(value, prefixBits, prefixFlags, ref span);
        buf.Write(tmp.AsSpan(0, n));
    }

    private static void WriteStr(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, bool useHuffman,
        ArrayBufferWriter<byte> buf)
    {
        var maxLen = 16 + HuffmanCodec.GetMaxEncodedLength(Math.Max(value.Length, 1));
        var tmp = new byte[maxLen];
        var span = tmp.AsSpan();
        var n = QpackStringCodec.Encode(value, prefixBits, prefixFlags, useHuffman, ref span);
        buf.Write(tmp.AsSpan(0, n));
    }
}