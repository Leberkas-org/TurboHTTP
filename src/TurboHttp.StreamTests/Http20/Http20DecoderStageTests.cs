using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20DecoderStageTests : StreamTestBase
{
    private static (IMemoryOwner<byte>, int) Chunk(byte[] data)
        => (new SimpleMemoryOwner(data), data.Length);

    private async Task<IReadOnlyList<Http2Frame>> DecodeAsync(params byte[][] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http20DecoderStage()))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: Single complete frame decoded correctly")]
    public async Task ST_20_FDEC_001_Single_Complete_Frame_Decoded()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: Frame split across two TCP chunks reassembled")]
    public async Task ST_20_FDEC_002_Frame_Split_Across_Two_Chunks_Reassembled()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: Two frames in one TCP chunk each decoded")]
    public async Task ST_20_FDEC_003_Two_Frames_In_One_Chunk_Decoded()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: SETTINGS frame (stream 0) decoded")]
    public async Task ST_20_FDEC_004_Settings_Frame_Decoded()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: DATA frame decoded with correct stream ID and payload")]
    public async Task ST_20_FDEC_005_Data_Frame_Decoded_With_StreamId_And_Payload()
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
