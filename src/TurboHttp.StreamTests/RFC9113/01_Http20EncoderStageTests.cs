using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the HTTP/2 frame encoder stage per RFC 9113.
/// Verifies that HEADERS, DATA, SETTINGS, and other frame types are correctly serialised to binary wire format.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20EncoderStage"/>.
/// RFC 9113 §4: HTTP/2 frame format, type-specific payload layout, and serialisation correctness.
/// </remarks>
public sealed class Http20EncoderStageTests : StreamTestBase
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "example.com",
        Port = 443,
        Version = HttpVersion.Version20
    };

    private async Task<byte[]> EncodeAsync(Http2Frame frame)
    {
        var item = await Source.Single(frame)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = (DataItem)item;
        var bytes = dataItem.Memory.Memory.Span[..dataItem.Length].ToArray();
        dataItem.Memory.Dispose();
        return bytes;
    }

    private async Task<List<DataItem>> EncodeMultipleAsync(params Http2Frame[] frames)
    {
        var items = await Source.From(frames)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        return items.Cast<DataItem>().ToList();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-005: DataItem Key is set from frame Endpoint")]
    public async Task Should_SetDataItemKeyFromEndpoint_When_FrameHasEndpoint()
    {
        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false)
        {
            Endpoint = TestEndpoint
        };

        var items = await EncodeMultipleAsync(frame);

        Assert.Single(items);
        Assert.Equal(TestEndpoint, items[0].Key);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-006: Captured endpoint propagates to subsequent frames without Endpoint")]
    public async Task Should_PropagateCapturedEndpoint_When_SubsequentFramesLackEndpoint()
    {
        var headers = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false)
        {
            Endpoint = TestEndpoint
        };
        var data = new DataFrame(streamId: 1, data: new byte[] { 0x01, 0x02 }, endStream: true);
        // data.Endpoint is null — should inherit captured endpoint

        var items = await EncodeMultipleAsync(headers, data);

        Assert.Equal(2, items.Count);
        Assert.Equal(TestEndpoint, items[0].Key);
        Assert.Equal(TestEndpoint, items[1].Key);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-007: Multiple data frames all receive captured endpoint")]
    public async Task Should_ApplyCapturedEndpointToAllDataFrames_When_MultipleFramesFollow()
    {
        var headers = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false)
        {
            Endpoint = TestEndpoint
        };
        var data1 = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: false);
        var data2 = new DataFrame(streamId: 1, data: new byte[] { 0x02 }, endStream: true);

        var items = await EncodeMultipleAsync(headers, data1, data2);

        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(TestEndpoint, item.Key));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-001: HEADERS frame has 9-byte header + HPACK payload")]
    public async Task Should_Encode9ByteHeaderPlusHpackPayload_When_EncodingHeadersFrame()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x01, bytes[3]); // frame type = HEADERS (0x1)
        Assert.Equal(hpackBlock.Length, bytes.Length - 9); // payload is exactly the HPACK block
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-002: DATA frame has 9-byte header + body payload")]
    public async Task Should_Encode9ByteHeaderPlusBody_When_EncodingDataFrame()
    {
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x00, bytes[3]); // frame type = DATA (0x0)
        Assert.Equal(body, bytes[9..]); // body payload follows the 9-byte header
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-003: Stream ID field is encoded big-endian in bytes 5–8")]
    public async Task Should_EncodeStreamIdBigEndian_When_WritingToBytes5To8()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]); // stream ID 1 encoded big-endian
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.2-20EN-004: Payload length field matches actual payload size")]
    public async Task Should_SetPayloadLengthFieldToActualPayloadSize_When_EncodingDataFrame()
    {
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var frame = new DataFrame(streamId: 3, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }
}
