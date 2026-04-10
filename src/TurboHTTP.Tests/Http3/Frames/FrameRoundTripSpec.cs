using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class FrameRoundTripSpec
{

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Data_frame_roundtrip()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0xDE, 0xAD };
        var original = new Http3DataFrame(payload);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3DataFrame>(decoded);
        Assert.Equal(Http3FrameType.Data, result.Type);
        Assert.Equal(payload, result.Data.ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Headers_frame_roundtrip()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x87, 0x44, 0x88, 0x62, 0xA1 };
        var original = new Http3HeadersFrame(headerBlock);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3HeadersFrame>(decoded);
        Assert.Equal(Http3FrameType.Headers, result.Type);
        Assert.Equal(headerBlock, result.HeaderBlock.ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void CancelPush_frame_roundtrip()
    {
        var original = new Http3CancelPushFrame(16383);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3CancelPushFrame>(decoded);
        Assert.Equal(Http3FrameType.CancelPush, result.Type);
        Assert.Equal(16383, result.PushId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Settings_frame_roundtrip()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096),   // MAX_FIELD_SECTION_SIZE
            (0x01, 100),    // QPACK_MAX_TABLE_CAPACITY
            (0x07, 50),     // QPACK_BLOCKED_STREAMS
        };
        var original = new Http3SettingsFrame(parameters);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3SettingsFrame>(decoded);
        Assert.Equal(Http3FrameType.Settings, result.Type);
        Assert.Equal(3, result.Parameters.Count);
        Assert.Equal((0x06L, 4096L), result.Parameters[0]);
        Assert.Equal((0x01L, 100L), result.Parameters[1]);
        Assert.Equal((0x07L, 50L), result.Parameters[2]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void PushPromise_frame_roundtrip()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var original = new Http3PushPromiseFrame(42, headerBlock);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3PushPromiseFrame>(decoded);
        Assert.Equal(Http3FrameType.PushPromise, result.Type);
        Assert.Equal(42, result.PushId);
        Assert.Equal(headerBlock, result.HeaderBlock.ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void GoAway_frame_roundtrip()
    {
        var original = new Http3GoAwayFrame(1_000_000);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3GoAwayFrame>(decoded);
        Assert.Equal(Http3FrameType.GoAway, result.Type);
        Assert.Equal(1_000_000, result.StreamId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void MaxPushId_frame_roundtrip()
    {
        var original = new Http3MaxPushIdFrame(63);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3MaxPushIdFrame>(decoded);
        Assert.Equal(Http3FrameType.MaxPushId, result.Type);
        Assert.Equal(63, result.PushId);
    }


    [Theory]
    [Trait("RFC", "RFC9114-7")]
    [InlineData(0)]
    [InlineData(63)]               // Max 1-byte varint
    [InlineData(64)]               // Min 2-byte varint
    [InlineData(16383)]            // Max 2-byte varint
    [InlineData(16384)]            // Min 4-byte varint
    [InlineData(1_073_741_823)]    // Max 4-byte varint
    public void Roundtrip_large_varint_values(long value)
    {
        // CancelPush
        var cp = new Http3CancelPushFrame(value);
        var cpDecoded = Assert.IsType<Http3CancelPushFrame>(Decode(Encode(cp)));
        Assert.Equal(value, cpDecoded.PushId);

        // GoAway
        var ga = new Http3GoAwayFrame(value);
        var gaDecoded = Assert.IsType<Http3GoAwayFrame>(Decode(Encode(ga)));
        Assert.Equal(value, gaDecoded.StreamId);

        // MaxPushId
        var mp = new Http3MaxPushIdFrame(value);
        var mpDecoded = Assert.IsType<Http3MaxPushIdFrame>(Decode(Encode(mp)));
        Assert.Equal(value, mpDecoded.PushId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Empty_data_frame_roundtrip()
    {
        var original = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3DataFrame>(decoded);
        Assert.Empty(result.Data.ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Empty_settings_frame_roundtrip()
    {
        var original = new Http3SettingsFrame(new List<(long, long)>());

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<Http3SettingsFrame>(decoded);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Multiple_frames_roundtrip()
    {
        var frames = new Http3Frame[]
        {
            new Http3DataFrame(new byte[] { 0x01, 0x02 }),
            new Http3HeadersFrame(new byte[] { 0x82, 0x87 }),
            new Http3CancelPushFrame(7),
            new Http3SettingsFrame(new List<(long, long)> { (0x06, 8192) }),
            new Http3PushPromiseFrame(3, new byte[] { 0xAA }),
            new Http3GoAwayFrame(100),
            new Http3MaxPushIdFrame(255),
        };

        // Encode all frames into a single buffer
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var wire = new byte[totalSize];
        var offset = 0;
        foreach (var f in frames)
        {
            var span = wire.AsSpan(offset);
            offset += f.WriteTo(ref span);
        }

        // Decode all frames
        var decoder = new Http3FrameDecoder();
        var decoded = decoder.DecodeAll(wire, out var consumed);

        Assert.Equal(totalSize, consumed);
        Assert.Equal(7, decoded.Count);

        // Verify each frame type and key fields
        var data = Assert.IsType<Http3DataFrame>(decoded[0]);
        Assert.Equal(new byte[] { 0x01, 0x02 }, data.Data.ToArray());

        var headers = Assert.IsType<Http3HeadersFrame>(decoded[1]);
        Assert.Equal(new byte[] { 0x82, 0x87 }, headers.HeaderBlock.ToArray());

        var cancelPush = Assert.IsType<Http3CancelPushFrame>(decoded[2]);
        Assert.Equal(7, cancelPush.PushId);

        var settings = Assert.IsType<Http3SettingsFrame>(decoded[3]);
        Assert.Equal((0x06L, 8192L), settings.Parameters[0]);

        var pushPromise = Assert.IsType<Http3PushPromiseFrame>(decoded[4]);
        Assert.Equal(3, pushPromise.PushId);
        Assert.Equal(new byte[] { 0xAA }, pushPromise.HeaderBlock.ToArray());

        var goaway = Assert.IsType<Http3GoAwayFrame>(decoded[5]);
        Assert.Equal(100, goaway.StreamId);

        var maxPushId = Assert.IsType<Http3MaxPushIdFrame>(decoded[6]);
        Assert.Equal(255, maxPushId.PushId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7")]
    public void Re_encode_produces_identical_bytes()
    {
        var frames = new Http3Frame[]
        {
            new Http3DataFrame(new byte[] { 0xDE, 0xAD }),
            new Http3HeadersFrame(new byte[] { 0x82 }),
            new Http3CancelPushFrame(16384),
            new Http3SettingsFrame(new List<(long, long)> { (0x01, 4096), (0x07, 100) }),
            new Http3PushPromiseFrame(99, new byte[] { 0xFF }),
            new Http3GoAwayFrame(256),
            new Http3MaxPushIdFrame(0),
        };

        foreach (var original in frames)
        {
            var wire1 = Encode(original);
            var decoded = Decode(wire1);
            Assert.NotNull(decoded);
            var wire2 = Encode(decoded);

            Assert.Equal(wire1, wire2);
        }
    }


    private static byte[] Encode(Http3Frame frame)
    {
        var buf = new byte[frame.SerializedSize];
        Http3FrameEncoder.Encode(frame, buf);
        return buf;
    }

    private static Http3Frame Decode(byte[] wire)
    {
        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.NotNull(frame);
        return frame;
    }
}
