using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderErrorCodeTests
{
    [Fact(DisplayName = "RFC9113-err-000: NO_ERROR (0x0) in GOAWAY decoded")]
    public void ErrorCode_NoError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.NoError, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded")]
    public void ErrorCode_ProtocolError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.ProtocolError, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded")]
    public void ErrorCode_InternalError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.InternalError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.InternalError, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded")]
    public void ErrorCode_FlowControlError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.FlowControlError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.FlowControlError, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded")]
    public void ErrorCode_SettingsTimeout_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.SettingsTimeout).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.SettingsTimeout, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded")]
    public void ErrorCode_StreamClosed_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.StreamClosed).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.StreamClosed, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-006: FRAME_SIZE_ERROR (0x6) decoded")]
    public void ErrorCode_FrameSizeError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.FrameSizeError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.FrameSizeError, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded")]
    public void ErrorCode_RefusedStream_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.RefusedStream).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.RefusedStream, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-008: CANCEL (0x8) in RST_STREAM decoded")]
    public void ErrorCode_Cancel_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.Cancel, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded")]
    public void ErrorCode_CompressionError_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.CompressionError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.CompressionError, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded")]
    public void ErrorCode_ConnectError_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.ConnectError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.ConnectError, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded")]
    public void ErrorCode_EnhanceYourCalm_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.EnhanceYourCalm).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, goAway.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-00c: INADEQUATE_SECURITY (0xc) decoded")]
    public void ErrorCode_InadequateSecurity_InRstStream_Decoded()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.InadequateSecurity).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var rst = Assert.IsType<RstStreamFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.InadequateSecurity, rst.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded")]
    public void ErrorCode_Http11Required_InGoAway_Decoded()
    {
        var frame = new GoAwayFrame(0, Http2ErrorCode.Http11Required).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);
        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(Http2ErrorCode.Http11Required, goAway.ErrorCode);
    }
}
