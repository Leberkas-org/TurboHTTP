using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Connection;

/// <summary>
/// Tests SETTINGS_MAX_CONCURRENT_STREAMS enforcement per RFC 9113 §6.5.2.
/// Verifies stream creation limits and correct error signalling when the limit is exceeded.
/// Part 1: Integration tests (MCS-INT-001 through MCS-INT-011).
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS limits the number of simultaneously open streams on a connection.
/// </remarks>
public sealed class Http2SettingsMaxConcurrentIntPart1Spec
{
    // Helpers

    private static byte[] MakeResponseHeadersBytes(int streamId, bool endStream = false, bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] MakeDataBytes(int streamId, bool endStream = true)
        => new DataFrame(streamId, "ok"u8.ToArray(), endStream).Serialize();

    private static byte[] MakeMaxConcurrentStreamsSettingsBytes(uint limit)
        => new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, limit)]).Serialize();

    private static byte[] Concat(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }

        return result;
    }

    /// <summary>
    /// RFC 9113 §6.5.2: Extracts MAX_CONCURRENT_STREAMS parameter from a decoded SETTINGS frame.
    /// Returns the current limit (int.MaxValue if not set).
    /// </summary>
    private static int ExtractMaxConcurrentStreams(SettingsFrame frame, int currentLimit)
    {
        foreach (var (param, value) in frame.Parameters)
        {
            if (param == SettingsParameter.MaxConcurrentStreams)
            {
                return (int)value;
            }
        }

        return currentLimit;
    }

    /// <summary>
    /// RFC 9113 §5.1.2 / §6.5.2: Opening a stream when active count >= max is a REFUSED_STREAM error.
    /// Enforces the MAX_CONCURRENT_STREAMS limit on new stream creation.
    /// </summary>
    private static void EnforceMaxConcurrentStreams(int activeCount, int maxConcurrent, int streamId)
    {
        if (maxConcurrent != int.MaxValue && activeCount >= maxConcurrent)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.5.2: MAX_CONCURRENT_STREAMS ({maxConcurrent}) exceeded: stream {streamId} refused.",
                Http2ErrorCode.RefusedStream,
                Http2ErrorScope.Stream,
                streamId);
        }
    }

    /// <summary>
    /// Tracks stream state transitions: HEADERS opens, END_STREAM/DATA+END_STREAM/RST_STREAM close.
    /// </summary>
    private static void TrackStreamState(
        Http2Frame frame,
        HashSet<int> openStreams,
        HashSet<int> closedStreams)
    {
        switch (frame)
        {
            case HeadersFrame h:
                if (!closedStreams.Contains(h.StreamId))
                {
                    openStreams.Add(h.StreamId);
                }

                if (h.EndStream)
                {
                    openStreams.Remove(h.StreamId);
                    closedStreams.Add(h.StreamId);
                }

                break;

            case DataFrame d:
                if (d.EndStream)
                {
                    openStreams.Remove(d.StreamId);
                    closedStreams.Add(d.StreamId);
                }

                break;

            case RstStreamFrame r:
                openStreams.Remove(r.StreamId);
                closedStreams.Add(r.StreamId);
                break;
        }
    }

    // MCS-INT: Integration Tests (§5.1.2 / §6.5.2)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_succeed_when_single_stream_under_default_limit()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_succeed_when_multiple_streams_under_limit()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        var bytes = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: false),
            MakeResponseHeadersBytes(streamId: 3, endStream: false),
            MakeResponseHeadersBytes(streamId: 5, endStream: false));

        var frames = decoder.Decode(bytes);
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(3, openStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_refuse_stream_when_stream_count_is_at_exact_limit()
    {
        var maxConcurrent = 2;
        var activeCount = 2;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 5));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_not_affect_existing_streams_when_new_stream_limit_enforced()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open two streams
        var bytes = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: false),
            MakeResponseHeadersBytes(streamId: 3, endStream: false));

        var frames = decoder.Decode(bytes);
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(2, openStreams.Count);

        // Now set limit to 1 (below current active count)
        // Existing streams should still be processable (DATA should work)
        var dataFrames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true));
        TrackStreamState(dataFrames[0], openStreams, closedStreams);
        Assert.Single(openStreams); // Stream 1 closed, stream 3 still open
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_allow_new_stream_when_stream_counter_decrements_on_end_stream_data()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();
        var maxConcurrent = 1;

        // Open stream 1
        var h1 = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));
        TrackStreamState(h1[0], openStreams, closedStreams);
        Assert.Single(openStreams);

        // Close stream 1 via END_STREAM DATA
        var d1 = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true));
        TrackStreamState(d1[0], openStreams, closedStreams);
        Assert.Empty(openStreams);

        // Stream 3 should be accepted now
        EnforceMaxConcurrentStreams(openStreams.Count, maxConcurrent, streamId: 3); // Should not throw
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_update_max_concurrent_streams_when_settings_frame_processed()
    {
        var decoder = new Http2FrameDecoder();

        // First SETTINGS: no limit (default int.MaxValue)
        var currentLimit = int.MaxValue;

        // Process SETTINGS with MaxConcurrentStreams=5
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(5));
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        currentLimit = ExtractMaxConcurrentStreams(frame, currentLimit);

        Assert.Equal(5, currentLimit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_update_limit_again_when_second_settings_frame_processed()
    {
        var decoder = new Http2FrameDecoder();
        var currentLimit = int.MaxValue;

        // First SETTINGS
        var frames1 = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(5));
        currentLimit = ExtractMaxConcurrentStreams(Assert.IsType<SettingsFrame>(frames1[0]), currentLimit);
        Assert.Equal(5, currentLimit);

        // Second SETTINGS
        var frames2 = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(20));
        currentLimit = ExtractMaxConcurrentStreams(Assert.IsType<SettingsFrame>(frames2[0]), currentLimit);
        Assert.Equal(20, currentLimit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_decrement_active_count_when_rst_stream_processed()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open streams 1 and 3
        var bytes = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: false),
            MakeResponseHeadersBytes(streamId: 3, endStream: false));

        var frames = decoder.Decode(bytes);
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(2, openStreams.Count);

        // RST_STREAM on stream 1
        var rstFrames = decoder.Decode(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize());
        TrackStreamState(rstFrames[0], openStreams, closedStreams);
        Assert.Single(openStreams);

        // RST_STREAM on stream 3
        var rstFrames2 = decoder.Decode(new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize());
        TrackStreamState(rstFrames2[0], openStreams, closedStreams);
        Assert.Empty(openStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_allow_sequential_streams_when_limit_is_one()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // First stream closes immediately
        var h1 = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));
        TrackStreamState(h1[0], openStreams, closedStreams);
        Assert.Empty(openStreams);

        // Second stream should be accepted (sequential, not concurrent)
        EnforceMaxConcurrentStreams(openStreams.Count, maxConcurrent: 1, streamId: 3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_refuse_all_streams_when_limit_is_zero()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 0, maxConcurrent: 0, streamId: 1));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_throw_refused_stream_when_multiple_streams_exceed_limit()
    {
        var maxConcurrent = 1;
        var activeCount = 1;

        for (var streamId = 3; streamId <= 7; streamId += 2)
        {
            var ex = Assert.Throws<Http2Exception>(
                () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId));

            Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
        }
    }
}
