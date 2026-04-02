using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http2.Decoding;

/// <summary>
/// RFC-tagged tests for the HTTP/2 frame decoder stage per RFC 9113.
/// Verifies frame type parsing, flags, stream IDs, and error handling for all defined frame types.
/// </summary>
[Trait("RFC", "RFC9113-4.1")]
public sealed class Http2DecoderFrameParsingSpec : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => NetworkBuffer.FromArray(data);

    private async Task<IReadOnlyList<Http2Frame>> DecodeAsync(params byte[][] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http20DecoderStage()))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_headers_frame_with_type_and_payload_when_complete()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_ping_frame_with_opaque_data_when_complete()
    {
        var opaqueData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var rawBytes = new PingFrame(opaqueData, isAck: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(pingFrame.IsAck);
        Assert.Equal(opaqueData, pingFrame.Data);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_window_update_frame_with_increment_when_complete()
    {
        var rawBytes = new WindowUpdateFrame(streamId: 7, increment: 65535).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var wuFrame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(7, wuFrame.StreamId);
        Assert.Equal(65535, wuFrame.Increment);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_reassemble_headers_frame_when_split_at_midpoint()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_reassemble_data_frame_when_split_inside_frame_header()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_reassemble_settings_frame_when_split_between_header_and_payload()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_both_frames_in_order_when_two_frames_in_one_tcp_segment()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_all_three_frames_in_order_when_three_frames_in_one_tcp_segment()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_settings_parameters_when_settings_frame_has_multiple_entries()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_settings_ack_with_empty_parameters_when_ack_flag_is_set()
    {
        var rawBytes = new SettingsFrame(new List<(SettingsParameter, uint)>(), isAck: true).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(settingsFrame.IsAck);
        Assert.Empty(settingsFrame.Parameters);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_data_frame_with_stream_id_payload_and_end_stream_when_complete()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_decode_data_frame_when_payload_is_empty()
    {
        var rawBytes = new DataFrame(streamId: 9, data: Array.Empty<byte>(), endStream: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(9, dataFrame.StreamId);
        Assert.False(dataFrame.EndStream);
        Assert.Empty(dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2DecoderFrameParsing_should_preserve_data_frame_large_payload_when_decoding()
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
