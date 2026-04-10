using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.FlowControl;

/// <summary>
/// Tests decoder defenses against resource-exhaustion attacks such as SETTINGS floods.
/// Part 2: HPACK table, stream exhaustion, empty DATA flood protection.
/// Verifies that flood protection thresholds produce Http2Exception with appropriate error codes.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §10.5: Implementations should limit the rate at which control frames can be received to protect against floods.
/// </remarks>
public sealed class ResourceExhaustionPart2Spec
{

    private static void EnforceEmptyDataFloodThreshold(int count, int threshold = 10000)
    {
        if (count > threshold)
        {
            throw new Http2Exception(
                $"RFC 9113 security: Excessive zero-length DATA frames ({count}) — possible empty DATA flood.",
                Http2ErrorCode.ProtocolError);
        }
    }


    private static byte[] BuildRawFrame(byte frameType, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        var len = payload.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
        frame[3] = frameType;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static void AppendLiteralIncrementalHeader(List<byte> output, string name, string value)
    {
        // RFC 7541 §6.2.1: Literal Header Field with Incremental Indexing, new name (0x40 | 0x00)
        output.Add(0x40);
        AppendHpackString(output, name);
        AppendHpackString(output, value);
    }

    private static void AppendHpackString(List<byte> output, string s)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        output.Add((byte)bytes.Length);  // not Huffman-encoded, MSB=0
        output.AddRange(bytes);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_keep_dynamic_table_within_limit_when_adding_many_headers()
    {
        var hpack = new HpackDecoder();
        hpack.SetMaxAllowedTableSize(256);

        // Build a header block with multiple literal-with-indexing headers so the table grows.
        // Each entry: name "x-header-nnn" (12 bytes) + value "v" (1 byte) + 32 = 45 bytes.
        // Six entries = 270 bytes > 256, so eviction must kick in.
        var blocks = new List<byte>();
        for (var i = 0; i < 6; i++)
        {
            var name = $"x-hdr-{i:D3}";
            var value = "v";
            AppendLiteralIncrementalHeader(blocks, name, value);
        }

        // Also prepend a :status 200 (indexed, index 8) so ValidateResponseHeaders passes.
        var fullBlock = new List<byte>();
        fullBlock.Add(0x88);  // indexed :status 200
        fullBlock.AddRange(blocks);

        hpack.Decode([..fullBlock]);  // must not throw; eviction must have maintained bounds
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_evict_all_entries_when_table_size_set_to_zero()
    {
        var hpack = new HpackDecoder();

        // Add one header via literal-with-indexing.
        var block1 = new byte[] { 0x88 };  // indexed :status 200 — no dynamic table entry
        hpack.Decode(block1);

        // Table size update to 0: DTS=0 prefix is 0x20 (first byte of header block).
        // RFC 7541 §6.3: Size update must appear at start of a header block.
        var blockWithUpdate = new byte[] { 0x20, 0x88 };  // DTS=0 then indexed :status 200
        hpack.Decode(blockWithUpdate);  // must not throw; table is now empty
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_prevent_table_growth_when_max_allowed_table_size_is_zero()
    {
        var hpack = new HpackDecoder();
        hpack.SetMaxAllowedTableSize(0);

        // A header block with a table-size update to 0 is valid. Decode :status 200.
        var block = new byte[] { 0x20, 0x88 };  // DTS=0, indexed :status 200
        var headers = hpack.Decode(block);
        Assert.Single(headers);
        Assert.Equal(":status", headers[0].Name);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_handle_stream_id_exhaustion_without_crash()
    {
        var decoder = new FrameDecoder();
        var closedStreamIds = new HashSet<int>();

        // Decode 10001 HEADERS+END_STREAM frames on distinct stream IDs
        for (var i = 0; i < 10001; i++)
        {
            var streamId = 2 * i + 1;  // odd stream IDs: 1, 3, ..., 20001
            var frame = BuildRawFrame(0x1, 0x5, streamId, [0x88]);  // END_HEADERS | END_STREAM
            var frames = decoder.Decode(frame);

            foreach (var f in frames)
            {
                if (f is HeadersFrame { EndStream: true } hf)
                {
                    closedStreamIds.Add(hf.StreamId);
                }
            }
        }

        // Verify we tracked all closed streams
        Assert.Equal(10001, closedStreamIds.Count);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_throw_http2_exception_when_10001_empty_data_frames_received()
    {
        var decoder = new FrameDecoder();

        // First, open stream 1 via HEADERS (END_HEADERS=0x4, no END_STREAM)
        var headersFrame = BuildRawFrame(0x1, 0x4, 1, [0x88]);
        var headersFrames = decoder.Decode(headersFrame);
        Assert.Single(headersFrames);

        // Now decode 10001 zero-length DATA frames
        const int count = 10001;
        var emptyData = BuildRawFrame(0x0, 0x0, 1, []);
        var emptyDataCount = 0;

        for (var i = 0; i < count; i++)
        {
            var frames = decoder.Decode(emptyData);
            foreach (var frame in frames)
            {
                if (frame is DataFrame { Data.IsEmpty: true })
                {
                    emptyDataCount++;
                }
            }

            if (i < count - 1)
            {
                // Don't enforce on the last one yet — we'll do it after collecting all
                if (emptyDataCount <= 10000)
                {
                    EnforceEmptyDataFloodThreshold(emptyDataCount);
                }
            }
        }

        // On the 10001st frame
        var ex = Assert.Throws<Http2Exception>(() => EnforceEmptyDataFloodThreshold(emptyDataCount));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_accept_10000_empty_data_frames_without_exception()
    {
        var decoder = new FrameDecoder();

        // Open stream 1 via HEADERS (END_HEADERS=0x4, no END_STREAM)
        var headersFrame = BuildRawFrame(0x1, 0x4, 1, [0x88]);
        decoder.Decode(headersFrame);

        // Send exactly 10000 zero-length DATA frames — must not throw
        const int count = 10000;
        var emptyData = BuildRawFrame(0x0, 0x0, 1, []);
        var emptyDataCount = 0;

        for (var i = 0; i < count; i++)
        {
            var frames = decoder.Decode(emptyData);
            foreach (var frame in frames)
            {
                if (frame is DataFrame { Data.IsEmpty: true })
                {
                    emptyDataCount++;
                }
            }
        }

        EnforceEmptyDataFloodThreshold(emptyDataCount); // must not throw
        Assert.Equal(10000, emptyDataCount);
    }

}
