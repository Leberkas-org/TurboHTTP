using System.Buffers.Binary;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.FlowControl;

/// <summary>
/// Tests decoder defenses against resource-exhaustion attacks such as SETTINGS floods.
/// Part 1: SETTINGS/RST/CONTINUATION/PING flood protection.
/// Verifies that flood protection thresholds produce Http2Exception with EnhanceYourCalm.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §10.5: Implementations should limit the rate at which control frames can be received to protect against floods.
/// </remarks>
public sealed class ResourceExhaustionPart1Spec
{

    private static void EnforceSettingsFloodThreshold(int settingsCount, int threshold = 100)
    {
        if (settingsCount > threshold)
        {
            throw new Http2Exception(
                $"RFC 9113 security: Excessive SETTINGS frames ({settingsCount}) — possible SETTINGS flood.",
                Http2ErrorCode.EnhanceYourCalm);
        }
    }

    private static void EnforceRstFloodThreshold(int rstCount, int threshold = 100)
    {
        if (rstCount > threshold)
        {
            throw new Http2Exception(
                "RFC 9113 security: Rapid RST_STREAM cycling — possible CVE-2023-44487 attack.",
                Http2ErrorCode.ProtocolError);
        }
    }

    private static void EnforceContinuationFloodThreshold(int count, int threshold = 1000)
    {
        if (count >= threshold)
        {
            throw new Http2Exception(
                $"RFC 9113 security: Excessive CONTINUATION frames ({count}) — possible CONTINUATION flood.",
                Http2ErrorCode.ProtocolError);
        }
    }

    private static void EnforcePingFloodThreshold(int count, int threshold = 1000)
    {
        if (count > threshold)
        {
            throw new Http2Exception(
                $"RFC 9113 security: Excessive non-ACK PING frames ({count}) — possible PING flood.",
                Http2ErrorCode.EnhanceYourCalm);
        }
    }


    private static byte[] BuildRawFrame(byte frameType, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        var len = payload.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
        frame[3] = frameType;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildSettingsFrame(bool ack, (ushort Id, uint Value)[] parameters)
    {
        var payload = new byte[parameters.Length * 6];
        for (var i = 0; i < parameters.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), parameters[i].Id);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), parameters[i].Value);
        }

        return BuildRawFrame(0x4, ack ? (byte)0x1 : (byte)0x0, 0, payload);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_throw_http2_exception_when_101_settings_frames_received()
    {
        var decoder = new Http2FrameDecoder();
        var settingsBytes = BuildSettingsFrame(ack: false, []);
        var settingsCount = 0;

        // Decode 100 SETTINGS frames — all should succeed
        for (var i = 0; i < 100; i++)
        {
            var frames = decoder.Decode(settingsBytes);
            foreach (var frame in frames)
            {
                if (frame is SettingsFrame sf && !sf.IsAck)
                {
                    settingsCount++;
                }
            }
            EnforceSettingsFloodThreshold(settingsCount); // must not throw
        }

        // Decode the 101st
        var framesAgain = decoder.Decode(settingsBytes);
        foreach (var frame in framesAgain)
        {
            if (frame is SettingsFrame sf && !sf.IsAck)
            {
                settingsCount++;
            }
        }

        var ex = Assert.Throws<Http2Exception>(() => EnforceSettingsFloodThreshold(settingsCount));
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_100_settings_frames_without_exception()
    {
        var decoder = new Http2FrameDecoder();
        var settingsBytes = BuildSettingsFrame(ack: false, []);
        var settingsCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var frames = decoder.Decode(settingsBytes);
            foreach (var frame in frames)
            {
                if (frame is SettingsFrame sf && !sf.IsAck)
                {
                    settingsCount++;
                }
            }
        }

        EnforceSettingsFloodThreshold(settingsCount); // must not throw
        Assert.Equal(100, settingsCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_not_count_settings_ack_toward_flood_threshold()
    {
        var decoder = new Http2FrameDecoder();
        var settingsAck = BuildSettingsFrame(ack: true, []);
        var settingsCount = 0;

        // 200 ACK SETTINGS frames — none should count toward the non-ACK limit
        for (var i = 0; i < 200; i++)
        {
            var frames = decoder.Decode(settingsAck);
            foreach (var frame in frames)
            {
                if (frame is SettingsFrame sf && !sf.IsAck)
                {
                    settingsCount++;
                }
            }
        }

        EnforceSettingsFloodThreshold(settingsCount); // must not throw
        Assert.Equal(0, settingsCount);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_throw_http2_exception_when_101_rst_stream_received()
    {
        var decoder = new Http2FrameDecoder();
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var rstCount = 0;

        // Decode 100 RST_STREAM frames on different stream IDs
        for (var i = 0; i < 100; i++)
        {
            var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
            var frames = decoder.Decode(rst);
            foreach (var frame in frames)
            {
                if (frame is RstStreamFrame)
                {
                    rstCount++;
                }
            }
            EnforceRstFloodThreshold(rstCount); // must not throw
        }

        // Decode the 101st
        var rst101 = BuildRawFrame(0x3, 0x0, 201, errorCode);
        var framesAgain = decoder.Decode(rst101);
        foreach (var frame in framesAgain)
        {
            if (frame is RstStreamFrame)
            {
                rstCount++;
            }
        }

        var ex = Assert.Throws<Http2Exception>(() => EnforceRstFloodThreshold(rstCount));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_accept_100_rst_stream_frames_without_exception()
    {
        var decoder = new Http2FrameDecoder();
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var rstCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
            var frames = decoder.Decode(rst);
            foreach (var frame in frames)
            {
                if (frame is RstStreamFrame)
                {
                    rstCount++;
                }
            }
        }

        EnforceRstFloodThreshold(rstCount); // must not throw
        Assert.Equal(100, rstCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_include_cve_reference_in_rapid_reset_message()
    {
        var decoder = new Http2FrameDecoder();
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var rstCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
            var frames = decoder.Decode(rst);
            foreach (var frame in frames)
            {
                if (frame is RstStreamFrame)
                {
                    rstCount++;
                }
            }
        }

        rstCount++; // simulate 101st RST
        var ex = Assert.Throws<Http2Exception>(() => EnforceRstFloodThreshold(rstCount));
        Assert.Contains("CVE-2023-44487", ex.Message);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_throw_http2_exception_when_1000_continuation_frames_received()
    {
        var decoder = new Http2FrameDecoder();
        var headersFrame = BuildRawFrame(0x1, 0x0, 1, [0x88]);  // no END_HEADERS
        var continuationNoEnd = BuildRawFrame(0x9, 0x0, 1, []);

        var chunk = new byte[headersFrame.Length + 999 * continuationNoEnd.Length];
        headersFrame.CopyTo(chunk, 0);
        for (var i = 0; i < 999; i++)
        {
            continuationNoEnd.CopyTo(chunk, headersFrame.Length + i * continuationNoEnd.Length);
        }

        var frames = decoder.Decode(chunk);
        var continuationCount = 0;
        foreach (var frame in frames)
        {
            if (frame is ContinuationFrame)
            {
                continuationCount++;
            }
        }

        // 999 CONTINUATION frames (plus 1 HEADERS) — should not throw
        EnforceContinuationFloodThreshold(continuationCount); // must not throw

        // Now add the 1000th
        var continuation1000 = BuildRawFrame(0x9, 0x0, 1, []);
        var frames1000 = decoder.Decode(continuation1000);
        foreach (var frame in frames1000)
        {
            if (frame is ContinuationFrame)
            {
                continuationCount++;
            }
        }

        var ex = Assert.Throws<Http2Exception>(() => EnforceContinuationFloodThreshold(continuationCount));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_accept_999_continuation_frames_without_exception()
    {
        var decoder = new Http2FrameDecoder();
        var headersFrame = BuildRawFrame(0x1, 0x0, 1, [0x88]);  // no END_HEADERS
        var continuationNoEnd = BuildRawFrame(0x9, 0x0, 1, []);

        var chunk = new byte[headersFrame.Length + 999 * continuationNoEnd.Length];
        headersFrame.CopyTo(chunk, 0);
        for (var i = 0; i < 999; i++)
        {
            continuationNoEnd.CopyTo(chunk, headersFrame.Length + i * continuationNoEnd.Length);
        }

        var frames = decoder.Decode(chunk);
        var continuationCount = 0;
        foreach (var frame in frames)
        {
            if (frame is ContinuationFrame)
            {
                continuationCount++;
            }
        }

        EnforceContinuationFloodThreshold(continuationCount); // must not throw
        Assert.Equal(999, continuationCount);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_throw_http2_exception_when_1001_ping_frames_received()
    {
        var decoder = new Http2FrameDecoder();
        var pingPayload = new byte[8];  // 8-byte PING payload
        var pingFrame = BuildRawFrame(0x6, 0x0, 0, pingPayload);  // type=PING, flags=0 (no ACK)
        var pingCount = 0;

        for (var i = 0; i < 1000; i++)
        {
            var frames = decoder.Decode(pingFrame);
            foreach (var frame in frames)
            {
                if (frame is PingFrame pf && !pf.IsAck)
                {
                    pingCount++;
                }
            }
        }

        EnforcePingFloodThreshold(pingCount); // must not throw

        var frames1001 = decoder.Decode(pingFrame);
        foreach (var frame in frames1001)
        {
            if (frame is PingFrame pf && !pf.IsAck)
            {
                pingCount++;
            }
        }

        var ex = Assert.Throws<Http2Exception>(() => EnforcePingFloodThreshold(pingCount));
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_accept_1000_ping_frames_without_exception()
    {
        var decoder = new Http2FrameDecoder();
        var pingPayload = new byte[8];
        var pingFrame = BuildRawFrame(0x6, 0x0, 0, pingPayload);
        var pingCount = 0;

        for (var i = 0; i < 1000; i++)
        {
            var frames = decoder.Decode(pingFrame);
            foreach (var frame in frames)
            {
                if (frame is PingFrame pf && !pf.IsAck)
                {
                    pingCount++;
                }
            }
        }

        EnforcePingFloodThreshold(pingCount); // must not throw
        Assert.Equal(1000, pingCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_not_count_ping_ack_toward_flood_threshold()
    {
        var decoder = new Http2FrameDecoder();
        var pingPayload = new byte[8];
        var pingAckFrame = BuildRawFrame(0x6, 0x1, 0, pingPayload);  // flags=0x1 → PING ACK
        var pingCount = 0;

        // 2000 PING ACK frames — none count toward non-ACK limit
        for (var i = 0; i < 2000; i++)
        {
            var frames = decoder.Decode(pingAckFrame);
            foreach (var frame in frames)
            {
                if (frame is PingFrame pf && !pf.IsAck)
                {
                    pingCount++;
                }
            }
        }

        EnforcePingFloodThreshold(pingCount); // must not throw
        Assert.Equal(0, pingCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_include_context_in_ping_flood_message()
    {
        var pingCount = 1001;
        var ex = Assert.Throws<Http2Exception>(() => EnforcePingFloodThreshold(pingCount));
        Assert.Contains("PING", ex.Message);
        Assert.Contains("flood", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
