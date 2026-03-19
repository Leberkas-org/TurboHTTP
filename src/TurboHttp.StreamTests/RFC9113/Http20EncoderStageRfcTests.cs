using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// RFC-tagged tests for the HTTP/2 frame encoder stage per RFC 9113.
/// Verifies that all frame types are correctly serialised to their binary wire format with proper headers and flags.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20EncoderStage"/>.
/// RFC 9113 §4–§6: HTTP/2 frame serialisation, type codes, flags, and 9-byte frame header format.
/// </remarks>
public sealed class Http20EncoderStageRfcTests : StreamTestBase
{
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-001: HEADERS frame produces 9-byte header followed by HPACK payload")]
    public async Task Should_Produce9ByteHeaderPlusHpackPayload_When_EncodingHeadersFrame()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41, 0x8A };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + hpackBlock.Length, bytes.Length);
        // Payload after the 9-byte frame header must be the exact HPACK block
        Assert.Equal(hpackBlock, bytes[9..]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-001: HEADERS frame with empty header block produces exactly 9 bytes")]
    public async Task Should_ProduceExactly9Bytes_When_HeadersFrameHasEmptyHeaderBlock()
    {
        var frame = new HeadersFrame(streamId: 3, headerBlock: Array.Empty<byte>(), endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-002: DATA frame produces 9-byte header followed by body payload")]
    public async Task Should_Produce9ByteHeaderPlusBody_When_EncodingDataFrame()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + body.Length, bytes.Length);
        Assert.Equal(body, bytes[9..]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-002: DATA frame with empty body produces exactly 9 bytes")]
    public async Task Should_ProduceExactly9Bytes_When_DataFrameHasEmptyBody()
    {
        var frame = new DataFrame(streamId: 5, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-003: Length field (bytes 0-2) matches DATA payload size")]
    public async Task Should_SetLengthFieldToDataPayloadSize_When_EncodingDataFrame()
    {
        var body = new byte[42];
        Random.Shared.NextBytes(body);
        var frame = new DataFrame(streamId: 1, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-003: Length field (bytes 0-2) matches HEADERS payload size")]
    public async Task Should_SetLengthFieldToHeadersPayloadSize_When_EncodingHeadersFrame()
    {
        var hpackBlock = new byte[17];
        Random.Shared.NextBytes(hpackBlock);
        var frame = new HeadersFrame(streamId: 3, headerBlock: hpackBlock, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(hpackBlock.Length, lengthField);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-003: Length field is zero for empty payload")]
    public async Task Should_SetLengthFieldToZero_When_PayloadIsEmpty()
    {
        var frame = new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(0, lengthField);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-004: DATA frame type byte (offset 3) is 0x0")]
    public async Task Should_SetFrameTypeByteTo0x0_When_EncodingDataFrame()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[3]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-004: HEADERS frame type byte (offset 3) is 0x1")]
    public async Task Should_SetFrameTypeByteTo0x1_When_EncodingHeadersFrame()
    {
        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x01, bytes[3]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-005: Stream ID 1 encoded big-endian in bytes 5-8")]
    public async Task Should_EncodeStreamId1BigEndian_When_StreamIdIs1()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-005: Stream ID 257 encoded big-endian in bytes 5-8")]
    public async Task Should_EncodeStreamId257BigEndian_When_StreamIdIs257()
    {
        var frame = new HeadersFrame(streamId: 257, headerBlock: new byte[] { 0x82 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        // 257 = 0x00000101
        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x01, bytes[7]);
        Assert.Equal(0x01, bytes[8]);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-005: Highest bit of stream ID field is always 0 (reserved)")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0x7FFFFFFF)] // max valid stream ID
    public async Task Should_SetHighestBitToZero_When_EncodingStreamIdField(int streamId)
    {
        var frame = new DataFrame(streamId: streamId, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        // Byte 5 highest bit (0x80) must be 0 — reserved bit per RFC 9113 §4.1
        Assert.Equal(0, bytes[5] & 0x80);
    }
}
