using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using AkkaActor = Akka.Actor;

using TurboHTTP.Server;



/// <summary>
/// Unit tests for HTTP/2 SessionManager SETTINGS and GOAWAY handling.
/// Tests frame emission for SETTINGS ACK, PING ACK, and GOAWAY processing.
/// RFC 9113 §6.5 (SETTINGS), §6.7 (PING), §6.8 (GOAWAY).
/// </summary>
public sealed class Http2SettingsGoawaySpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public List<(string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public AkkaActor.IActorRef StageActor { get; set; } = AkkaActor.ActorRefs.Nobody;

        public void OnRequest(TurboHttpContext context)
        {
        }

        public void OnOutbound(ITransportOutbound item)
        {
            Outbound.Add(item);
        }

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
            ScheduledTimers.Add((name, delay));
        }

        public void OnCancelTimer(string name)
        {
            CancelledTimers.Add(name);
        }
    }

    private static Http2ServerSessionManager CreateSessionManager(TrackingServerOps ops)
    {
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        return new Http2ServerSessionManager(encoderOptions, decoderOptions, ops);
    }

    private static byte[] BuildSettingsFrame(bool isAck = false)
    {
        var frame = new byte[9];
        frame[3] = (byte)FrameType.Settings; // 0x04
        frame[4] = isAck ? (byte)0x01 : (byte)0; // ACK flag at byte 4
        // stream ID = 0 (already zeroed)
        return frame;
    }

    private static byte[] BuildPingFrame(byte[] data, bool isAck = false)
    {
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length = 8
        frame[3] = (byte)FrameType.Ping; // 0x06
        frame[4] = isAck ? (byte)0x01 : (byte)0; // ACK flag
        // stream ID = 0 (already zeroed)
        data.AsSpan(0, Math.Min(8, data.Length)).CopyTo(frame.AsSpan(9));
        return frame;
    }

    private static byte[] BuildGoAwayFrame(int lastStreamId, uint errorCode)
    {
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length = 8
        frame[3] = (byte)FrameType.GoAway; // 0x07
        // stream ID = 0 (already zeroed)
        frame[9] = (byte)(lastStreamId >> 24);
        frame[10] = (byte)(lastStreamId >> 16);
        frame[11] = (byte)(lastStreamId >> 8);
        frame[12] = (byte)lastStreamId;
        frame[13] = (byte)(errorCode >> 24);
        frame[14] = (byte)(errorCode >> 16);
        frame[15] = (byte)(errorCode >> 8);
        frame[16] = (byte)errorCode;
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void PreStart_should_emit_settings_frame()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();

        // Should emit initial SETTINGS frame
        Assert.NotEmpty(ops.Outbound);
        var td = Assert.IsType<TransportData>(ops.Outbound[0]);

        // Verify frame type is SETTINGS (0x04) at offset 3
        Assert.Equal((byte)FrameType.Settings, td.Buffer.Span[3]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Settings_should_emit_ack()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame

        // Send SETTINGS without ACK
        var settingsFrame = BuildSettingsFrame(isAck: false);
        sm.DecodeClientData(WrapFrame(settingsFrame));

        // Should emit SETTINGS ACK
        Assert.NotEmpty(ops.Outbound);
        var td = Assert.IsType<TransportData>(ops.Outbound[0]);

        // Verify frame type is SETTINGS (0x04)
        Assert.Equal((byte)FrameType.Settings, td.Buffer.Span[3]);

        // Verify ACK flag is set (bit 0 at byte 4)
        Assert.True((td.Buffer.Span[4] & 0x01) != 0, "ACK flag should be set");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Settings_ack_should_be_ignored()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();
        ops.Outbound.Clear();

        // Send SETTINGS with ACK already set
        var settingsFrame = BuildSettingsFrame(isAck: true);
        sm.DecodeClientData(WrapFrame(settingsFrame));

        // Should not emit any response (ACK is idempotent, no echo)
        Assert.Empty(ops.Outbound);

        // Should not crash
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Ping_should_emit_ack_with_echoed_data()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();
        ops.Outbound.Clear();

        // Send PING with 8 bytes of data
        byte[] pingData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var pingFrame = BuildPingFrame(pingData, isAck: false);
        sm.DecodeClientData(WrapFrame(pingFrame));

        // Should emit PING ACK
        Assert.NotEmpty(ops.Outbound);
        var td = Assert.IsType<TransportData>(ops.Outbound[0]);

        // Verify frame type is PING (0x06)
        Assert.Equal((byte)FrameType.Ping, td.Buffer.Span[3]);

        // Verify ACK flag is set (bit 0 at byte 4)
        Assert.True((td.Buffer.Span[4] & 0x01) != 0, "ACK flag should be set");

        // Verify echoed data matches (bytes 9-16)
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(pingData[i], td.Buffer.Span[9 + i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Ping_ack_should_be_ignored()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();
        ops.Outbound.Clear();

        // Send PING with ACK already set
        byte[] pingData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var pingFrame = BuildPingFrame(pingData, isAck: true);
        sm.DecodeClientData(WrapFrame(pingFrame));

        // Should not emit any response (ACK is idempotent, no echo)
        Assert.Empty(ops.Outbound);

        // Should not crash
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void GoAway_should_not_crash()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSessionManager(ops);

        sm.PreStart();
        ops.Outbound.Clear();

        // Send GOAWAY with last stream ID = 0 and error code = 0 (NO_ERROR)
        var goAwayFrame = BuildGoAwayFrame(lastStreamId: 0, errorCode: 0);
        sm.DecodeClientData(WrapFrame(goAwayFrame));

        // Should not crash or throw
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void PreStart_should_emit_settings_with_configured_stream_window_size()
    {
        var ops = new TrackingServerOps();
        var encoderOptions = new Http2ServerEncoderOptions();
        var decoderOptions = new Http2ServerDecoderOptions();
        var customStreamWindow = 256 * 1024;
        var sessionManager = new Http2ServerSessionManager(
            encoderOptions, decoderOptions, ops,
            initialStreamWindowSize: customStreamWindow);

        sessionManager.PreStart();

        var settingsData = Assert.IsType<TransportData>(ops.Outbound[0]);
        var settingsBytes = settingsData.Buffer.Span;

        var found = false;
        var offset = 9;
        var payloadLength = (settingsBytes[0] << 16) | (settingsBytes[1] << 8) | settingsBytes[2];
        var end = 9 + payloadLength;
        while (offset + 6 <= end)
        {
            var id = (ushort)((settingsBytes[offset] << 8) | settingsBytes[offset + 1]);
            var value = (uint)((settingsBytes[offset + 2] << 24)
                              | (settingsBytes[offset + 3] << 16)
                              | (settingsBytes[offset + 4] << 8)
                              | settingsBytes[offset + 5]);
            if (id == (ushort)SettingsParameter.InitialWindowSize)
            {
                Assert.Equal((uint)customStreamWindow, value);
                found = true;
            }
            offset += 6;
        }

        Assert.True(found, "SETTINGS frame should contain INITIAL_WINDOW_SIZE");
        settingsData.Buffer.Dispose();
    }
}



