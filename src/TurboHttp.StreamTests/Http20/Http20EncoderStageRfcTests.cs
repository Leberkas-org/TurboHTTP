using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

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

    // ─── 20E-RFC-001: HEADERS frame → 9-byte header + HPACK payload ───────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-001: HEADERS frame produces 9-byte header followed by HPACK payload")]
    public async Task Headers_Frame_Produces_9Byte_Header_Plus_Hpack_Payload()
    {
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86, 0x41, 0x8A };
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpackBlock, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + hpackBlock.Length, bytes.Length);
        // Payload after the 9-byte frame header must be the exact HPACK block
        Assert.Equal(hpackBlock, bytes[9..]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-001: HEADERS frame with empty header block produces exactly 9 bytes")]
    public async Task Headers_Frame_Empty_Block_Produces_Exactly_9_Bytes()
    {
        var frame = new HeadersFrame(streamId: 3, headerBlock: Array.Empty<byte>(), endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    // ─── 20E-RFC-002: DATA frame → 9-byte header + body payload ───────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-002: DATA frame produces 9-byte header followed by body payload")]
    public async Task Data_Frame_Produces_9Byte_Header_Plus_Body()
    {
        var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9 + body.Length, bytes.Length);
        Assert.Equal(body, bytes[9..]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-002: DATA frame with empty body produces exactly 9 bytes")]
    public async Task Data_Frame_Empty_Body_Produces_Exactly_9_Bytes()
    {
        var frame = new DataFrame(streamId: 5, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(9, bytes.Length);
    }

    // ─── 20E-RFC-003: Frame-length field (3 bytes) → correct payload length ───

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-003: Length field (bytes 0-2) matches DATA payload size")]
    public async Task Length_Field_Matches_Data_Payload_Size()
    {
        var body = new byte[42];
        Random.Shared.NextBytes(body);
        var frame = new DataFrame(streamId: 1, data: body, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(body.Length, lengthField);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-003: Length field (bytes 0-2) matches HEADERS payload size")]
    public async Task Length_Field_Matches_Headers_Payload_Size()
    {
        var hpackBlock = new byte[17];
        Random.Shared.NextBytes(hpackBlock);
        var frame = new HeadersFrame(streamId: 3, headerBlock: hpackBlock, endStream: false);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(hpackBlock.Length, lengthField);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-003: Length field is zero for empty payload")]
    public async Task Length_Field_Is_Zero_For_Empty_Payload()
    {
        var frame = new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true);

        var bytes = await EncodeAsync(frame);

        var lengthField = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        Assert.Equal(0, lengthField);
    }

    // ─── 20E-RFC-004: Frame type (1 byte): 0x0=DATA, 0x1=HEADERS ─────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-004: DATA frame type byte (offset 3) is 0x0")]
    public async Task Data_Frame_Type_Byte_Is_0x0()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0xFF }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[3]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-004: HEADERS frame type byte (offset 3) is 0x1")]
    public async Task Headers_Frame_Type_Byte_Is_0x1()
    {
        var frame = new HeadersFrame(streamId: 1, headerBlock: new byte[] { 0x82 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x01, bytes[3]);
    }

    // ─── 20E-RFC-005: Stream ID in big-endian (4 bytes), highest bit = 0 ──────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20EN-005: Stream ID 1 encoded big-endian in bytes 5-8")]
    public async Task StreamId_1_Encoded_BigEndian()
    {
        var frame = new DataFrame(streamId: 1, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
        Assert.Equal(0x01, bytes[8]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-4.1-20E-RFC-005: Stream ID 257 encoded big-endian in bytes 5-8")]
    public async Task StreamId_257_Encoded_BigEndian()
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
    public async Task StreamId_Highest_Bit_Is_Always_Zero(int streamId)
    {
        var frame = new DataFrame(streamId: streamId, data: new byte[] { 0x01 }, endStream: false);

        var bytes = await EncodeAsync(frame);

        // Byte 5 highest bit (0x80) must be 0 — reserved bit per RFC 9113 §4.1
        Assert.Equal(0, bytes[5] & 0x80);
    }
}
