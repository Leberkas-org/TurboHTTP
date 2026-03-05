using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2FrameDecoderBasicTests
{
    // ── Settings ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.5-001: SETTINGS frame parameters are decoded correctly")]
    public void Decode_SettingsFrame_ExtractsParameters()
    {
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 65535u),
        }).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(settings);

        Assert.Single(frames);
        var sf = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.False(sf.IsAck);
        Assert.Equal(2, sf.Parameters.Count);
        Assert.Contains(sf.Parameters, p => p.Item1 == SettingsParameter.MaxConcurrentStreams && p.Item2 == 100u);
        Assert.Contains(sf.Parameters, p => p.Item1 == SettingsParameter.InitialWindowSize && p.Item2 == 65535u);
    }

    [Fact(DisplayName = "9113-6.5-002: SETTINGS ACK flag is preserved")]
    public void Decode_SettingsAck_IsAckTrue()
    {
        var ack = SettingsFrame.SettingsAck();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(ack);

        Assert.Single(frames);
        var sf = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(sf.IsAck);
    }

    // ── Ping ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.7-001: PING request frame data is decoded correctly")]
    public void Decode_PingRequest_ReturnsCorrectData()
    {
        var data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var ping = new PingFrame(data, isAck: false).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(ping);

        Assert.Single(frames);
        var pf = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(pf.IsAck);
        Assert.Equal(data, pf.Data);
    }

    [Fact(DisplayName = "9113-6.7-002: PING ACK flag is preserved")]
    public void Decode_PingAck_IsAckTrue()
    {
        var data = new byte[] { 7, 6, 5, 4, 3, 2, 1, 0 };
        var ping = new PingFrame(data, isAck: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(ping);

        Assert.Single(frames);
        var pf = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(pf.IsAck);
        Assert.Equal(data, pf.Data);
    }

    // ── WindowUpdate ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.9-001: WINDOW_UPDATE frame increment is decoded correctly")]
    public void Decode_WindowUpdate_ReturnsIncrement()
    {
        var frame = new WindowUpdateFrame(1, 32768).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var wf = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, wf.StreamId);
        Assert.Equal(32768, wf.Increment);
    }

    // ── RstStream ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.4-001: RST_STREAM error code is decoded correctly")]
    public void Decode_RstStream_ReturnsErrorCode()
    {
        var frame = new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var rf = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(3, rf.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, rf.ErrorCode);
    }

    // ── GoAway ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.8-001: GOAWAY last-stream-id and error code are decoded correctly")]
    public void Decode_GoAway_ParsedCorrectly()
    {
        var frame = new GoAwayFrame(5, Http2ErrorCode.NoError,
            "server shutdown"u8.ToArray()).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var gf = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(5, gf.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, gf.ErrorCode);
    }

    // ── Fragmentation ────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-4.1-001: Frame split across two TCP segments is reassembled")]
    public void Decode_FrameFragmented_ReassembledCorrectly()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        const int cut = 5;
        var chunk1 = ping[..cut];
        var chunk2 = ping[cut..];

        var decoder = new Http2FrameDecoder();
        var result1 = decoder.Decode(chunk1);
        var result2 = decoder.Decode(chunk2);

        Assert.Empty(result1);
        Assert.Single(result2);
        Assert.IsType<PingFrame>(result2[0]);
    }

    // ── Multiple frames ──────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-4.1-002: Multiple frames in one TCP segment are all decoded")]
    public void Decode_MultipleFrames_AllProcessed()
    {
        var ping1 = new PingFrame([1, 1, 1, 1, 1, 1, 1, 1]).Serialize();
        var ping2 = new PingFrame([2, 2, 2, 2, 2, 2, 2, 2]).Serialize();
        var settings = SettingsFrame.SettingsAck();

        var combined = new byte[ping1.Length + ping2.Length + settings.Length];
        ping1.CopyTo(combined, 0);
        ping2.CopyTo(combined, ping1.Length);
        settings.CopyTo(combined, ping1.Length + ping2.Length);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(3, frames.Count);
        Assert.IsType<PingFrame>(frames[0]);
        Assert.IsType<PingFrame>(frames[1]);
        Assert.IsType<SettingsFrame>(frames[2]);
    }

    // ── HEADERS + DATA (frame-level) ─────────────────────────────────────────

    [Fact(DisplayName = "9113-6.1-001: HEADERS and DATA frames are decoded with correct flags")]
    public void Decode_HeadersAndData_CorrectFrameObjects()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode(
        [
            (":status", "200"),
            ("content-type", "text/plain"),
        ]);
        var headersFrame = new HeadersFrame(1, headerBlock,
            endStream: false, endHeaders: true).Serialize();

        var bodyData = "Hello, HTTP/2!"u8.ToArray();
        var dataFrame = new DataFrame(1, bodyData, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, hf.StreamId);
        Assert.False(hf.EndStream);
        Assert.True(hf.EndHeaders);

        var df = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(1, df.StreamId);
        Assert.True(df.EndStream);
        Assert.Equal(bodyData, df.Data.ToArray());
    }

    [Fact(DisplayName = "9113-6.2-001: HEADERS with END_STREAM flag is decoded correctly")]
    public void Decode_HeadersWithEndStream_FlagsCorrect()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "204")]);
        var headersFrame = new HeadersFrame(3, headerBlock,
            endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(3, hf.StreamId);
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    // ── CONTINUATION ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-6.10-001: HEADERS + CONTINUATION frames are decoded as separate frame objects")]
    public void Decode_ContinuationFrames_DecodedSeparately()
    {
        var hpackEncoder = new HpackEncoder();
        var headerBlock = hpackEncoder.Encode(
        [
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123"),
        ]);

        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(5, split1,
            endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(5, split2,
            endHeaders: true).Serialize();

        var combined = new byte[headersFrame.Length + contFrame.Length];
        headersFrame.CopyTo(combined, 0);
        contFrame.CopyTo(combined, headersFrame.Length);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(5, hf.StreamId);
        Assert.False(hf.EndHeaders);

        var cf = Assert.IsType<ContinuationFrame>(frames[1]);
        Assert.Equal(5, cf.StreamId);
        Assert.True(cf.EndHeaders);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "9113-4.1-003: Reset clears buffered partial data")]
    public void Decode_AfterReset_PartialBufferCleared()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        var chunk1 = ping[..5];

        var decoder = new Http2FrameDecoder();
        var r1 = decoder.Decode(chunk1);
        Assert.Empty(r1);

        decoder.Reset();

        // After reset, the remainder is cleared — full frame needed again
        var r2 = decoder.Decode(ping);
        Assert.Single(r2);
        Assert.IsType<PingFrame>(r2[0]);
    }
}
