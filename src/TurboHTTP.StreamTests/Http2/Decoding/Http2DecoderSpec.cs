using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http2.Decoding;

/// <summary>
/// Tests the HTTP/2 frame decoder stage per RFC 9113.
/// Verifies that binary-encoded HTTP/2 frames are correctly parsed from byte streams including partial frames.
/// </summary>
[Trait("RFC", "RFC9113-4.1")]
public sealed class Http2DecoderSpec : StreamTestBase
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
    public async Task Http2Decoder_should_decode_single_complete_frame_when_full_frame_arrives()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var rawBytes = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: false, endHeaders: true)
            .Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headersFrame.StreamId);
        Assert.Equal(hpackBlock, headersFrame.HeaderBlockFragment.ToArray());
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Decoder_should_reassemble_frame_when_split_across_two_tcp_chunks()
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
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Decoder_should_decode_both_frames_when_two_frames_arrive_in_one_tcp_chunk()
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
    public async Task Http2Decoder_should_decode_settings_frame_when_on_stream_0()
    {
        var parameters = new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.HeaderTableSize, 4096u)
        };
        var rawBytes = new SettingsFrame(parameters, isAck: false).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(0, settingsFrame.StreamId);
        Assert.Single(settingsFrame.Parameters);
        Assert.Equal(SettingsParameter.HeaderTableSize, settingsFrame.Parameters[0].Item1);
        Assert.Equal(4096u, settingsFrame.Parameters[0].Item2);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Decoder_should_decode_data_frame_with_stream_id_and_payload_when_complete_frame_arrives()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var rawBytes = new DataFrame(streamId: 5, data: body, endStream: true).Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(5, dataFrame.StreamId);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }
}
