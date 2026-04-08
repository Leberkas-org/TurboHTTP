using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http2.Encoding;

/// <summary>
/// Tests for HTTP/2 frame encoder serialisation — all frame types, frame header format, and flag encoding.
/// </summary>
[Trait("RFC", "RFC9113-4.1")]
public sealed class Http2EncoderFrameSerializationSpec : StreamTestBase
{
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

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_produce_9_byte_header_plus_hpack_payload_for_headers_frame()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41, 0x8A };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + hpackBlock.Length, bytes.Length);
        Assert.Equal(hpackBlock, bytes[9..]);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_produce_exactly_9_bytes_when_headers_frame_has_empty_header_block()
    {
        var frame = new HeadersFrame(streamId: 3, headerBlock: Array.Empty<byte>(), endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_produce_9_byte_header_plus_body_for_data_frame()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + body.Length, bytes.Length);
        Assert.Equal(body, bytes[9..]);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_produce_exactly_9_bytes_when_data_frame_has_empty_body()
    {
        var frame = new DataFrame(streamId: 5, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_length_field_to_data_payload_size()
    {
        var body = new byte[42];
        Random.Shared.NextBytes(body);
        var frame = new DataFrame(streamId: 1, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_length_field_to_headers_payload_size()
    {
        var hpackBlock = new byte[17];
        Random.Shared.NextBytes(hpackBlock);
        var frame = new HeadersFrame(streamId: 3, headerBlock: hpackBlock, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(hpackBlock.Length, lengthField);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_length_field_to_zero_when_payload_is_empty()
    {
        var frame = new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(0, lengthField);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_frame_type_byte_to_0x0_for_data_frame()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[3]);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_set_frame_type_byte_to_0x1_for_headers_frame()
    {
        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x01, bytes[3]);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_encode_stream_id_1_big_endian_when_stream_id_is_1()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2Encoder_should_encode_stream_id_257_big_endian_when_stream_id_is_257()
    {
        var frame = new HeadersFrame(streamId: 257, headerBlock: new byte[] { 0x82 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        // 257 = 0x00000101
        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x01, bytes[7]);
        Assert.Equal(0x01, bytes[8]);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0x7FFFFFFF)] // max valid stream ID
    public async Task Http2Encoder_should_set_highest_bit_to_zero_when_encoding_stream_id_field(int streamId)
    {
        var frame = new DataFrame(streamId: streamId, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        // Byte 5 highest bit (0x80) must be 0 — reserved bit per RFC 9113 §4.1
        Assert.Equal(0, bytes[5] & 0x80);
    }
}
