using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderStreamFlowControlTests
{
    [Fact(DisplayName = "7540-5.2-dec-001: New stream initial window is 65535")]
    public void FlowControl_InitialConnectionReceiveWindow_Is65535()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(65535, session.ConnectionReceiveWindow);
    }

    [Fact(DisplayName = "7540-5.2-dec-002: WINDOW_UPDATE decoded and window updated")]
    public void FlowControl_WindowUpdateDecoded_WindowUpdated()
    {
        var frame = new WindowUpdateFrame(0, 32768).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);

        // Connection-level WINDOW_UPDATE (stream 0) updates the connection send window.
        Assert.Equal(65535 + 32768, session.ConnectionSendWindow);
    }

    [Fact(DisplayName = "7540-5.2-dec-003: Peer DATA beyond window causes FLOW_CONTROL_ERROR")]
    public void FlowControl_PeerDataExceedsReceiveWindow_ThrowsFlowControlError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // Reduce connection receive window to 4 bytes.
        session.SetConnectionReceiveWindow(4);

        // Send 10 bytes of data — exceeds the window.
        var dataFrame = new DataFrame(1, new byte[10], endStream: false).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.2-dec-004: WINDOW_UPDATE overflow causes FLOW_CONTROL_ERROR")]
    public void FlowControl_WindowUpdateOverflow_ThrowsFlowControlError()
    {
        // The connection send window starts at 65535. Sending increment = 0x7FFFFFFF
        // would produce 65535 + 2147483647 = 2147549182 > 0x7FFFFFFF → overflow.
        var overflowFrame = new byte[13]; // 9 + 4
        overflowFrame[0] = 0; overflowFrame[1] = 0; overflowFrame[2] = 4; // length=4
        overflowFrame[3] = 0x08; // WINDOW_UPDATE
        overflowFrame[4] = 0x00; // flags
        // stream = 0
        overflowFrame[5] = 0; overflowFrame[6] = 0; overflowFrame[7] = 0; overflowFrame[8] = 0;
        // increment = 0x7FFFFFFF
        overflowFrame[9]  = 0x7F;
        overflowFrame[10] = 0xFF;
        overflowFrame[11] = 0xFF;
        overflowFrame[12] = 0xFF;

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(overflowFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.2-dec-008: WINDOW_UPDATE increment=0 causes PROTOCOL_ERROR")]
    public void FlowControl_WindowUpdateIncrementZero_ThrowsProtocolError()
    {
        // Build raw WINDOW_UPDATE with increment = 0.
        var frame = new byte[13];
        frame[0] = 0; frame[1] = 0; frame[2] = 4; // length=4
        frame[3] = 0x08; // WINDOW_UPDATE
        frame[4] = 0x00; // flags
        // stream = 0 (bytes 5–8 are zero)
        // increment = 0 (bytes 9–12 are zero)

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}
