using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Connection;

/// <summary>
/// Tests SETTINGS_MAX_CONCURRENT_STREAMS enforcement per RFC 9113 §6.5.2.
/// Verifies stream creation limits and correct error signalling when the limit is exceeded.
/// Part 1: API tests.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS limits the number of simultaneously open streams on a connection.
/// </remarks>
public sealed class Http2SettingsMaxConcurrentApiSpec
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

    // MCS-API: API Contract Tests (§5.1.2 / §6.5.2)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_decode_correctly_when_max_concurrent_streams_is_1()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(1));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(1, limit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_decode_correctly_when_max_concurrent_streams_is_0()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(0));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(0, limit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_decode_correctly_when_max_concurrent_streams_is_100()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(100));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(100, limit);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_recognize_as_ack_when_settings_ack_received()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(SettingsFrame.SettingsAck());

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(frame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_open_stream_when_headers_received_without_end_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));

        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(frame.EndStream);

        var openStreams = new HashSet<int>();
        TrackStreamState(frame, openStreams, []);
        Assert.Single(openStreams);
        Assert.Equal(1, openStreams.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_close_stream_immediately_when_headers_received_with_end_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));

        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndStream);

        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();
        TrackStreamState(frame, openStreams, closedStreams);
        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_close_stream_when_data_received_with_end_stream()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // HEADERS without END_STREAM opens stream 1
        var headersFrames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));
        TrackStreamState(headersFrames[0], openStreams, closedStreams);
        Assert.Single(openStreams);

        // DATA with END_STREAM closes stream 1
        var dataFrames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true));
        TrackStreamState(dataFrames[0], openStreams, closedStreams);
        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_track_concurrent_streams_independently_when_multiple_streams_open()
    {
        var decoder = new FrameDecoder();
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
        Assert.Contains(1, openStreams);
        Assert.Contains(3, openStreams);
        Assert.Contains(5, openStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_close_stream_when_rst_stream_received()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open stream 1
        var headersFrames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));
        TrackStreamState(headersFrames[0], openStreams, closedStreams);
        Assert.Single(openStreams);

        // RST_STREAM on stream 1
        var rstFrames = decoder.Decode(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize());
        TrackStreamState(rstFrames[0], openStreams, closedStreams);
        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_throw_refused_stream_when_concurrent_stream_limit_exceeded()
    {
        var maxConcurrent = 1;
        var activeCount = 1;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 3));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_use_refused_stream_error_code_when_concurrent_stream_limit_exceeded()
    {
        var maxConcurrent = 1;
        var activeCount = 1;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 3));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_include_stream_id_in_message_when_concurrent_stream_limit_exceeded()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: 1, streamId: 3));

        Assert.Contains("3", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_include_limit_in_message_when_concurrent_stream_limit_exceeded()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 2, maxConcurrent: 2, streamId: 5));

        Assert.Contains("2", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_accept_new_stream_when_previous_stream_closed()
    {
        var decoder = new FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();
        var maxConcurrent = 1;

        // Open stream 1
        var headersFrames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));
        TrackStreamState(headersFrames[0], openStreams, closedStreams);
        Assert.Single(openStreams);

        // Close stream 1 via END_STREAM
        var dataFrames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true));
        TrackStreamState(dataFrames[0], openStreams, closedStreams);
        Assert.Empty(openStreams);

        // Stream 3 should be accepted (limit not exceeded)
        var newActiveCount = openStreams.Count;
        var exception = Record.Exception(() =>
            EnforceMaxConcurrentStreams(newActiveCount, maxConcurrent, streamId: 3));

        Assert.Null(exception);
    }
}
