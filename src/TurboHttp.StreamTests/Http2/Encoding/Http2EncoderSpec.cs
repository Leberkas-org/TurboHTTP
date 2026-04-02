using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http2.Encoding;

/// <summary>
/// Tests the HTTP/2 frame encoder stage per RFC 9113.
/// Verifies that HEADERS, DATA, SETTINGS, and other frame types are correctly serialised to binary wire format.
/// </summary>
[Trait("RFC", "RFC9113-4.1")]
public sealed class Http2EncoderSpec : StreamTestBase
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
        var item = await Source.Single(new List<Http2Frame> { frame })
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = Assert.IsAssignableFrom<NetworkBuffer>(item);
        var bytes = dataItem.Span.ToArray();
        dataItem.Dispose();
        return bytes;
    }

    // Encodes each frame as a separate single-element batch, preserving per-frame output semantics.
    private async Task<List<NetworkBuffer>> EncodeMultipleAsync(params Http2Frame[] frames)
    {
        var items = await Source.From(frames.Select(f => new List<Http2Frame> { f }))
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        return items.OfType<NetworkBuffer>().ToList();
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_data_item_key_when_frame_has_endpoint()
    {
        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false)
        {
            Endpoint = TestEndpoint
        };

        var items = await EncodeMultipleAsync(frame);

        Assert.Single(items);
        Assert.Equal(TestEndpoint, items[0].Key);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_propagate_captured_endpoint_when_subsequent_frames_lack_endpoint()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_apply_captured_endpoint_to_all_data_frames_when_multiple_frames_follow()
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

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_encode_9_byte_header_plus_hpack_payload_for_headers_frame()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41 };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x01, bytes[3]); // frame type = HEADERS (0x1)
        Assert.Equal(hpackBlock.Length, bytes.Length - 9); // payload is exactly the HPACK block
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_encode_9_byte_header_plus_body_for_data_frame()
    {
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.True(bytes.Length >= 9, $"Encoded frame must be at least 9 bytes, got {bytes.Length}");
        Assert.Equal(0x00, bytes[3]); // frame type = DATA (0x0)
        Assert.Equal(body, bytes[9..]); // body payload follows the 9-byte header
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_encode_stream_id_big_endian_in_bytes_5_to_8()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]); // stream ID 1 encoded big-endian
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-4.2")]
    public async Task Http2Encoder_should_set_payload_length_field_to_actual_payload_size()
    {
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var frame = new DataFrame(streamId: 3, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }
}
