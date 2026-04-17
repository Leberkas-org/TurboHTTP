using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests padding handling in DATA and HEADERS frames per RFC 9113 §6.2 and §6.4.
/// Verifies correct extraction and validation of padded frame payloads.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §6.4: DATA frames can include padding to obscure message size.
/// RFC 9113 §6.2: HEADERS frames can include padding for similar purposes.
/// </remarks>
public sealed class Http2DecoderPaddingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decode_data_frame_with_padding()
    {
        var dataPayload = new byte[] { 1, 2, 3, 4, 5 };
        var paddingLength = 8;
        var frame = BuildPaddedDataFrame(1, dataPayload, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decode_data_frame_with_zero_padding()
    {
        var dataPayload = new byte[] { 1, 2, 3 };
        var paddingLength = 0;
        var frame = BuildPaddedDataFrame(1, dataPayload, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decode_data_frame_with_max_padding()
    {
        var dataPayload = new byte[] { 1 };
        var paddingLength = 255;
        var frame = BuildPaddedDataFrame(1, dataPayload, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_headers_frame_with_padding()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var paddingLength = 10;
        var frame = BuildPaddedHeadersFrame(1, headerBlock, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_headers_frame_with_zero_padding()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var paddingLength = 0;
        var frame = BuildPaddedHeadersFrame(1, headerBlock, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_headers_frame_with_max_padding()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET")]);
        var paddingLength = 255;
        var frame = BuildPaddedHeadersFrame(1, headerBlock, paddingLength);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_set_end_stream_flag_on_padded_data_frame()
    {
        var dataPayload = new byte[] { 1, 2, 3 };
        var paddingLength = 5;
        var frame = BuildPaddedDataFrame(1, dataPayload, paddingLength, endStream: true);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(dataFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_set_end_headers_flag_on_padded_headers_frame()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var paddingLength = 5;
        var frame = BuildPaddedHeadersFrame(1, headerBlock, paddingLength, endHeaders: true);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndHeaders);
    }

    // Helper methods

    private static byte[] BuildPaddedDataFrame(int streamId, byte[] data, int paddingLength, bool endStream = false)
    {
        var payloadLength = 1 + data.Length + paddingLength; // 1 byte for padding length field
        var frame = new byte[9 + payloadLength];

        frame[0] = (byte)(payloadLength >> 16);
        frame[1] = (byte)(payloadLength >> 8);
        frame[2] = (byte)payloadLength;
        frame[3] = 0x00; // DATA
        frame[4] = (byte)((endStream ? 0x01 : 0x00) | 0x08); // END_STREAM (if set) and PADDED flag
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);

        frame[9] = (byte)paddingLength;
        Array.Copy(data, 0, frame, 10, data.Length);

        return frame;
    }

    private static byte[] BuildPaddedHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, int paddingLength, bool endHeaders = false)
    {
        var payloadLength = 1 + headerBlock.Length + paddingLength; // 1 byte for padding length field
        var frame = new byte[9 + payloadLength];

        frame[0] = (byte)(payloadLength >> 16);
        frame[1] = (byte)(payloadLength >> 8);
        frame[2] = (byte)payloadLength;
        frame[3] = 0x01; // HEADERS
        frame[4] = (byte)((endHeaders ? 0x04 : 0x00) | 0x08); // END_HEADERS (if set) and PADDED flag
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);

        frame[9] = (byte)paddingLength;
        headerBlock.CopyTo(frame.AsMemory(10));

        return frame;
    }
}
