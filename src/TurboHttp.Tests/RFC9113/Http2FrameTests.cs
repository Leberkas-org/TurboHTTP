using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2FrameTests
{
    [Fact]
    public void SettingsFrame_Serialize_CorrectFormat()
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

    [Fact]
    public void SettingsAck_Serialize_EmptyPayload()
    {
        var ack = SettingsFrame.SettingsAck();
        Assert.Equal(9, ack.Length);
        Assert.Equal(0x01, ack[4]);
    }

    [Fact]
    public void PingFrame_Serialize_8BytePayload()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data).Serialize();
        Assert.Equal(17, frame.Length);
        Assert.Equal(8, frame[2]);
        Assert.Equal(6, frame[3]);
    }

    [Fact]
    public void WindowUpdateFrame_Serialize_CorrectIncrement()
    {
        var frame = new WindowUpdateFrame(0, 65535).Serialize();
        Assert.Equal(13, frame.Length);

        Assert.Equal(0x00, frame[9]);
        Assert.Equal(0x00, frame[10]);
        Assert.Equal(0xFF, frame[11]);
        Assert.Equal(0xFF, frame[12]);
    }

    [Fact]
    public void DataFrame_Serialize_WithEndStream()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();
        Assert.Equal(12, frame.Length);
        Assert.Equal(0x1, frame[4]);
        Assert.Equal((byte)FrameType.Data, frame[3]);
    }

    [Fact]
    public void GoAwayFrame_Serialize_WithDebugData()
    {
        var debug = "test error"u8.ToArray();
        var frame = new GoAwayFrame(3, Http2ErrorCode.ProtocolError, debug).Serialize();
        Assert.Equal(27, frame.Length);
    }
}