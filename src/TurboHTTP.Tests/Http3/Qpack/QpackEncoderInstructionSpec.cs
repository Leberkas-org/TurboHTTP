using System.Text;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackEncoderInstructionSpec
{
    private readonly byte[] _buffer = new byte[1024];

    private Span<byte> CreateSpan() => _buffer.AsSpan();

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.1")]
    [InlineData(0, new byte[] { 0x20 })] // 001_00000 → capacity=0
    [InlineData(30, new byte[] { 0x3E })] // 001_11110 → capacity=30
    [InlineData(31, new byte[] { 0x3F, 0x00 })] // 001_11111 + 0x00 → capacity=31
    [InlineData(4096, new byte[] { 0x3F, 0xE1, 0x1F })] // 001_11111 + multi-byte
    public void Should_EncodeSetDynamicTableCapacity(int capacity, byte[] expected)
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(capacity, ref span);

        Assert.Equal(expected, _buffer[..written]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.1")]
    public void Should_ThrowForNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper);
        return;

        static void ThrowHelper()
        {
            Span<byte> span = new byte[1024];
            QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(-1, ref span);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.2")]
    public void Should_EncodeInsertWithNameReference_Static()
    {
        // Static table index 1 = :path, value = "/index.html"
        // First byte: 1(T=1)xxxxxx → 0xC0 | 1 = 0xC1
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(1, true, "/index.html", ref span);

        var data = _buffer.AsSpan(0, written);
        Assert.True(data.Length >= 2);

        // First byte: 1 1 000001 = 0xC1 (static ref, index 1)
        Assert.Equal(0xC1, data[0]);

        // Decode the value string to verify round-trip
        var pos = 1;
        var valueBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 7);
        Assert.Equal("/index.html", Encoding.UTF8.GetString(valueBytes));
        Assert.Equal(written, pos);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.2")]
    public void Should_EncodeInsertWithNameReference_Dynamic()
    {
        // Dynamic table index 0, value = "bar"
        // First byte: 1(T=0)xxxxxx → 0x80 | 0 = 0x80
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(0, false, "bar", ref span);

        var data = _buffer.AsSpan(0, written);
        Assert.True(data.Length >= 2);

        // First byte: 1 0 000000 = 0x80 (dynamic ref, index 0)
        Assert.Equal(0x80, data[0]);

        var pos = 1;
        var valueBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 7);
        Assert.Equal("bar", Encoding.UTF8.GetString(valueBytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.2")]
    public void Should_ThrowForNegativeNameIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper);
        return;

        static void ThrowHelper()
        {
            Span<byte> span = new byte[1024];
            QpackEncoderInstructionWriter.WriteInsertWithNameReference(-1, true, "val"u8, ref span);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.3")]
    public void Should_EncodeInsertWithLiteralName()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-custom", "hello", ref span);

        var data = _buffer.AsSpan(0, written);
        Assert.True(data.Length >= 4);

        // First byte high bits: 01 → 0x40 mask
        Assert.Equal(0x40, data[0] & 0xC0);

        // Decode name
        var pos = 0;
        var nameBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 5);
        Assert.Equal("x-custom", Encoding.UTF8.GetString(nameBytes));

        // Decode value
        var valueBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 7);
        Assert.Equal("hello", Encoding.UTF8.GetString(valueBytes));
        Assert.Equal(written, pos);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.3")]
    public void Should_EncodeInsertWithLiteralName_EmptyValue()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-empty", "", ref span);

        // Decode name
        var pos = 0;
        var nameBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 5);
        Assert.Equal("x-empty", Encoding.UTF8.GetString(nameBytes));

        // Decode value — should be empty
        var valueBytes = QpackStringCodec.Decode(_buffer[..written], ref pos, 7);
        Assert.Empty(valueBytes);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.4")]
    [InlineData(0, new byte[] { 0x00 })] // 000_00000 → index=0
    [InlineData(7, new byte[] { 0x07 })] // 000_00111 → index=7
    [InlineData(31, new byte[] { 0x1F, 0x00 })] // 000_11111 + 0x00 → index=31
    [InlineData(100, new byte[] { 0x1F, 0x45 })] // 000_11111 + 69
    public void Should_EncodeDuplicate(int index, byte[] expected)
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteDuplicate(index, ref span);

        Assert.Equal(expected, _buffer[..written]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.4")]
    public void Should_ThrowForNegativeDuplicateIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper);
        return;

        static void ThrowHelper()
        {
            Span<byte> span = new byte[1024];
            QpackEncoderInstructionWriter.WriteDuplicate(-1, ref span);
        }
    }
}