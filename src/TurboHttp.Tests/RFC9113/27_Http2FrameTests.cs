using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests HTTP/2 frame serialization and binary layout per RFC 9113 §4.1 and §6.
/// Verifies that each frame type produces the correct byte sequence when serialized.
/// </summary>
/// <remarks>
/// Class under test: <see cref="SettingsFrame"/>, <see cref="DataFrame"/>, <see cref="HeadersFrame"/>, and related frame types.
/// RFC 9113 §4.1: Frame format — length(24) + type(8) + flags(8) + stream(31) + payload.
/// </remarks>
public sealed class Http2FrameTests
{
    [Fact(DisplayName = "RFC9113-4.1-FS-001: SETTINGS frame serializes to correct binary format")]
    public void Should_SerializeToCorrectFormat_WhenSettingsFrameBuilt()
    {
        var frame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.EnablePush, 0u),
        });
        var bytes = frame.Serialize();

        Assert.Equal(9 + 12, bytes.Length);
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(12, bytes[2]);
        Assert.Equal(4, bytes[3]);
        Assert.Equal(0, bytes[4]);
        Assert.Equal(0, bytes[5]);
        Assert.Equal(0, bytes[6]);
        Assert.Equal(0, bytes[7]);
        Assert.Equal(0, bytes[8]);
    }

    [Fact(DisplayName = "RFC9113-4.1-FS-002: SETTINGS ACK serializes to empty payload with ACK flag")]
    public void Should_SerializeEmptyPayload_WhenSettingsAckBuilt()
    {
        var ack = SettingsFrame.SettingsAck();
        Assert.Equal(9, ack.Length);
        Assert.Equal(0x01, ack[4]);
    }

    [Fact(DisplayName = "RFC9113-4.1-FS-003: PING frame serializes to 8-byte payload")]
    public void Should_Serialize8BytePayload_WhenPingFrameBuilt()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data).Serialize();
        Assert.Equal(17, frame.Length);
        Assert.Equal(8, frame[2]);
        Assert.Equal(6, frame[3]);
    }

    [Fact(DisplayName = "RFC9113-4.1-FS-004: WINDOW_UPDATE frame serializes correct increment bytes")]
    public void Should_SerializeCorrectIncrement_WhenWindowUpdateFrameBuilt()
    {
        var frame = new WindowUpdateFrame(0, 65535).Serialize();
        Assert.Equal(13, frame.Length);

        Assert.Equal(0x00, frame[9]);
        Assert.Equal(0x00, frame[10]);
        Assert.Equal(0xFF, frame[11]);
        Assert.Equal(0xFF, frame[12]);
    }

    [Fact(DisplayName = "RFC9113-4.1-FS-005: DATA frame serializes with END_STREAM flag and correct type")]
    public void Should_SerializeWithEndStreamFlag_WhenDataFrameBuilt()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();
        Assert.Equal(12, frame.Length);
        Assert.Equal(0x1, frame[4]);
        Assert.Equal((byte)FrameType.Data, frame[3]);
    }

    [Fact(DisplayName = "RFC9113-4.1-FS-006: GOAWAY frame serializes with debug data")]
    public void Should_SerializeWithDebugData_WhenGoAwayFrameBuilt()
    {
        var debug = "test error"u8.ToArray();
        var frame = new GoAwayFrame(3, Http2ErrorCode.ProtocolError, debug).Serialize();
        Assert.Equal(27, frame.Length);
    }

    [Theory(DisplayName = "RFC9113-4.1-FS-007: Negative stream ID must throw ArgumentOutOfRangeException")]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Should_ThrowArgumentOutOfRangeException_WhenStreamIdIsNegative(int negativeStreamId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadersFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RstStreamFrame(negativeStreamId, Http2ErrorCode.Cancel));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowUpdateFrame(negativeStreamId, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContinuationFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
    }

    [Theory(DisplayName = "RFC9113-4.1-FS-008: Zero and positive stream IDs must be accepted")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Should_AcceptStreamId_WhenStreamIdIsNonNegative(int streamId)
    {
        var frame = new DataFrame(streamId, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(streamId, frame.StreamId);
    }
}
