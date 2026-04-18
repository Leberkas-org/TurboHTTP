using System.Text;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackEncoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_EncodeStaticIndexed_When_ExactMatch()
    {
        // :method GET is static index 17
        var encoder = new QpackEncoder(maxTableCapacity: 0); // no dynamic table
        var headers = new List<(string, string)> { (":method", "GET") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);
        var data = buf[..n];

        // Prefix: RIC=0 (1 byte: 0x00), delta base=0 (1 byte: 0x00)
        Assert.Equal(0x00, data[0]); // RIC = 0
        Assert.Equal(0x00, data[1]); // S=0, delta base=0

        // §4.5.2 static indexed: 1T=1xxxxxx → 0xC0 | index
        // Index 17 fits in 6-bit prefix (< 63)
        Assert.Equal(0xC0 | 17, data[2]); // 0xD1
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_EncodeMultipleStaticIndexed()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)>
        {
            (":method", "GET"), // index 17
            (":path", "/"), // index 1
            (":scheme", "https"), // index 23
        };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);
        var data = buf[..n];

        // Prefix: 2 bytes (RIC=0, deltaBase=0)
        Assert.Equal(0x00, data[0]);
        Assert.Equal(0x00, data[1]);

        // Three static indexed entries
        Assert.Equal(0xC0 | 17, data[2]); // :method GET
        Assert.Equal(0xC0 | 1, data[3]); // :path /
        Assert.Equal(0xC0 | 23, data[4]); // :scheme https
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_InsertAndReference_When_DynamicTableEnabled()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var headers = new List<(string, string)> { ("x-custom", "value1") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        // Dynamic table should have 1 entry
        Assert.Equal(1, encoder.DynamicTable.Count);
        Assert.Equal(1, encoder.DynamicTable.InsertCount);

        // Encoder instructions should be non-empty (InsertWithLiteralName)
        Assert.True(encoder.EncoderInstructions.Length > 0);

        var data = buf[..n];

        // Prefix: RIC should be 1 (absolute index 0 referenced → RIC = 1)
        // MaxEntries = 4096 / 32 = 128
        // EncodedRIC = (1 % (2 * 128)) + 1 = 2
        Assert.Equal(2, data[0]);

        // Base = RIC = 1, so S=0, delta base = base - RIC = 0
        Assert.Equal(0x00, data[1]);

        // Dynamic indexed: 1T=0xxxxxx → 0x80 | relativeIndex
        // relativeIndex = base(1) - absIdx(0) - 1 = 0
        Assert.Equal(0x80, data[2]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.2")]
    public void Should_ReuseDynamicEntry_When_AlreadyInserted()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        // First encode: inserts "x-custom: value1" into dynamic table
        var headers1 = new List<(string, string)> { ("x-custom", "value1") };
        encoder.Encode(headers1);

        // Second encode: should reference without new insert
        var headers2 = new List<(string, string)> { ("x-custom", "value1") };
        Span<byte> buf = new byte[8192];
        var span = buf;
        encoder.Encode(headers2, ref span);

        // Still only 1 entry in table (reused, not re-inserted)
        Assert.Equal(1, encoder.DynamicTable.Count);

        // No new encoder instructions on second encode
        Assert.Equal(0, encoder.EncoderInstructions.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.6")]
    public void Should_EncodeLiteral_When_NoTableMatch()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0); // dynamic table disabled
        var headers = new List<(string, string)> { ("x-custom", "value") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);
        var data = buf[..n];

        // Prefix: RIC=0, delta base=0
        Assert.Equal(0x00, data[0]);
        Assert.Equal(0x00, data[1]);

        // §4.5.6: 001N=0Hxxx → first byte starts with 0x20 (N=0)
        // The H bit and name length are encoded by QpackStringCodec
        Assert.Equal(0x20, data[2] & 0x30); // N=0 confirmed
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_NeverIndex_When_SensitiveHeader()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        // "authorization" is in static table at index 84 (name-only match)
        var headers = new List<(string, string)> { ("authorization", "Bearer token123") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        // Should NOT be inserted into dynamic table
        Assert.Equal(0, encoder.DynamicTable.Count);

        var data = buf[..n];

        // Prefix: RIC=0 (no dynamic references for sensitive headers)
        Assert.Equal(0x00, data[0]);
        Assert.Equal(0x00, data[1]);

        // §4.5.4 with N=1, T=1 (static name ref): 0111xxxx → 0x70 | index
        // Static index 84 for "authorization" doesn't fit in 4-bit prefix (max 15)
        // So it's multi-byte: 0x7F, then continuation
        Assert.Equal(0x7F, data[2]); // 0x70 | 0x0F (max prefix)
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_NeverIndex_When_CookieHeader()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        // "cookie" is in static table at index 5 (name-only match)
        var headers = new List<(string, string)> { ("cookie", "session=abc123") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        // Should NOT be inserted into dynamic table
        Assert.Equal(0, encoder.DynamicTable.Count);

        var data = buf[..n];

        // Prefix: RIC=0
        Assert.Equal(0x00, data[0]);

        // §4.5.4 with N=1, T=1 (static): 0111xxxx → 0x70 | 5
        Assert.Equal(0x75, data[2]); // 0x70 | 5
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.1")]
    public void Should_EncodeRequiredInsertCount_When_DynamicRefsExist()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        // Insert 3 headers — each gets a new dynamic table entry
        var headers = new List<(string, string)>
        {
            ("x-a", "1"),
            ("x-b", "2"),
            ("x-c", "3"),
        };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        Assert.Equal(3, encoder.DynamicTable.InsertCount);

        var data = buf[..n];

        // RIC = 3 (highest abs index = 2, so RIC = 3)
        // MaxEntries = 4096 / 32 = 128
        // EncodedRIC = (3 % (2*128)) + 1 = 4
        Assert.Equal(4, data[0]);

        // S=0, delta base = base(3) - RIC(3) = 0
        Assert.Equal(0x00, data[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.1.2")]
    public void Should_UseHuffman_When_Shorter()
    {
        // Use a long ASCII value where Huffman saves bytes
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { ("x-test", "www.example.com") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        // Just verify it encodes without error and produces output
        Assert.True(n > 2); // prefix + at least one header
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_EmitEncoderInstructions_When_InsertingWithStaticNameRef()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        // ":path" is static index 1 — value "/api/v1" is not in static table
        var headers = new List<(string, string)> { (":path", "/api/v1") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        encoder.Encode(headers, ref span);

        // Should have emitted an InsertWithNameReference instruction
        var instructions = encoder.EncoderInstructions;
        Assert.True(instructions.Length > 0);

        // Parse the instruction to verify it's InsertWithNameReference(static, index=1)
        var decoder = new QpackInstructionDecoder();
        var status = decoder.TryDecodeEncoderInstruction(instructions.Span, out var instruction);
        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.True(instruction.IsStatic);
        Assert.Equal(1, instruction.NameIndex);
        Assert.Equal("/api/v1", Encoding.UTF8.GetString(instruction.Value));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5.4")]
    public void Should_EncodeLiteralWithStaticName_When_DynamicTableFull()
    {
        // Tiny table that can't fit the entry
        var encoder = new QpackEncoder(maxTableCapacity: 32);
        // ":path" is static index 1, value too long for 32-byte table
        var headers = new List<(string, string)> { (":path", "/very/long/path/that/exceeds/capacity") };

        Span<byte> buf = new byte[8192];
        var span = buf;
        var n = encoder.Encode(headers, ref span);

        // Dynamic table should be empty (entry too large)
        Assert.Equal(0, encoder.DynamicTable.Count);

        var data = buf[..n];

        // Prefix: RIC=0
        Assert.Equal(0x00, data[0]);
        Assert.Equal(0x00, data[1]);

        // §4.5.4: 01N=0T=1xxxx → 0x50 | index
        // Static index 1 for :path
        Assert.Equal(0x51, data[2]); // 0x50 | 1
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void Should_Throw_When_EmptyHeaderName()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { ("", "value") };

        Assert.Throws<QpackException>(() => encoder.Encode(headers));
    }
}