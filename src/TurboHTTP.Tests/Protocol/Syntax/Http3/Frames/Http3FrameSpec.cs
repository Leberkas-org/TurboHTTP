using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Frames;

public sealed class Http3FrameSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    [InlineData(FrameType.Data, 0x00)]
    [InlineData(FrameType.Headers, 0x01)]
    [InlineData(FrameType.CancelPush, 0x03)]
    [InlineData(FrameType.Settings, 0x04)]
    [InlineData(FrameType.PushPromise, 0x05)]
    [InlineData(FrameType.GoAway, 0x06)]
    [InlineData(FrameType.MaxPushId, 0x0d)]
    internal void FrameType_should_match_rfc_values(FrameType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Http3DataFrame_should_have_empty_payload()
    {
        var frame = new DataFrame(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(FrameType.Data, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x00 (1 byte) + Length=0x00 (1 byte) = 2 bytes
        Assert.Equal(2, bytes.Length);
        Assert.Equal(2, frame.SerializedSize);
        Assert.Equal(0x00, bytes[0]); // Type
        Assert.Equal(0x00, bytes[1]); // Length
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Http3DataFrame_should_serialize_with_payload()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var frame = new DataFrame(payload);
        var bytes = frame.Serialize();
        // Type=0x00 (1b) + Length=0x04 (1b) + 4 bytes payload = 6
        Assert.Equal(6, bytes.Length);
        Assert.Equal(6, frame.SerializedSize);
        Assert.Equal(0x00, bytes[0]); // Type
        Assert.Equal(0x04, bytes[1]); // Length
        Assert.Equal(payload, bytes[2..]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Http3DataFrame_should_advance_span_in_writeto()
    {
        var frame = new DataFrame(new byte[] { 0x01, 0x02 });
        var buf = new byte[frame.SerializedSize + 5];
        var span = buf.AsSpan();
        var written = frame.WriteTo(ref span);
        Assert.Equal(frame.SerializedSize, written);
        Assert.Equal(5, span.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void Http3HeadersFrame_should_serialize()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 }; // sample QPACK block
        var frame = new HeadersFrame(headerBlock);
        Assert.Equal(FrameType.Headers, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x01 (1b) + Length=0x03 (1b) + 3 bytes = 5
        Assert.Equal(5, bytes.Length);
        Assert.Equal(5, frame.SerializedSize);
        Assert.Equal(0x01, bytes[0]); // Type
        Assert.Equal(0x03, bytes[1]); // Length
        Assert.Equal(headerBlock, bytes[2..]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void Http3HeadersFrame_should_handle_empty_block()
    {
        var frame = new HeadersFrame(ReadOnlyMemory<byte>.Empty);
        var bytes = frame.Serialize();
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void Http3CancelPushFrame_should_serialize()
    {
        var frame = new CancelPushFrame(42);
        Assert.Equal(FrameType.CancelPush, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x03 (1b) + Length=0x01 (1b) + pushId=42 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x03, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length = 1 (varint for 42 is 1 byte)
        Assert.Equal(42, bytes[2]); // Push ID
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void Http3CancelPushFrame_should_handle_large_push_id()
    {
        var frame = new CancelPushFrame(16383); // 2-byte varint
        var bytes = frame.Serialize();
        // Type=0x03 (1b) + Length=0x02 (1b, varint for 2) + pushId (2b) = 4
        Assert.Equal(4, bytes.Length);
        Assert.Equal(4, frame.SerializedSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public void Http3CancelPushFrame_should_reject_negative_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CancelPushFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Http3SettingsFrame_should_serialize_empty()
    {
        var frame = new SettingsFrame([]);
        Assert.Equal(FrameType.Settings, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x04 (1b) + Length=0x00 (1b) = 2
        Assert.Equal(2, bytes.Length);
        Assert.Equal(2, frame.SerializedSize);
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Http3SettingsFrame_should_serialize_with_parameters()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096), // SETTINGS_MAX_FIELD_SECTION_SIZE = 4096
            (0x01, 100), // SETTINGS_QPACK_MAX_TABLE_CAPACITY = 100
        };
        var frame = new SettingsFrame(parameters);
        var bytes = frame.Serialize();

        // Verify type
        Assert.Equal(0x04, bytes[0]);

        // Roundtrip: decode the payload manually
        var span = bytes.AsSpan();
        QuicVarInt.TryDecode(span, out var frameType, out var consumed);
        Assert.Equal((long)FrameType.Settings, frameType);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out _, out consumed);
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void Http3PushPromiseFrame_should_serialize()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB };
        var frame = new PushPromiseFrame(7, headerBlock);
        Assert.Equal(FrameType.PushPromise, frame.Type);
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public void Http3PushPromiseFrame_should_reject_negative_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PushPromiseFrame(-1, ReadOnlyMemory<byte>.Empty));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void Http3GoAwayFrame_should_serialize()
    {
        var frame = new GoAwayFrame(0);
        Assert.Equal(FrameType.GoAway, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x06 (1b) + Length=0x01 (1b) + streamId=0 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x06, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length
        Assert.Equal(0x00, bytes[2]); // Stream ID = 0
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void Http3GoAwayFrame_should_handle_large_stream_id()
    {
        var frame = new GoAwayFrame(1_000_000);
        var bytes = frame.Serialize();

        // Decode and verify
        var span = bytes.AsSpan();
        QuicVarInt.TryDecode(span, out var frameType, out var consumed);
        Assert.Equal((long)FrameType.GoAway, frameType);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out _, out consumed);
        span = span[consumed..];

        QuicVarInt.TryDecode(span, out var streamId, out consumed);
        Assert.Equal(1_000_000, streamId);

        Assert.Equal(bytes.Length, frame.SerializedSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public void Http3GoAwayFrame_should_reject_negative_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GoAwayFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void Http3MaxPushIdFrame_should_serialize()
    {
        var frame = new MaxPushIdFrame(15);
        Assert.Equal(FrameType.MaxPushId, frame.Type);
        var bytes = frame.Serialize();
        // Type=0x0d (1b) + Length=0x01 (1b) + pushId=15 (1b) = 3
        Assert.Equal(3, bytes.Length);
        Assert.Equal(3, frame.SerializedSize);
        Assert.Equal(0x0d, bytes[0]); // Type
        Assert.Equal(0x01, bytes[1]); // Length
        Assert.Equal(15, bytes[2]); // Push ID
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public void Http3MaxPushIdFrame_should_reject_negative_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxPushIdFrame(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void Http3Frame_should_have_serialized_size_matching_actual_for_all_frame_types()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        Http3Frame[] frames =
        [
            new DataFrame(payload),
            new HeadersFrame(payload),
            new CancelPushFrame(63),
            new SettingsFrame(new List<(long, long)> { (0x06, 4096), (0x01, 0) }),
            new PushPromiseFrame(10, payload),
            new GoAwayFrame(100),
            new MaxPushIdFrame(63),
        ];

        foreach (var frame in frames)
        {
            var bytes = frame.Serialize();
            Assert.Equal(frame.SerializedSize, bytes.Length);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void Http3Frame_should_return_serialized_size_in_writeto()
    {
        var payload = "\u07ad"u8.ToArray();
        Http3Frame[] frames =
        [
            new DataFrame(payload),
            new HeadersFrame(payload),
            new CancelPushFrame(0),
            new SettingsFrame([]),
            new PushPromiseFrame(0, payload),
            new GoAwayFrame(0),
            new MaxPushIdFrame(0),
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Http3DataFrame_should_encode_large_payload_with_multi_byte_length()
    {
        // 100 bytes payload → length=100 uses 2-byte varint (64-16383 range)
        var payload = new byte[100];
        var frame = new DataFrame(payload);
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