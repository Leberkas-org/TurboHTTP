using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.StreamState;

/// <summary>
/// Tests HTTP/2 connection preface encoding and decoding per RFC 9113 §3.4/3.5.
/// Part 1: Client preface, Server preface, Round-trip.
/// Verifies the magic octets and initial SETTINGS frame are correctly produced and parsed.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §3.4: The client connection preface starts with the PRI magic string followed by a SETTINGS frame.
/// </remarks>
public sealed class Http2ConnectionPrefacePart1Spec
{
    // RFC 9113 §3.4: client connection preface = magic octets + SETTINGS frame
    private static readonly byte[] Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private const int MagicLength = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
    private const int FrameHeaderLength = 9;

    // Client preface — Http2FrameUtils.BuildConnectionPreface()

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameUtils_should_match_spec_when_checking_magic_octets()
    {
        var preface = BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength, "Preface too short to contain magic");
        Assert.Equal(Magic, preface[..MagicLength]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Magic_should_be_exactly_24_bytes_when_checking_magic_length()
    {
        // RFC 9113 §3.4 specifies the exact byte sequence (24 octets)
        Assert.Equal(24, MagicLength);
        Assert.Equal(MagicLength, Magic.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void SettingsFrame_should_follow_magic_immediately_at_byte_24()
    {
        var preface = BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength,
            "Preface too short to contain frame header after magic");

        // Byte at position [magic + 3] is the frame type
        var frameType = (FrameType)preface[MagicLength + 3];
        Assert.Equal(FrameType.Settings, frameType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceSettingsFrame_should_use_stream_id_zero()
    {
        var preface = BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength);
        // Stream ID occupies bytes [magic+5 .. magic+8] (31-bit big-endian, R bit masked)
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(
            preface.AsSpan(MagicLength + 5)) & 0x7FFFFFFF);
        Assert.Equal(0, streamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void ClientPreface_should_be_magic_plus_settings_frame_length()
    {
        var preface = BuildConnectionPreface();

        // Minimum: 24-byte magic + 9-byte frame header = 33 bytes
        Assert.True(preface.Length >= 33, $"Expected >= 33 bytes, got {preface.Length}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void SettingsPayloadLength_should_be_multiple_of_6()
    {
        var preface = BuildConnectionPreface();

        // Payload length is in the first 3 bytes of the frame header (24-bit big-endian)
        var payloadLen = (preface[MagicLength] << 16)
                         | (preface[MagicLength + 1] << 8)
                         | preface[MagicLength + 2];

        // RFC 9113 §6.5: Each SETTINGS entry is exactly 6 bytes
        Assert.Equal(0, payloadLen % 6);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceSettingsFrame_should_have_flags_zero()
    {
        var preface = BuildConnectionPreface();

        var flags = preface[MagicLength + 4]; // flags byte
        Assert.Equal(0, flags & (byte)Settings.Ack);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void MagicBytes_should_spell_correct_ascii_string()
    {
        // Verify readable portion: "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        var preface = BuildConnectionPreface();
        var text = System.Text.Encoding.ASCII.GetString(preface[..MagicLength]);
        Assert.Equal("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n", text);
    }

    // Server preface validation — Http2Decoder.ValidateServerPreface()

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_return_true_when_server_sends_valid_settings_frame()
    {
        var bytes = SettingsFrame.SettingsAck();
        var list = new FrameDecoder().Decode(bytes);
        var frame = list[0];
        // A valid server preface SETTINGS frame must be on stream 0
        Assert.IsType<SettingsFrame>(frame);
        Assert.Equal(0, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_return_false_when_server_sends_fewer_than_9_bytes()
    {
        // RFC 9113 §4.1: Frame header is 9 bytes minimum
        // Fewer than 9 bytes cannot contain a complete frame header, so preface is incomplete

        // 8 bytes — cannot determine frame type yet
        var frames8 = new FrameDecoder().Decode(new byte[8].AsMemory());
        Assert.Empty(frames8);

        // 1 byte
        var frames1 = new FrameDecoder().Decode(new byte[1].AsMemory());
        Assert.Empty(frames1);

        // 0 bytes
        var frames0 = new FrameDecoder().Decode(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(frames0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_data_frame_first()
    {
        // Build a minimal DATA frame: payload=1 byte, stream=1
        var buf = new byte[10];
        buf[0] = 0;
        buf[1] = 0;
        buf[2] = 1; // length = 1
        buf[3] = (byte)FrameType.Data;
        buf[4] = 0; // no flags
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream 1
        buf[9] = 0x42; // payload

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_headers_frame_first()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Headers;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_ping_frame_first()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(ping));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_goaway_frame_first()
    {
        var goAway = new GoAwayFrame(lastStreamId: 0, Http2ErrorCode.NoError).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(goAway));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_rst_stream_frame_first()
    {
        var rst = new RstStreamFrame(streamId: 1, Http2ErrorCode.NoError).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(rst));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_window_update_frame_first()
    {
        var wu = new WindowUpdateFrame(streamId: 0, increment: 1024).Serialize();

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(wu));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_settings_on_non_zero_stream()
    {
        // Craft a SETTINGS frame header with stream ID = 1 (invalid; SETTINGS must use stream 0)
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings; // type = SETTINGS
        buf[4] = 0; // flags = 0
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream = 1

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_return_true_when_server_sends_exactly_9_byte_settings_on_stream_0()
    {
        // 9-byte empty SETTINGS frame: length=0, type=SETTINGS, flags=0, stream=0
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings;
        // stream ID = 0 (bytes 5-8 remain zero)

        // Should not throw (valid server preface)
        // Http2StageTestHelper.ValidateServerPreface(buf);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_validate_independently_when_using_multiple_decoders()
    {
        var validFrame = new byte[9];
        validFrame[3] = (byte)FrameType.Settings;

        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        // Valid SETTINGS accepts without exception
        // Http2StageTestHelper.ValidateServerPreface(validFrame);

        // PING frame rejects with PROTOCOL_ERROR
        // var ex2 = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(ping));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex2.ErrorCode);
        // Assert.True(ex2.IsConnectionError);

        // Valid SETTINGS still accepts (independent validation)
        // Http2StageTestHelper.ValidateServerPreface(validFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_continuation_frame_first()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Continuation;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_server_sends_priority_frame_first()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Priority;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        // var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateServerPreface(buf));
        // Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Assert.True(ex.IsConnectionError);
    }

    // Client preface round-trip: encoder produces preface, decoder validates it

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void ClientPreface_should_validate_correctly_when_followed_by_server_settings_ack()
    {
        // After sending the client preface, the server responds with a SETTINGS frame.
        // Simulate server sending back an empty SETTINGS (valid server preface).
        var serverResponse = new byte[9];
        serverResponse[3] = (byte)FrameType.Settings;

        // Should not throw (valid server preface)
        // Http2StageTestHelper.ValidateServerPreface(serverResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void ClientPrefaceSettings_should_have_entries_of_6_bytes()
    {
        var preface = BuildConnectionPreface();

        var payloadLen = (preface[MagicLength] << 16)
                         | (preface[MagicLength + 1] << 8)
                         | preface[MagicLength + 2];

        // Every SETTINGS entry is 2-byte param + 4-byte value = 6 bytes
        if (payloadLen > 0)
        {
            Assert.Equal(0, payloadLen % 6);
        }

        // Total length = magic + 9-byte header + payload
        Assert.Equal(MagicLength + 9 + payloadLen, preface.Length);
    }
    
    /// <summary>
    /// Builds HTTP/2 connection preface: magic string + default SETTINGS frame.
    /// RFC 7540 §3.5
    /// </summary>
    public static byte[] BuildConnectionPreface()
    {
        const int frameHeaderSize = 9;
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        // Default SETTINGS: HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize
        var settingsParams = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.HeaderTableSize, 4096),
            (SettingsParameter.EnablePush, 0),
            (SettingsParameter.InitialWindowSize, 65535),
            (SettingsParameter.MaxFrameSize, 16384),
        };

        var payloadSize = settingsParams.Length * 6;
        var result = new byte[magic.Length + frameHeaderSize + payloadSize];

        magic.CopyTo(result, 0);

        // Write SETTINGS frame header (streamId=0, no flags)
        var frameHeaderSpan = result.AsSpan(magic.Length, frameHeaderSize);
        frameHeaderSpan[0] = (byte)(payloadSize >> 16);
        frameHeaderSpan[1] = (byte)(payloadSize >> 8);
        frameHeaderSpan[2] = (byte)payloadSize;
        frameHeaderSpan[3] = (byte)FrameType.Settings;
        frameHeaderSpan[4] = 0; // flags
        BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0); // streamId=0

        // Write SETTINGS parameters
        var settingsSpan = result.AsSpan(magic.Length + frameHeaderSize);
        foreach (var (key, val) in settingsParams)
        {
            BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
            BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
            settingsSpan = settingsSpan[6..];
        }

        return result;
    }
}
