using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

public sealed class Http20DecoderStageRfcTests : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => new DataItem(new SimpleMemoryOwner(data), data.Length) { Key = RequestEndpoint.Default };

    private async Task<IReadOnlyList<Http2Frame>> DecodeAsync(params byte[][] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http20DecoderStage()))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    // ─── 20D-RFC-001: Complete frame → correctly decoded ─────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-001: Complete HEADERS frame decoded with correct type and payload")]
    public async Task Complete_Headers_Frame_Decoded_Correctly()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var rawBytes = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: false, endHeaders: true)
            .Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headersFrame.StreamId);
        Assert.True(headersFrame.EndHeaders);
        Assert.False(headersFrame.EndStream);
        Assert.Equal(hpackBlock, headersFrame.HeaderBlockFragment.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-001: Complete PING frame decoded with correct opaque data")]
    public async Task Complete_Ping_Frame_Decoded_Correctly()
    {
        var opaqueData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var rawBytes = new PingFrame(opaqueData, isAck: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(pingFrame.IsAck);
        Assert.Equal(opaqueData, pingFrame.Data);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-001: Complete WINDOW_UPDATE frame decoded with correct increment")]
    public async Task Complete_WindowUpdate_Frame_Decoded_Correctly()
    {
        var rawBytes = new WindowUpdateFrame(streamId: 7, increment: 65535).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var wuFrame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(7, wuFrame.StreamId);
        Assert.Equal(65535, wuFrame.Increment);
    }

    // ─── 20D-RFC-002: Frame split across 2 TCP segments → reassembled ────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-002: HEADERS frame split at midpoint reassembled correctly")]
    public async Task Headers_Frame_Split_At_Midpoint_Reassembled()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var rawBytes = new HeadersFrame(streamId: 3, headerBlock: hpackBlock, endHeaders: true).Serialize();

        var splitAt = rawBytes.Length / 2;
        var chunk1 = rawBytes[..splitAt];
        var chunk2 = rawBytes[splitAt..];

        var frames = await DecodeAsync(chunk1, chunk2);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(3, headersFrame.StreamId);
        Assert.Equal(hpackBlock, headersFrame.HeaderBlockFragment.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-002: DATA frame split inside 9-byte header reassembled correctly")]
    public async Task Data_Frame_Split_Inside_Header_Reassembled()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var rawBytes = new DataFrame(streamId: 5, data: body, endStream: true).Serialize();

        // Split at byte 4 — inside the 9-byte frame header
        var chunk1 = rawBytes[..4];
        var chunk2 = rawBytes[4..];

        var frames = await DecodeAsync(chunk1, chunk2);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(5, dataFrame.StreamId);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-002: SETTINGS frame split between header and payload reassembled")]
    public async Task Settings_Frame_Split_Between_Header_And_Payload_Reassembled()
    {
        var parameters = new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 32768u)
        };
        var rawBytes = new SettingsFrame(parameters, isAck: false).Serialize();

        // Split exactly after the 9-byte header
        var chunk1 = rawBytes[..9];
        var chunk2 = rawBytes[9..];

        var frames = await DecodeAsync(chunk1, chunk2);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(2, settingsFrame.Parameters.Count);
    }

    // ─── 20D-RFC-003: 2 frames in one TCP segment → both decoded ─────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-003: Two frames in single TCP segment both decoded in order")]
    public async Task Two_Frames_In_Single_Segment_Both_Decoded()
    {
        var settingsBytes = new SettingsFrame(new List<(SettingsParameter, uint)>(), isAck: true).Serialize();
        var headersBytes = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endHeaders: true)
            .Serialize();

        var combined = new byte[settingsBytes.Length + headersBytes.Length];
        settingsBytes.CopyTo(combined, 0);
        headersBytes.CopyTo(combined, settingsBytes.Length);

        var frames = await DecodeAsync(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<SettingsFrame>(frames[0]);
        Assert.IsType<HeadersFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-003: Three frames in single TCP segment all decoded in order")]
    public async Task Three_Frames_In_Single_Segment_All_Decoded()
    {
        var pingBytes = new PingFrame(new byte[8], isAck: false).Serialize();
        var dataBytes = new DataFrame(streamId: 1, data: new byte[] { 0xAB }, endStream: false).Serialize();
        var wuBytes = new WindowUpdateFrame(streamId: 0, increment: 1024).Serialize();

        var combined = new byte[pingBytes.Length + dataBytes.Length + wuBytes.Length];
        pingBytes.CopyTo(combined, 0);
        dataBytes.CopyTo(combined, pingBytes.Length);
        wuBytes.CopyTo(combined, pingBytes.Length + dataBytes.Length);

        var frames = await DecodeAsync(combined);

        Assert.Equal(3, frames.Count);
        Assert.IsType<PingFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
        Assert.IsType<WindowUpdateFrame>(frames[2]);
    }

    // ─── 20D-RFC-004: SETTINGS frame (Type 0x4) → flags and parameters correct ──

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-004: SETTINGS frame parameters decoded with correct keys and values")]
    public async Task Settings_Frame_Parameters_Decoded_Correctly()
    {
        var parameters = new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.MaxConcurrentStreams, 128u),
            (SettingsParameter.InitialWindowSize, 65535u)
        };
        var rawBytes = new SettingsFrame(parameters, isAck: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(0, settingsFrame.StreamId);
        Assert.False(settingsFrame.IsAck);
        Assert.Equal(3, settingsFrame.Parameters.Count);
        Assert.Equal(SettingsParameter.HeaderTableSize, settingsFrame.Parameters[0].Item1);
        Assert.Equal(4096u, settingsFrame.Parameters[0].Item2);
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, settingsFrame.Parameters[1].Item1);
        Assert.Equal(128u, settingsFrame.Parameters[1].Item2);
        Assert.Equal(SettingsParameter.InitialWindowSize, settingsFrame.Parameters[2].Item1);
        Assert.Equal(65535u, settingsFrame.Parameters[2].Item2);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-004: SETTINGS ACK frame decoded with empty parameters and ACK flag")]
    public async Task Settings_Ack_Frame_Decoded_Correctly()
    {
        var rawBytes = new SettingsFrame(new List<(SettingsParameter, uint)>(), isAck: true).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(settingsFrame.IsAck);
        Assert.Empty(settingsFrame.Parameters);
    }

    // ─── 20D-RFC-005: DATA frame → stream ID and payload correct ─────────────────

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC9113-4.1-20D-RFC-005: DATA frame decoded with correct stream ID, payload, and END_STREAM flag")]
    public async Task Data_Frame_Decoded_With_StreamId_Payload_And_EndStream()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var rawBytes = new DataFrame(streamId: 5, data: body, endStream: true).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(5, dataFrame.StreamId);
        Assert.True(dataFrame.EndStream);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20D-RFC-005: DATA frame with empty payload decoded correctly")]
    public async Task Data_Frame_Empty_Payload_Decoded_Correctly()
    {
        var rawBytes = new DataFrame(streamId: 9, data: Array.Empty<byte>(), endStream: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(9, dataFrame.StreamId);
        Assert.False(dataFrame.EndStream);
        Assert.Empty(dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-4.1-20D-RFC-005: Large DATA frame payload preserved through decode")]
    public async Task Data_Frame_Large_Payload_Preserved()
    {
        var body = new byte[1024];
        Random.Shared.NextBytes(body);
        var rawBytes = new DataFrame(streamId: 11, data: body, endStream: true).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(11, dataFrame.StreamId);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }
}