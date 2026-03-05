using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderErrorCodeTests
{
    [Fact(DisplayName = "7540-err-000: NO_ERROR (0x0) in GOAWAY decoded")]
    public void ErrorCode_NoError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.IsGoingAway);
        Assert.Equal(Http2ErrorCode.NoError, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded")]
    public void ErrorCode_ProtocolError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ProtocolError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Single(session.RstStreams);
        Assert.Equal(Http2ErrorCode.ProtocolError, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded")]
    public void ErrorCode_InternalError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.InternalError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.InternalError, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded")]
    public void ErrorCode_FlowControlError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.FlowControlError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.FlowControlError, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded")]
    public void ErrorCode_SettingsTimeout_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.SettingsTimeout).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.SettingsTimeout, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded")]
    public void ErrorCode_StreamClosed_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.StreamClosed).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Single(session.RstStreams);
        Assert.Equal(Http2ErrorCode.StreamClosed, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-006: FRAME_SIZE_ERROR (0x6) decoded")]
    public void ErrorCode_FrameSizeError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.FrameSizeError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Single(session.RstStreams);
        Assert.Equal(Http2ErrorCode.FrameSizeError, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded")]
    public void ErrorCode_RefusedStream_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.RefusedStream).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.RefusedStream, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-008: CANCEL (0x8) in RST_STREAM decoded")]
    public void ErrorCode_Cancel_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.Cancel, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded")]
    public void ErrorCode_CompressionError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.CompressionError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.CompressionError, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded")]
    public void ErrorCode_ConnectError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ConnectError).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.ConnectError, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded")]
    public void ErrorCode_EnhanceYourCalm_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.EnhanceYourCalm).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, session.GoAwayFrame!.ErrorCode);
    }

    [Fact(DisplayName = "7540-err-00c: INADEQUATE_SECURITY (0xc) decoded")]
    public void ErrorCode_InadequateSecurity_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.InadequateSecurity).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.InadequateSecurity, session.RstStreams[0].Error);
    }

    [Fact(DisplayName = "7540-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded")]
    public void ErrorCode_Http11Required_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.Http11Required).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.Equal(Http2ErrorCode.Http11Required, session.GoAwayFrame!.ErrorCode);
    }
}
