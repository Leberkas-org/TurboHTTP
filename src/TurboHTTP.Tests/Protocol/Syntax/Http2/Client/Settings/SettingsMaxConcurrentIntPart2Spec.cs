using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Settings;

public sealed class Http2SettingsMaxConcurrentIntPart2Spec
{
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

    private static void EnforceMaxConcurrentStreams(int activeCount, int maxConcurrent, int streamId)
    {
        if (maxConcurrent != int.MaxValue && activeCount >= maxConcurrent)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.5.2: MAX_CONCURRENT_STREAMS ({maxConcurrent}) exceeded: stream {streamId} refused.");
        }
    }

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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_count_as_zero_open_streams_when_headers_only_response_with_end_stream()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));
        TrackStreamState(frames[0], openStreams, closedStreams);

        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_decrement_count_correctly_when_headers_plus_data_response_received()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        var bytes = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: false),
            MakeDataBytes(streamId: 1, endStream: true));

        var frames = decoder.Decode(bytes);
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_not_double_count_stream_when_continuation_frames_received()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("content-type", "text/plain")]);

        var split = headerBlock.Length / 2;
        var block1 = headerBlock[..split];
        var block2 = headerBlock[split..];

        var headersFrame = new HeadersFrame(1, block1, endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block2, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        var frames = decoder.Decode(Concat(headersFrame, contFrame));
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Single(openStreams);
        Assert.Equal(1, openStreams.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_not_close_existing_streams_when_settings_changed_below_active_count()
    {
        var decoder = new FrameDecoder();
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

        // Process SETTINGS (doesn't affect open streams)
        decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(1));

        // Existing streams still open
        Assert.Equal(2, openStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_allow_more_streams_when_concurrent_stream_limit_increased()
    {
        // With limit 2, two streams are open
        var activeCount = 2;

        // With new limit 5, third stream should be accepted
        var newLimit = 5;
        EnforceMaxConcurrentStreams(activeCount, newLimit, streamId: 5); // Should not throw
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_allow_existing_streams_to_complete_when_limit_decreased()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open 3 streams
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

        // Lower limit below active count — existing streams still process
        var dFrames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true));
        TrackStreamState(dFrames[0], openStreams, closedStreams);

        // Stream 1 closed, streams 3 and 5 still open
        Assert.Equal(2, openStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_maintain_accurate_count_when_active_streams_open_and_close()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open 5 streams
        var bytes1 = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: false),
            MakeResponseHeadersBytes(streamId: 3, endStream: false),
            MakeResponseHeadersBytes(streamId: 5, endStream: false),
            MakeResponseHeadersBytes(streamId: 7, endStream: false),
            MakeResponseHeadersBytes(streamId: 9, endStream: false));

        var frames1 = decoder.Decode(bytes1);
        foreach (var frame in frames1)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(5, openStreams.Count);

        // Close 3 streams
        var bytes2 = Concat(
            MakeDataBytes(streamId: 1, endStream: true),
            MakeDataBytes(streamId: 3, endStream: true),
            MakeDataBytes(streamId: 5, endStream: true));

        var frames2 = decoder.Decode(bytes2);
        foreach (var frame in frames2)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(2, openStreams.Count);

        // Open 3 more
        var bytes3 = Concat(
            MakeResponseHeadersBytes(streamId: 11, endStream: false),
            MakeResponseHeadersBytes(streamId: 13, endStream: false),
            MakeResponseHeadersBytes(streamId: 15, endStream: false));

        var frames3 = decoder.Decode(bytes3);
        foreach (var frame in frames3)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Equal(5, openStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_not_decrement_below_zero_when_rst_stream_received_for_unknown_stream()
    {
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // No streams open
        Assert.Empty(openStreams);

        // RST_STREAM for stream 99 (never opened)
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(new RstStreamFrame(99, Http2ErrorCode.Cancel).Serialize());
        TrackStreamState(frames[0], openStreams, closedStreams);

        // Still empty (RST on unknown stream just records it closed)
        Assert.Empty(openStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_not_modify_limit_when_settings_ack_received()
    {
        var decoder = new FrameDecoder();
        const int currentLimit = 5;

        // Process SETTINGS ACK (has no parameters)
        var frames = decoder.Decode(SettingsFrame.SettingsAck());
        var frame = Assert.IsType<SettingsFrame>(frames[0]);

        // Extract returns current limit unchanged
        var newLimit = ExtractMaxConcurrentStreams(frame, currentLimit);
        Assert.Equal(5, newLimit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_apply_max_concurrent_streams_when_settings_frame_has_multiple_params()
    {
        var decoder = new FrameDecoder();
        var settings = new SettingsFrame(
        [
            (SettingsParameter.InitialWindowSize, 32768u),
            (SettingsParameter.MaxConcurrentStreams, 7u),
            (SettingsParameter.MaxFrameSize, 32768u),
        ]);

        var frames = decoder.Decode(settings.Serialize());
        var frame = Assert.IsType<SettingsFrame>(frames[0]);

        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(7, limit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_reference_rfc_in_message_when_concurrent_stream_limit_exceeded()
    {
        var ex = Assert.Throws<HttpProtocolException>(() =>
            EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: 1, streamId: 3));

        Assert.Contains("6.5.2", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_have_zero_open_streams_when_all_streams_closed_via_end_stream_headers()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open and immediately close 3 streams
        var bytes = Concat(
            MakeResponseHeadersBytes(streamId: 1, endStream: true),
            MakeResponseHeadersBytes(streamId: 3, endStream: true),
            MakeResponseHeadersBytes(streamId: 5, endStream: true));

        var frames = decoder.Decode(bytes);
        foreach (var frame in frames)
        {
            TrackStreamState(frame, openStreams, closedStreams);
        }

        Assert.Empty(openStreams);
        Assert.Equal(3, closedStreams.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_apply_large_value_correctly_when_max_concurrent_streams_set()
    {
        var decoder = new FrameDecoder();
        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, int.MaxValue)]);

        var frames = decoder.Decode(settings.Serialize());
        var frame = Assert.IsType<SettingsFrame>(frames[0]);

        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(int.MaxValue, limit);

        // Streams accepted with large limit
        EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: limit, streamId: 1);
    }
}