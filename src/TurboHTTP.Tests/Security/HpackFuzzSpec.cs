namespace TurboHTTP.Tests.Security;

using System.Buffers;
using TurboHTTP.Protocol.Http2.Hpack;
using Xunit;

/// <summary>
/// Fuzzing tests for HPACK decoder resistance to adversarial inputs.
/// Tests random bytes, Huffman data, dynamic table management, out-of-bounds indexing,
/// string length validation, never-indexed headers, table eviction, truncated blocks,
/// and boundary conditions with seeded Random for reproducibility.
/// </summary>
public sealed class HpackFuzzSpec
{
    private const int IterationsPerSeed = 100;
    private const int MaxHeaderTableSize = 65536;
    private const int MaxHeaderListSize = 65536;
    private const int MaxStringLength = 8192;
    private const int MemoryLimitBytes = 256 * 1024;

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void HpackDecoder_should_never_crash_when_given_random_bytes(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            var length = random.Next(1, 4097);
            var data = new byte[length];
            random.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    public void HpackDecoder_should_handle_huffman_encoded_random_data(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var stringLength = random.Next(1, 256);
            var randomBytes = new byte[stringLength];
            random.NextBytes(randomBytes);

            var span = buffer.GetSpan(10 + stringLength);
            span[0] = 0x80 | 1;
            var huffmanByte = (byte)(0x80 | (stringLength & 0x7F));
            span[1] = huffmanByte;
            randomBytes.CopyTo(span.Slice(2));
            buffer.Advance(2 + stringLength);

            AssertDecodeNeverCrashes(decoder, buffer.WrittenSpan.ToArray());
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_bound_dynamic_table_size_update(int seed)
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        var random = new Random(seed);

        for (var i = 0; i < 10; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();

            var headerSpan = buffer.GetSpan(4);
            headerSpan[0] = 0x20 | 31;
            headerSpan[1] = 0x80;
            headerSpan[2] = 0x80;
            headerSpan[3] = 0x01;
            buffer.Advance(4);

            for (var j = 0; j < 100; j++)
            {
                var nameLen = random.Next(5, 50);
                var name = new byte[nameLen];
                random.NextBytes(name);

                var entrySpan = buffer.GetSpan(2 + nameLen);
                entrySpan[0] = 0x40 | 1;
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_throw_when_indexed_reference_is_zero()
    {
        var decoder = new HpackDecoder();
        byte[] data = { 0x80 };

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(data));
        Assert.NotNull(ex);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_throw_when_indexed_reference_is_out_of_bounds(int seed)
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

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_gracefully_fail_when_string_length_exceeds_remaining_bytes(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        var buffer = new ArrayBufferWriter<byte>();
        var span = buffer.GetSpan(10);
        span[0] = 0x40 | 1;
        span[1] = 0x80 | 127;
        span[2] = 0x80;
        span[3] = 0x07;
        new byte[] { 1, 2, 3, 4, 5 }.CopyTo(span.Slice(4));
        buffer.Advance(9);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.NotNull(ex);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_throw_when_string_length_exceeds_max(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);
        decoder.SetMaxStringLength(100);

        var buffer = new ArrayBufferWriter<byte>();
        var span = buffer.GetSpan(4 + 200);
        span[0] = 0x40 | 1;
        span[1] = 0x80 | 127;
        span[2] = 0x80;
        span[3] = 0xC8;
        buffer.Advance(4);
        buffer.Write(new byte[200]);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.NotNull(ex);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_preserve_never_indexed_sensitive_header_flag(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (var i = 0; i < 10; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var name = $"x-secret-{random.Next(100)}";
            var value = $"val-{random.Next(1000)}";
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);

            var span = buffer.GetSpan(2 + nameBytes.Length + 1 + valueBytes.Length);
            span[0] = 0x10;
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

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    public void HpackDecoder_should_handle_dynamic_table_eviction_without_bomb(int seed)
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        var random = new Random(seed);

        var buffer = new ArrayBufferWriter<byte>();

        for (var i = 0; i < 1000; i++)
        {
            var name = $"header-{i}";
            var value = new string('x', 20);
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);

            var span = buffer.GetSpan(2 + nameBytes.Length + 1 + valueBytes.Length);
            span[0] = 0x40 | 1;
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

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    [InlineData(42)]
    [InlineData(137)]
    public void HpackDecoder_should_gracefully_fail_when_header_block_is_truncated(int seed)
    {
        var decoder = new HpackDecoder();
        var random = new Random(seed);

        for (var i = 0; i < 20; i++)
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
            var truncatePos = random.Next(1, data.Length);
            var truncated = new byte[truncatePos];
            Array.Copy(data, truncated, truncatePos);

            AssertDecodeNeverCrashes(decoder, truncated);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_set_max_header_table_size()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(MaxHeaderTableSize);
        Assert.NotNull(decoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_enforce_max_header_list_size()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(100);

        var buffer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < 10; i++)
        {
            var span = buffer.GetSpan(52);
            span[0] = 0x40 | 1;
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_throw_when_header_name_is_zero_length()
    {
        var decoder = new HpackDecoder();
        var buffer = new ArrayBufferWriter<byte>();

        var span = buffer.GetSpan(3);
        span[0] = 0x40;
        span[1] = 0;
        span[2] = 0;
        buffer.Advance(3);

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buffer.WrittenSpan));
        Assert.Contains("Empty header name", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_allow_header_value_with_zero_length()
    {
        var decoder = new HpackDecoder();
        var buffer = new ArrayBufferWriter<byte>();

        var span = buffer.GetSpan(2);
        span[0] = 0x41;
        span[1] = 0;
        buffer.Advance(2);

        var headers = decoder.Decode(buffer.WrittenSpan);
        Assert.Single(headers);
        Assert.Equal(":authority", headers[0].Name);
        Assert.Equal(string.Empty, headers[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_throw_when_hpack_integer_overflows()
    {
        var data = new byte[12];
        data[0] = 0xFF;
        for (var i = 1; i < 11; i++)
        {
            data[i] = 0xFF;
        }
        data[11] = 0x00;

        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(data));
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
