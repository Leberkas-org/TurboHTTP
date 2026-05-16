using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Frames;

public sealed class FrameRoundTripSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_data_frame()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0xDE, 0xAD };
        var original = new DataFrame(payload);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<DataFrame>(decoded);
        Assert.Equal(FrameType.Data, result.Type);
        Assert.Equal(payload, result.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_headers_frame()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x87, 0x44, 0x88, 0x62, 0xA1 };
        var original = new HeadersFrame(headerBlock);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<HeadersFrame>(decoded);
        Assert.Equal(FrameType.Headers, result.Type);
        Assert.Equal(headerBlock, result.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_cancel_push_frame()
    {
        var original = new CancelPushFrame(16383);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<CancelPushFrame>(decoded);
        Assert.Equal(FrameType.CancelPush, result.Type);
        Assert.Equal(16383, result.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_settings_frame()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096), // MAX_FIELD_SECTION_SIZE
            (0x01, 100), // QPACK_MAX_TABLE_CAPACITY
            (0x07, 50), // QPACK_BLOCKED_STREAMS
        };
        var original = new SettingsFrame(parameters);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<SettingsFrame>(decoded);
        Assert.Equal(FrameType.Settings, result.Type);
        Assert.Equal(3, result.Parameters.Count);
        Assert.Equal((0x06L, 4096L), result.Parameters[0]);
        Assert.Equal((0x01L, 100L), result.Parameters[1]);
        Assert.Equal((0x07L, 50L), result.Parameters[2]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_push_promise_frame()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var original = new PushPromiseFrame(42, headerBlock);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<PushPromiseFrame>(decoded);
        Assert.Equal(FrameType.PushPromise, result.Type);
        Assert.Equal(42, result.PushId);
        Assert.Equal(headerBlock, result.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_goaway_frame()
    {
        var original = new GoAwayFrame(1_000_000);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<GoAwayFrame>(decoded);
        Assert.Equal(FrameType.GoAway, result.Type);
        Assert.Equal(1_000_000, result.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_max_push_id_frame()
    {
        var original = new MaxPushIdFrame(63);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<MaxPushIdFrame>(decoded);
        Assert.Equal(FrameType.MaxPushId, result.Type);
        Assert.Equal(63, result.PushId);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    [InlineData(0)]
    [InlineData(63)] // Max 1-byte varint
    [InlineData(64)] // Min 2-byte varint
    [InlineData(16383)] // Max 2-byte varint
    [InlineData(16384)] // Min 4-byte varint
    [InlineData(1_073_741_823)] // Max 4-byte varint
    public void FrameRoundTrip_should_preserve_large_varint_values(long value)
    {
        // CancelPush
        var cp = new CancelPushFrame(value);
        var cpDecoded = Assert.IsType<CancelPushFrame>(Decode(Encode(cp)));
        Assert.Equal(value, cpDecoded.PushId);

        // GoAway
        var ga = new GoAwayFrame(value);
        var gaDecoded = Assert.IsType<GoAwayFrame>(Decode(Encode(ga)));
        Assert.Equal(value, gaDecoded.StreamId);

        // MaxPushId
        var mp = new MaxPushIdFrame(value);
        var mpDecoded = Assert.IsType<MaxPushIdFrame>(Decode(Encode(mp)));
        Assert.Equal(value, mpDecoded.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_empty_data_frame()
    {
        var original = new DataFrame(ReadOnlyMemory<byte>.Empty);

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<DataFrame>(decoded);
        Assert.Empty(result.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_empty_settings_frame()
    {
        var original = new SettingsFrame(new List<(long, long)>());

        var wire = Encode(original);
        var decoded = Decode(wire);

        var result = Assert.IsType<SettingsFrame>(decoded);
        Assert.Empty(result.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_preserve_multiple_frames()
    {
        var frames = new Http3Frame[]
        {
            new DataFrame(new byte[] { 0x01, 0x02 }),
            new HeadersFrame(new byte[] { 0x82, 0x87 }),
            new CancelPushFrame(7),
            new SettingsFrame(new List<(long, long)> { (0x06, 8192) }),
            new PushPromiseFrame(3, new byte[] { 0xAA }),
            new GoAwayFrame(100),
            new MaxPushIdFrame(255),
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
        var decoder = new FrameDecoder();
        var decoded = decoder.DecodeAll(wire, out var consumed);

        Assert.Equal(totalSize, consumed);
        Assert.Equal(7, decoded.Count);

        // Verify each frame type and key fields
        var data = Assert.IsType<DataFrame>(decoded[0]);
        Assert.Equal(new byte[] { 0x01, 0x02 }, data.Data.ToArray());

        var headers = Assert.IsType<HeadersFrame>(decoded[1]);
        Assert.Equal(new byte[] { 0x82, 0x87 }, headers.HeaderBlock.ToArray());

        var cancelPush = Assert.IsType<CancelPushFrame>(decoded[2]);
        Assert.Equal(7, cancelPush.PushId);

        var settings = Assert.IsType<SettingsFrame>(decoded[3]);
        Assert.Equal((0x06L, 8192L), settings.Parameters[0]);

        var pushPromise = Assert.IsType<PushPromiseFrame>(decoded[4]);
        Assert.Equal(3, pushPromise.PushId);
        Assert.Equal(new byte[] { 0xAA }, pushPromise.HeaderBlock.ToArray());

        var goaway = Assert.IsType<GoAwayFrame>(decoded[5]);
        Assert.Equal(100, goaway.StreamId);

        var maxPushId = Assert.IsType<MaxPushIdFrame>(decoded[6]);
        Assert.Equal(255, maxPushId.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameRoundTrip_should_produce_identical_bytes_when_re_encoded()
    {
        var frames = new Http3Frame[]
        {
            new DataFrame("\u07ad"u8.ToArray()),
            new HeadersFrame(new byte[] { 0x82 }),
            new CancelPushFrame(16384),
            new SettingsFrame(new List<(long, long)> { (0x01, 4096), (0x07, 100) }),
            new PushPromiseFrame(99, new byte[] { 0xFF }),
            new GoAwayFrame(256),
            new MaxPushIdFrame(0),
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
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static Http3Frame Decode(byte[] wire)
    {
        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);
        Assert.Equal(DecodeStatus.Success, status);
        Assert.NotNull(frame);
        return frame;
    }
}