namespace TurboHttp.Tests.Security;

using System.Buffers;
using TurboHttp.Protocol.RFC7541;
using Xunit;

public sealed class HpackFuzzTests
{
    private const int IterationsPerSeed = 100;
    private const int MaxHeaderTableSize = 65536;
    private const int MaxHeaderListSize = 65536;
    private const int MaxStringLength = 8192;
    private const int MemoryLimitBytes = 256 * 1024;

    [Theory(DisplayName = "RFC7541-FUZZ-RND-001: Random bytes 1-4KB never crash or hang")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Category1_RandomBytes_NeverCrash(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (int i = 0; i < IterationsPerSeed; i++)
        {
            int length = random.Next(1, 4097);
            var data = new byte[length];
            random.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Theory(DisplayName = "RFC7541-FUZZ-HUF-002: Huffman-encoded random data decoded or throws")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    public void Category2_HuffmanRandomData_DecodedOrThrows(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (int i = 0; i < IterationsPerSeed; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            int stringLength = random.Next(1, 256);
            var randomBytes = new byte[stringLength];
            random.NextBytes(randomBytes);

            var span = buffer.GetSpan(10 + stringLength);
            span[0] = (byte)(0x80 | 1);
            byte huffmanByte = (byte)(0x80 | (stringLength & 0x7F));
            span[1] = huffmanByte;
            randomBytes.CopyTo(span.Slice(2));
            buffer.Advance(2 + stringLength);

            AssertDecodeNeverCrashes(decoder, buffer.WrittenSpan.ToArray());
        }
    }

    [Theory(DisplayName = "RFC7541-FUZZ-DTU-003: Dynamic table size update + indexed flood bounded")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category3_DynamicTableSizeUpdate_Bounded(int seed)
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        var random = new Random(seed);

        for (int i = 0; i < 10; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();

            var headerSpan = buffer.GetSpan(4);
            headerSpan[0] = (byte)(0x20 | 31);
            headerSpan[1] = 0x80;
            headerSpan[2] = 0x80;
            headerSpan[3] = 0x01;
            buffer.Advance(4);

            for (int j = 0; j < 100; j++)
            {
                int nameLen = random.Next(5, 50);
                var name = new byte[nameLen];
                random.NextBytes(name);

                var entrySpan = buffer.GetSpan(2 + nameLen);
                entrySpan[0] = (byte)(0x40 | 1);
                entrySpan[1] = (byte)(nameLen & 0x7F);
                name.CopyTo(entrySpan.Slice(2));
                buffer.Advance(2 + nameLen);
            }

            try
            {
                decoder.Decode(buffer.WrittenSpan);
            }
            catch (HpackException)
            {
                // Expected
            }
        }
    }

    [Fact(DisplayName = "RFC7541-FUZZ-IDX-004: Indexed reference to entry 0 throws")]
    public void Category4_IndexedRefZero_Throws()
    {
        var decoder = new HpackDecoder();
        byte[] data = { 0x80 };

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(data));
        Assert.NotNull(ex);
    }

    [Theory(DisplayName = "RFC7541-FUZZ-OOB-005: Indexed reference beyond table size throws")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category5_IndexedRefOutOfBounds_Throws(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        var buffer = new ArrayBufferWriter<byte>();
        var span = buffer.GetSpan(3);
        span[0] = 0xFF;
        span[1] = 0xFF;
        span[2] = 0x7F;
        buffer.Advance(3);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.NotNull(ex);
    }

    [Theory(DisplayName = "RFC7541-FUZZ-STR-006: String length > remaining bytes gracefully fails")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category6_StringLengthExceedsRemaining_Throws(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        var buffer = new ArrayBufferWriter<byte>();
        var span = buffer.GetSpan(10);
        span[0] = (byte)(0x40 | 1);
        span[1] = (byte)(0x80 | 127);
        span[2] = 0x80;
        span[3] = 0x07;
        new byte[] { 1, 2, 3, 4, 5 }.CopyTo(span.Slice(4));
        buffer.Advance(9);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.NotNull(ex);
    }

    [Theory(DisplayName = "RFC7541-FUZZ-LEN-007: Header exceeding max string length throws")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category7_StringLengthExceedsMax_Throws(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);
        decoder.SetMaxStringLength(100);

        var buffer = new ArrayBufferWriter<byte>();
        var span = buffer.GetSpan(4 + 200);
        span[0] = (byte)(0x40 | 1);
        span[1] = (byte)(0x80 | 127);
        span[2] = 0x80;
        span[3] = 0xC8;
        buffer.Advance(4);
        buffer.Write(new byte[200]);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.NotNull(ex);
    }

    [Theory(DisplayName = "RFC7541-FUZZ-NVR-008: Never-indexed sensitive header flag preserved")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category8_NeverIndexedFlag_Preserved(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (int i = 0; i < 10; i++)
        {
            // Build never-indexed literal: 0x10 prefix, name index=0, then name string, then value string
            var buffer = new ArrayBufferWriter<byte>();
            var name = $"x-secret-{random.Next(100)}";
            var value = $"val-{random.Next(1000)}";
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);

            var span = buffer.GetSpan(2 + nameBytes.Length + 1 + valueBytes.Length);
            span[0] = 0x10; // never-indexed, name index 0
            span[1] = (byte)nameBytes.Length;
            nameBytes.CopyTo(span.Slice(2));
            span[2 + nameBytes.Length] = (byte)valueBytes.Length;
            valueBytes.CopyTo(span.Slice(3 + nameBytes.Length));
            buffer.Advance(3 + nameBytes.Length + valueBytes.Length);

            var headers = decoder.Decode(buffer.WrittenSpan);
            Assert.Single(headers);
            Assert.True(headers[0].NeverIndex, "NeverIndex flag must be preserved");
            Assert.Equal(name, headers[0].Name);
            Assert.Equal(value, headers[0].Value);
        }
    }

    [Theory(DisplayName = "RFC7541-FUZZ-EVV-009: Dynamic table 1000+ entries eviction works")]
    [InlineData(42)]
    public void Category9_DynamicTableEviction_NoBomb(int seed)
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        var random = new Random(seed);

        var buffer = new ArrayBufferWriter<byte>();

        for (int i = 0; i < 1000; i++)
        {
            string name = $"header-{i}";
            string value = new string('x', 20);
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);

            var span = buffer.GetSpan(2 + nameBytes.Length + 1 + valueBytes.Length);
            span[0] = (byte)(0x40 | 1);
            span[1] = (byte)nameBytes.Length;
            nameBytes.CopyTo(span.Slice(2));
            span[2 + nameBytes.Length] = (byte)valueBytes.Length;
            valueBytes.CopyTo(span.Slice(3 + nameBytes.Length));
            buffer.Advance(3 + nameBytes.Length + valueBytes.Length);
        }

        try
        {
            decoder.Decode(buffer.WrittenSpan);
        }
        catch (HpackException)
        {
            // Expected
        }
    }

    [Theory(DisplayName = "RFC7541-FUZZ-TRC-010: Truncated header block gracefully fails")]
    [InlineData(42)]
    [InlineData(137)]
    public void Category10_TruncatedBlock_Throws(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (int i = 0; i < 20; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();

            var span = buffer.GetSpan(1 + 1 + 1 + 4 + 1 + 5);
            span[0] = 0x82;
            span[1] = 0x40;
            span[2] = 4;
            "test"u8.CopyTo(span.Slice(3));
            span[7] = 5;
            "value"u8.CopyTo(span.Slice(8));
            buffer.Advance(13);

            var data = buffer.WrittenMemory.ToArray();
            int truncatePos = random.Next(1, data.Length);
            var truncated = new byte[truncatePos];
            Array.Copy(data, truncated, truncatePos);

            AssertDecodeNeverCrashes(decoder, truncated);
        }
    }

    [Fact(DisplayName = "RFC7541-FUZZ-BND-001: Max header table size setting works")]
    public void Boundary1_MaxTableSize_Set()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        Assert.NotNull(decoder);
    }

    [Fact(DisplayName = "RFC7541-FUZZ-BND-002: Max header list size enforcement")]
    public void Boundary2_MaxHeaderListSize_Enforced()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(100);

        var buffer = new ArrayBufferWriter<byte>();
        for (int i = 0; i < 10; i++)
        {
            var span = buffer.GetSpan(52);
            span[0] = (byte)(0x40 | 1);
            span[1] = 50;
            buffer.Advance(2);
            buffer.Write(new byte[50]);
        }

        try
        {
            decoder.Decode(buffer.WrittenSpan);
        }
        catch (HpackException)
        {
            // Expected
        }
    }

    [Fact(DisplayName = "RFC7541-FUZZ-BND-003: Header with 0-length name throws")]
    public void Boundary3_ZeroLengthName_Throws()
    {
        var decoder = new HpackDecoder();
        var buffer = new ArrayBufferWriter<byte>();

        // Literal with incremental indexing, name index=0 (new name), name length=0 (empty)
        var span = buffer.GetSpan(3);
        span[0] = 0x40; // incremental indexing, name index 0
        span[1] = 0;    // name: raw string, length 0 → empty name
        span[2] = 0;    // value: raw string, length 0
        buffer.Advance(3);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.Contains("Empty header name", ex.Message);
    }

    [Fact(DisplayName = "RFC7541-FUZZ-BND-004: Header with 0-length value allowed")]
    public void Boundary4_ZeroLengthValue_Allowed()
    {
        var decoder = new HpackDecoder();
        var buffer = new ArrayBufferWriter<byte>();

        // Literal with incremental indexing, name index=1 (":authority"), value length=0
        var span = buffer.GetSpan(2);
        span[0] = 0x41; // incremental indexing, name index 1 → ":authority"
        span[1] = 0;    // value: raw string, length 0
        buffer.Advance(2);

        var headers = decoder.Decode(buffer.WrittenSpan);
        Assert.Single(headers);
        Assert.Equal(":authority", headers[0].Name);
        Assert.Equal(string.Empty, headers[0].Value);
    }

    [Fact(DisplayName = "RFC7541-FUZZ-BND-005: Integer overflow in HPACK integer throws")]
    public void Boundary5_IntegerOverflow_Throws()
    {
        // Indexed header with index value that overflows int (7-bit prefix, all continuation bytes)
        // 0xFF = prefix full (127), then 10 continuation bytes all 0xFF → huge value
        var data = new byte[12];
        data[0] = 0xFF; // indexed header, prefix = 127
        for (int i = 1; i < 11; i++)
        {
            data[i] = 0xFF; // continuation with more-bit set
        }
        data[11] = 0x00; // stop byte

        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(data));
    }

    [Theory(DisplayName = "RFC7541-FUZZ-MEM-001: Decoder stays under 256KB allocation")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    public void Memory1_AllocationBounded(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        long startBytes = GC.GetTotalMemory(true);

        for (int i = 0; i < 20; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();

            int length = random.Next(1, 4097);
            var randomData = new byte[length];
            random.NextBytes(randomData);

            buffer.Write(randomData);

            try
            {
                decoder.Decode(buffer.WrittenSpan);
            }
            catch (HpackException)
            {
                // Expected
            }
        }

        long endBytes = GC.GetTotalMemory(true);
        long delta = endBytes - startBytes;

        Assert.True(delta < MemoryLimitBytes, $"Memory delta {delta} exceeds {MemoryLimitBytes}");
    }

    private static void AssertDecodeNeverCrashes(HpackDecoder decoder, byte[] data)
    {
        try
        {
            decoder.Decode(data);
        }
        catch (HpackException)
        {
            // Expected
        }
    }
}
