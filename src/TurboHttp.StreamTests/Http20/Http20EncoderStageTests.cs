using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20EncoderStageTests : StreamTestBase
{
    private async Task<byte[]> EncodeAsync(Http2Frame frame)
    {
        var chunk = await Source.Single(frame)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.First<(IMemoryOwner<byte>, int)>(), Materializer);

        var bytes = chunk.Item1.Memory.Span[..chunk.Item2].ToArray();
        chunk.Item1.Dispose();
        return bytes;
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: HEADERS frame has 9-byte header + HPACK payload")]
    public async Task ST_20_FENC_001_Headers_Frame_Has_9Byte_Header_And_Payload()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x01, bytes[3]); // frame type = HEADERS (0x1)
        Assert.Equal(hpackBlock.Length, bytes.Length - 9); // payload is exactly the HPACK block
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: DATA frame has 9-byte header + body payload")]
    public async Task ST_20_FENC_002_Data_Frame_Has_9Byte_Header_And_Body()
    {
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x00, bytes[3]); // frame type = DATA (0x0)
        Assert.Equal(body, bytes[9..]); // body payload follows the 9-byte header
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: Stream ID field is encoded big-endian in bytes 5–8")]
    public async Task ST_20_FENC_003_StreamId_Encoded_BigEndian_In_Bytes5To8()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]); // stream ID 1 encoded big-endian
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.2: Payload length field matches actual payload size")]
    public async Task ST_20_FENC_004_Payload_Length_Field_Matches_Actual_Payload_Size()
    {
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var frame = new DataFrame(streamId: 3, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }
}
