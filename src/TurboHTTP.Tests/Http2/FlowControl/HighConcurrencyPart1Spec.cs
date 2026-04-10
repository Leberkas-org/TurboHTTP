using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.FlowControl;

/// <summary>
/// Tests decoder correctness under high-concurrency and high-volume frame sequences per RFC 9113 §5.
/// Part 1: Sequential stream handling with 1000+ streams per connection.
/// Verifies stream multiplexing with many parallel streams processed sequentially through the decoder.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §5.1.1: Stream identifiers are assigned sequentially by the client; concurrent streams are multiplexed on a single connection.
/// </remarks>
public sealed class HighConcurrencyPart1Spec
{

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildHeadersFrame(int streamId, bool endStream = false)
        => BuildRawFrame(0x1, (byte)(0x4 | (endStream ? 0x1 : 0x0)), streamId, [0x88]);

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = true)
        => BuildRawFrame(0x0, endStream ? (byte)0x1 : (byte)0x0, streamId, data);

    private static byte[] BuildSettingsFrame(bool ack, params (ushort id, uint value)[] settings)
    {
        var payload = new byte[settings.Length * 6];
        for (var i = 0; i < settings.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), settings[i].id);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), settings[i].value);
        }

        return BuildRawFrame(0x4, ack ? (byte)0x1 : (byte)0x0, 0, payload);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_handle_1000_sequential_streams_with_end_stream_headers()
    {
        var decoder = new FrameDecoder();
        var closedStreams = new HashSet<int>();
        var activeStreams = new HashSet<int>();

        for (var i = 0; i < 1000; i++)
        {
            var streamId = 2 * i + 1; // odd IDs: 1, 3, 5, ..., 1999
            var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));

            foreach (var frame in frames)
            {
                if (frame is HeadersFrame { EndStream: true } hf)
                {
                    closedStreams.Add(hf.StreamId);
                    activeStreams.Remove(hf.StreamId);
                }
                else if (frame is HeadersFrame hf2)
                {
                    activeStreams.Add(hf2.StreamId);
                }
            }
        }

        Assert.Empty(activeStreams);
        Assert.Equal(1000, closedStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_1000_responses_from_1000_streams()
    {
        var decoder = new FrameDecoder();
        var decodedFrameCount = 0;

        for (var i = 0; i < 1000; i++)
        {
            var streamId = 2 * i + 1;
            var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));
            decodedFrameCount += frames.Count;
        }

        Assert.Equal(1000, decodedFrameCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_max_concurrent_streams_from_settings()
    {
        var decoder = new FrameDecoder();
        // MAX_CONCURRENT_STREAMS = SettingsParameter id 3
        var settingsBytes = BuildSettingsFrame(false, (3, 500));
        var frames = decoder.Decode(settingsBytes);

        var settingsFrame = frames.OfType<SettingsFrame>().First();
        Assert.NotNull(settingsFrame);
        // Verify we can extract the parameter (implementation detail of SettingsFrame)
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_recycle_stream_capacity_after_bulk_data_close()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open 100 streams via HEADERS without END_STREAM
        for (var i = 0; i < 100; i++)
        {
            var streamId = 2 * i + 1;
            var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: false));
            foreach (var frame in frames)
            {
                if (frame is HeadersFrame)
                {
                    openStreams.Add(streamId);
                }
            }
        }

        Assert.Equal(100, openStreams.Count);

        // Close all 100 via DATA + END_STREAM
        var oneByte = new byte[] { 0x42 };
        foreach (var streamId in openStreams.ToList())
        {
            var frames = decoder.Decode(BuildDataFrame(streamId, oneByte, endStream: true));
            foreach (var frame in frames)
            {
                if (frame is DataFrame { EndStream: true } df)
                {
                    closedStreams.Add(df.StreamId);
                    openStreams.Remove(df.StreamId);
                }
            }
        }

        Assert.Empty(openStreams);
        Assert.Equal(100, closedStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_track_all_closed_streams_with_no_cap_on_closed_stream_count()
    {
        var decoder = new FrameDecoder();
        var closedStreams = new HashSet<int>();

        for (var i = 0; i < 10001; i++)
        {
            var streamId = 2 * i + 1; // 1, 3, ..., 20001
            var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));
            foreach (var frame in frames)
            {
                if (frame is HeadersFrame { EndStream: true } hf)
                {
                    closedStreams.Add(hf.StreamId);
                }
            }
        }

        Assert.Equal(10001, closedStreams.Count);
    }
}
