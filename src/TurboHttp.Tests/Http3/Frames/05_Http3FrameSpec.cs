using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Frames;

public sealed class Http3FrameSpec
{

    [Theory]
    [Trait("RFC", "RFC9114-7")]
    [InlineData(Http3FrameType.Data, 0x00)]
    [InlineData(Http3FrameType.Headers, 0x01)]
    [InlineData(Http3FrameType.CancelPush, 0x03)]
    [InlineData(Http3FrameType.Settings, 0x04)]
    [InlineData(Http3FrameType.PushPromise, 0x05)]
    [InlineData(Http3FrameType.GoAway, 0x06)]
    [InlineData(Http3FrameType.MaxPushId, 0x0d)]
    public void FrameType_values_match_rfc(Http3FrameType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void DataFrame_empty_payload()
    {
        var frame = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(Http3FrameType.Data, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x00 (1 byte) + Length=0x00 (1 byte) = 2 bytes
        Assert.Equal(2, bytes.Length);
        Assert.Equal(2, frame.SerializedSize);
        Assert.Equal(0x00, bytes[0]); // Type
        Assert.Equal(0x00, bytes[1]); // Length
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void DataFrame_with_payload()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var frame = new Http3DataFrame(payload);
        var bytes = frame.Serialize();
        // Type=0x00 (1b) + Length=0x04 (1b) + 4 bytes payload = 6
        Assert.Equal(6, bytes.Length);
        Assert.Equal(6, frame.SerializedSize);
        Assert.Equal(0x00, bytes[0]); // Type
        Assert.Equal(0x04, bytes[1]); // Length
        Assert.Equal(payload, bytes[2..]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void DataFrame_WriteTo_advances_span()
    {
        var frame = new Http3DataFrame(new byte[] { 0x01, 0x02 });
        var buf = new byte[frame.SerializedSize + 5];
        var span = buf.AsSpan();
        var written = frame.WriteTo(ref span);
        Assert.Equal(frame.SerializedSize, written);
        Assert.Equal(5, span.Length);
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void HeadersFrame_serializes()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 }; // sample QPACK block
        var frame = new Http3HeadersFrame(headerBlock);
        Assert.Equal(Http3FrameType.Headers, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x01 (1b) + Length=0x03 (1b) + 3 bytes = 5
        Assert.Equal(5, bytes.Length);
        Assert.Equal(5, frame.SerializedSize);
        Assert.Equal(0x01, bytes[0]); // Type
        Assert.Equal(0x03, bytes[1]); // Length
        Assert.Equal(headerBlock, bytes[2..]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void HeadersFrame_empty_block()
    {
        var frame = new Http3HeadersFrame(ReadOnlyMemory<byte>.Empty);
        var bytes = frame.Serialize();
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void CancelPushFrame_serializes()
    {
        var frame = new Http3CancelPushFrame(42);
        Assert.Equal(Http3FrameType.CancelPush, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x03 (1b) + Length=0x01 (1b) + pushId=42 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x03, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length = 1 (varint for 42 is 1 byte)
        Assert.Equal(42, bytes[2]);   // Push ID
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void CancelPushFrame_large_push_id()
    {
        var frame = new Http3CancelPushFrame(16383); // 2-byte varint
        var bytes = frame.Serialize();
        // Type=0x03 (1b) + Length=0x02 (1b, varint for 2) + pushId (2b) = 4
        Assert.Equal(4, bytes.Length);
        Assert.Equal(4, frame.SerializedSize);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void CancelPushFrame_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3CancelPushFrame(-1));
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void SettingsFrame_empty()
    {
        var frame = new Http3SettingsFrame(Array.Empty<(long, long)>());
        Assert.Equal(Http3FrameType.Settings, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x04 (1b) + Length=0x00 (1b) = 2
        Assert.Equal(2, bytes.Length);
        Assert.Equal(2, frame.SerializedSize);
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void SettingsFrame_with_parameters()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096),  // SETTINGS_MAX_FIELD_SECTION_SIZE = 4096
            (0x01, 100),   // SETTINGS_QPACK_MAX_TABLE_CAPACITY = 100
        };
        var frame = new Http3SettingsFrame(parameters);
        var bytes = frame.Serialize();

        // Verify type
        Assert.Equal(0x04, bytes[0]);

        // Roundtrip: decode the payload manually
        var span = bytes.AsSpan();
        QuicVarInt.TryDecode(span, out var frameType, out var consumed);
        Assert.Equal((long)Http3FrameType.Settings, frameType);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out var length, out consumed);
        span = span[consumed..];

        // First parameter: id=0x06, value=4096
        QuicVarInt.TryDecode(span, out var id1, out consumed);
        span = span[consumed..];
        Assert.Equal(0x06, id1);

        QuicVarInt.TryDecode(span, out var val1, out consumed);
        span = span[consumed..];
        Assert.Equal(4096, val1);

        // Second parameter: id=0x01, value=100
        QuicVarInt.TryDecode(span, out var id2, out consumed);
        span = span[consumed..];
        Assert.Equal(0x01, id2);

        QuicVarInt.TryDecode(span, out var val2, out consumed);
        span = span[consumed..];
        Assert.Equal(100, val2);

        // Should have consumed everything
        Assert.Equal(0, span.Length);

        // SerializedSize matches
        Assert.Equal(bytes.Length, frame.SerializedSize);
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void PushPromiseFrame_serializes()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB };
        var frame = new Http3PushPromiseFrame(7, headerBlock);
        Assert.Equal(Http3FrameType.PushPromise, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x05 (1b) + Length=0x03 (1b, varint for 3: 1b pushId + 2b header) + pushId=7 (1b) + header (2b) = 5
        Assert.Equal(5, bytes.Length);
        Assert.Equal(5, frame.SerializedSize);
        Assert.Equal(0x05, bytes[0]); // Type
        Assert.Equal(0x03, bytes[1]); // Length
        Assert.Equal(0x07, bytes[2]); // Push ID
        Assert.Equal(0xAA, bytes[3]);
        Assert.Equal(0xBB, bytes[4]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void PushPromiseFrame_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3PushPromiseFrame(-1, ReadOnlyMemory<byte>.Empty));
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void GoAwayFrame_serializes()
    {
        var frame = new Http3GoAwayFrame(0);
        Assert.Equal(Http3FrameType.GoAway, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x06 (1b) + Length=0x01 (1b) + streamId=0 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x06, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length
        Assert.Equal(0x00, bytes[2]); // Stream ID = 0
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void GoAwayFrame_large_stream_id()
    {
        var frame = new Http3GoAwayFrame(1_000_000);
        var bytes = frame.Serialize();

        // Decode and verify
        var span = bytes.AsSpan();
        QuicVarInt.TryDecode(span, out var frameType, out var consumed);
        Assert.Equal((long)Http3FrameType.GoAway, frameType);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out var length, out consumed);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out var streamId, out consumed);
        Assert.Equal(1_000_000, streamId);

        Assert.Equal(bytes.Length, frame.SerializedSize);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void GoAwayFrame_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3GoAwayFrame(-1));
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void MaxPushIdFrame_serializes()
    {
        var frame = new Http3MaxPushIdFrame(15);
        Assert.Equal(Http3FrameType.MaxPushId, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x0d (1b) + Length=0x01 (1b) + pushId=15 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x0d, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length
        Assert.Equal(15, bytes[2]);   // Push ID
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void MaxPushIdFrame_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3MaxPushIdFrame(-1));
    }


    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void SerializedSize_matches_actual_for_all_frames()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        Http3Frame[] frames =
        [
            new Http3DataFrame(payload),
            new Http3HeadersFrame(payload),
            new Http3CancelPushFrame(63),
            new Http3SettingsFrame(new List<(long, long)> { (0x06, 4096), (0x01, 0) }),
            new Http3PushPromiseFrame(10, payload),
            new Http3GoAwayFrame(100),
            new Http3MaxPushIdFrame(63),
        ];

        foreach (var frame in frames)
        {
            var bytes = frame.Serialize();
            Assert.Equal(frame.SerializedSize, bytes.Length);
        }
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void WriteTo_returns_SerializedSize()
    {
        var payload = new byte[] { 0xDE, 0xAD };
        Http3Frame[] frames =
        [
            new Http3DataFrame(payload),
            new Http3HeadersFrame(payload),
            new Http3CancelPushFrame(0),
            new Http3SettingsFrame(Array.Empty<(long, long)>()),
            new Http3PushPromiseFrame(0, payload),
            new Http3GoAwayFrame(0),
            new Http3MaxPushIdFrame(0),
        ];

        foreach (var frame in frames)
        {
            var buf = new byte[frame.SerializedSize];
            var span = buf.AsSpan();
            var written = frame.WriteTo(ref span);
            Assert.Equal(frame.SerializedSize, written);
            Assert.Equal(0, span.Length);
        }
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void DataFrame_large_payload_multi_byte_length()
    {
        // 100 bytes payload → length=100 uses 2-byte varint (64-16383 range)
        var payload = new byte[100];
        var frame = new Http3DataFrame(payload);
        var bytes = frame.Serialize();
        // Type=0x00 (1b) + Length=100 (2b, since 100 > 63) + payload (100b) = 103
        Assert.Equal(103, bytes.Length);
        Assert.Equal(103, frame.SerializedSize);

        // Verify we can decode the length prefix
        var span = bytes.AsSpan();
        QuicVarInt.TryDecode(span, out var type, out var consumed);
        Assert.Equal(0, type);
        span = span[consumed..];
        QuicVarInt.TryDecode(span, out var length, out consumed);
        Assert.Equal(100, length);
    }
}
