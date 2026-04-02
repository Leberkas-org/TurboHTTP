using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests the HTTP/3 frame decoder stage per RFC 9114 §7.
/// Verifies that binary-encoded HTTP/3 frames are correctly parsed from byte streams
/// including partial frames and unknown frame type handling.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30DecoderStage"/>.
/// RFC 9114 §7.1: HTTP/3 frame format uses QUIC variable-length integer encoding
/// for both type and length fields.
/// </remarks>
public sealed class Http30DecoderSpec : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => NetworkBuffer.FromArray(data);

    private async Task<IReadOnlyList<Http3Frame>> DecodeAsync(params byte[][] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http30DecoderStage()))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Http30Decoder_should_decode_data_frame_when_complete_frame_arrives()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new Http3DataFrame(payload);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(payload, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public async Task Http30Decoder_should_decode_headers_frame_when_complete_frame_arrives()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x84 };
        var frame = new Http3HeadersFrame(headerBlock);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.Equal(headerBlock, headersFrame.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30Decoder_should_decode_settings_frame_with_parameter_pairs()
    {
        var parameters = new List<(long, long)> { (0x06, 4096) };
        var frame = new Http3SettingsFrame(parameters);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<Http3SettingsFrame>(frames[0]);
        Assert.Single(settingsFrame.Parameters);
        Assert.Equal(0x06, settingsFrame.Parameters[0].Identifier);
        Assert.Equal(4096, settingsFrame.Parameters[0].Value);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.6")]
    public async Task Http30Decoder_should_decode_goaway_frame_with_stream_id()
    {
        var frame = new Http3GoAwayFrame(4);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var goAwayFrame = Assert.IsType<Http3GoAwayFrame>(frames[0]);
        Assert.Equal(4, goAwayFrame.StreamId);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.7")]
    public async Task Http30Decoder_should_decode_max_push_id_frame_with_push_id()
    {
        var frame = new Http3MaxPushIdFrame(10);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var maxPushIdFrame = Assert.IsType<Http3MaxPushIdFrame>(frames[0]);
        Assert.Equal(10, maxPushIdFrame.PushId);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.3")]
    public async Task Http30Decoder_should_decode_cancel_push_frame_with_push_id()
    {
        var frame = new Http3CancelPushFrame(7);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var cancelPushFrame = Assert.IsType<Http3CancelPushFrame>(frames[0]);
        Assert.Equal(7, cancelPushFrame.PushId);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.5")]
    public async Task Http30Decoder_should_decode_push_promise_frame_with_push_id_and_header_block()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 };
        var frame = new Http3PushPromiseFrame(1, headerBlock);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var pushPromiseFrame = Assert.IsType<Http3PushPromiseFrame>(frames[0]);
        Assert.Equal(1, pushPromiseFrame.PushId);
        Assert.Equal(headerBlock, pushPromiseFrame.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Decoder_should_decode_empty_data_frame_with_zero_length_payload()
    {
        var frame = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Empty(dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Decoder_should_decode_both_frames_when_two_frames_arrive_in_one_chunk()
    {
        var headersBytes = new Http3HeadersFrame(new byte[] { 0x82 }).Serialize();
        var dataBytes = new Http3DataFrame(new byte[] { 0x01, 0x02 }).Serialize();

        var combined = new byte[headersBytes.Length + dataBytes.Length];
        headersBytes.CopyTo(combined, 0);
        dataBytes.CopyTo(combined, headersBytes.Length);

        var frames = await DecodeAsync(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.IsType<Http3DataFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Decoder_should_reassemble_frame_when_split_across_two_chunks()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var rawBytes = new Http3DataFrame(payload).Serialize();

        var splitAt = rawBytes.Length / 2;
        var chunk1 = rawBytes[..splitAt];
        var chunk2 = rawBytes[splitAt..];

        var frames = await DecodeAsync(chunk1, chunk2);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(payload, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.1")]
    public async Task Http30Decoder_should_preserve_content_when_round_tripping()
    {
        var original = new Http3DataFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        var rawBytes = original.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(original.Data.ToArray(), decoded.Data.ToArray());
    }
}
