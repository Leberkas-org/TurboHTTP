using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests the HTTP/3 frame encoder stage per RFC 9114 §7.
/// Verifies that DATA, HEADERS, SETTINGS, GOAWAY, and other frame types
/// are correctly serialised to the QUIC variable-length integer wire format.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30EncoderStage"/>.
/// RFC 9114 §7.1: HTTP/3 frame format uses QUIC variable-length integer encoding
/// for both type and length fields (unlike HTTP/2's fixed 9-byte header).
/// </remarks>
public sealed class Http30EncoderSpec : StreamTestBase
{
    private async Task<byte[]> EncodeAsync(Http3Frame frame)
    {
        var item = await Source.Single(frame)
            .Via(Flow.FromGraph(new Http30EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = Assert.IsAssignableFrom<NetworkBuffer>(item);
        var bytes = dataItem.Span.ToArray();
        dataItem.Dispose();
        return bytes;
    }

    private async Task<List<NetworkBuffer>> EncodeMultipleAsync(params Http3Frame[] frames)
    {
        var items = await Source.From(frames)
            .Via(Flow.FromGraph(new Http30EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        return items.OfType<NetworkBuffer>().ToList();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Http30Encoder_should_encode_data_frame_with_correct_type_and_payload()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new Http3DataFrame(payload);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x00, bytes[0]); // type = DATA (0x00), 1-byte varint
        Assert.Equal(payload.Length, bytes[1]); // length = 5, 1-byte varint
        Assert.Equal(payload, bytes[2..]); // payload follows
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public async Task Http30Encoder_should_encode_headers_frame_with_correct_type_and_header_block()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x84 };
        var frame = new Http3HeadersFrame(headerBlock);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x01, bytes[0]); // type = HEADERS (0x01)
        Assert.Equal(headerBlock.Length, bytes[1]); // length
        Assert.Equal(headerBlock, bytes[2..]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30Encoder_should_encode_settings_frame_with_parameter_pairs()
    {
        var parameters = new List<(long, long)> { (0x06, 4096) };
        var frame = new Http3SettingsFrame(parameters);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x04, bytes[0]); // type = SETTINGS (0x04)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public async Task Http30Encoder_should_encode_goaway_frame_with_stream_id()
    {
        var frame = new Http3GoAwayFrame(4);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x06, bytes[0]); // type = GOAWAY (0x06)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public async Task Http30Encoder_should_encode_max_push_id_frame_with_push_id()
    {
        var frame = new Http3MaxPushIdFrame(10);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x0d, bytes[0]); // type = MAX_PUSH_ID (0x0d)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30Encoder_should_encode_cancel_push_frame_with_push_id()
    {
        var frame = new Http3CancelPushFrame(7);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x03, bytes[0]); // type = CANCEL_PUSH (0x03)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public async Task Http30Encoder_should_encode_push_promise_frame_with_push_id_and_header_block()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 };
        var frame = new Http3PushPromiseFrame(1, headerBlock);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x05, bytes[0]); // type = PUSH_PROMISE (0x05)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Encoder_should_encode_empty_data_frame_with_zero_length_payload()
    {
        var frame = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x00, bytes[0]); // type = DATA
        Assert.Equal(0x00, bytes[1]); // length = 0
        Assert.Equal(2, bytes.Length); // just the 2-byte prefix
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Encoder_should_encode_multiple_frames_independently()
    {
        var headers = new Http3HeadersFrame(new byte[] { 0x82 });
        var data = new Http3DataFrame(new byte[] { 0x01, 0x02 });

        var items = await EncodeMultipleAsync(headers, data);

        Assert.Equal(2, items.Count);
        Assert.Equal(headers.SerializedSize, items[0].Length);
        Assert.Equal(data.SerializedSize, items[1].Length);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Encoder_should_match_direct_serialize_when_encoded_via_stage()
    {
        var frame = new Http3DataFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        var expected = frame.Serialize();

        var bytes = await EncodeAsync(frame);

        Assert.Equal(expected, bytes);
    }
}
