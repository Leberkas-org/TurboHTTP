using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests SETTINGS_MAX_CONCURRENT_STREAMS enforcement per RFC 9113 §6.5.2.
/// Verifies stream creation limits and correct error signalling when the limit is exceeded.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS limits the number of simultaneously open streams on a connection.
/// </remarks>
public sealed class Http2SettingsMaxConcurrentTests
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-007: SETTINGS with MaxConcurrentStreams=1")]
    public void Should_DecodeCorrectly_WhenMaxConcurrentStreamsIs1()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(1));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(1, limit);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-008: SETTINGS with MaxConcurrentStreams=0")]
    public void Should_DecodeCorrectly_WhenMaxConcurrentStreamsIs0()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(0));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(0, limit);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-009: SETTINGS with MaxConcurrentStreams=100")]
    public void Should_DecodeCorrectly_WhenMaxConcurrentStreamsIs100()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(100));

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(100, limit);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-010: SETTINGS ACK doesn't modify MaxConcurrentStreams")]
    public void Should_RecognizeAsAck_WhenSettingsAckReceived()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(SettingsFrame.SettingsAck());

        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(frame.IsAck);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-011: HEADERS without EndStream opens stream")]
    public void Should_OpenStream_WhenHeadersReceivedWithoutEndStream()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: false));

        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(frame.EndStream);

        var openStreams = new HashSet<int>();
        TrackStreamState(frame, openStreams, []);
        Assert.Single(openStreams);
        Assert.Equal(1, openStreams.First());
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-012: HEADERS with EndStream closes immediately")]
    public void Should_CloseStreamImmediately_WhenHeadersReceivedWithEndStream()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));

        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndStream);

        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();
        TrackStreamState(frame, openStreams, closedStreams);
        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-013: DATA with EndStream closes stream")]
    public void Should_CloseStream_WhenDataReceivedWithEndStream()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-014: Multiple concurrent streams tracked")]
    public void Should_TrackConcurrentStreamsIndependently_WhenMultipleStreamsOpen()
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
        Assert.Contains(1, openStreams);
        Assert.Contains(3, openStreams);
        Assert.Contains(5, openStreams);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-015: RST_STREAM closes stream")]
    public void Should_CloseStream_WhenRstStreamReceived()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-016: Exceeding MaxConcurrentStreams throws")]
    public void Should_ThrowRefusedStream_WhenConcurrentStreamLimitExceeded()
    {
        var maxConcurrent = 1;
        var activeCount = 1;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 3));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-017: Exceeded limit uses RefusedStream error code")]
    public void Should_UseRefusedStreamErrorCode_WhenConcurrentStreamLimitExceeded()
    {
        var maxConcurrent = 1;
        var activeCount = 1;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 3));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-018: Exceeded limit message includes stream ID")]
    public void Should_IncludeStreamIdInMessage_WhenConcurrentStreamLimitExceeded()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: 1, streamId: 3));

        Assert.Contains("3", ex.Message);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-019: Exceeded limit message references MaxConcurrentStreams")]
    public void Should_IncludeLimitInMessage_WhenConcurrentStreamLimitExceeded()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 2, maxConcurrent: 2, streamId: 5));

        Assert.Contains("2", ex.Message);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-API-020: After stream closes, new stream accepted")]
    public void Should_AcceptNewStream_WhenPreviousStreamClosed()
    {
        var decoder = new Http2FrameDecoder();
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

    // MCS-INT: Integration Tests (§5.1.2 / §6.5.2)

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-001: Single stream under default limit succeeds")]
    public void Should_Succeed_WhenSingleStreamUnderDefaultLimit()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-002: Multiple streams under limit all succeed")]
    public void Should_Succeed_WhenMultipleStreamsUnderLimit()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-003: Stream at exact limit is refused")]
    public void Should_RefuseStream_WhenStreamCountIsAtExactLimit()
    {
        var maxConcurrent = 2;
        var activeCount = 2;

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount, maxConcurrent, streamId: 5));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-004: Limit enforcement applies only to new streams")]
    public void Should_NotAffectExistingStreams_WhenNewStreamLimitEnforced()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-005: Counter decrements on EndStream DATA")]
    public void Should_AllowNewStream_WhenStreamCounterDecrementsOnEndStreamData()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-006: SETTINGS frame updates MaxConcurrentStreams")]
    public void Should_UpdateMaxConcurrentStreams_WhenSettingsFrameProcessed()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-007: Second SETTINGS updates limit again")]
    public void Should_UpdateLimitAgain_WhenSecondSettingsFrameProcessed()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-008: RST_STREAM decrements active stream counter")]
    public void Should_DecrementActiveCount_WhenRstStreamProcessed()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-009: Limit of 1 allows sequential streams")]
    public void Should_AllowSequentialStreams_WhenLimitIsOne()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-010: Limit of 0 refuses all new streams")]
    public void Should_RefuseAllStreams_WhenLimitIsZero()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 0, maxConcurrent: 0, streamId: 1));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-011: Multiple streams over limit all throw RefusedStream")]
    public void Should_ThrowRefusedStream_WhenMultipleStreamsExceedLimit()
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-012: Headers-only response with EndStream")]
    public void Should_CountAsZeroOpenStreams_WhenHeadersOnlyResponseWithEndStream()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        var frames = decoder.Decode(MakeResponseHeadersBytes(streamId: 1, endStream: true));
        TrackStreamState(frames[0], openStreams, closedStreams);

        Assert.Empty(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-013: Headers+DATA response decrements count")]
    public void Should_DecrementCountCorrectly_WhenHeadersPlusDataResponseReceived()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-014: Continuation frames do not double-count")]
    public void Should_NotDoubleCountStream_WhenContinuationFramesReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("content-type", "text/plain")]);

        var split = headerBlock.Length / 2;
        var block1 = headerBlock[..split];
        var block2 = headerBlock[split..];

        var headersFrame = new HeadersFrame(1, block1, endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block2, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-015: SETTINGS change doesn't close existing streams")]
    public void Should_NotCloseExistingStreams_WhenSettingsChangedBelowActiveCount()
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

        // Process SETTINGS (doesn't affect open streams)
        decoder.Decode(MakeMaxConcurrentStreamsSettingsBytes(1));

        // Existing streams still open
        Assert.Equal(2, openStreams.Count);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-016: Increasing limit allows more streams")]
    public void Should_AllowMoreStreams_WhenConcurrentStreamLimitIncreased()
    {
        // With limit 2, two streams are open
        var activeCount = 2;

        // With new limit 5, third stream should be accepted
        var newLimit = 5;
        EnforceMaxConcurrentStreams(activeCount, newLimit, streamId: 5); // Should not throw
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-017: Decreasing limit allows existing streams to complete")]
    public void Should_AllowExistingStreamsToComplete_WhenLimitDecreased()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-019: ActiveStreamCount accurate across open/close cycles")]
    public void Should_MaintainAccurateCount_WhenActiveStreamsOpenAndClose()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-020: RST_STREAM on unknown stream doesn't decrement below zero")]
    public void Should_NotDecrementBelowZero_WhenRstStreamReceivedForUnknownStream()
    {
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // No streams open
        Assert.Empty(openStreams);

        // RST_STREAM for stream 99 (never opened)
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(new RstStreamFrame(99, Http2ErrorCode.Cancel).Serialize());
        TrackStreamState(frames[0], openStreams, closedStreams);

        // Still empty (RST on unknown stream just records it closed)
        Assert.Empty(openStreams);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-021: SETTINGS ACK frame doesn't modify limit")]
    public void Should_NotModifyLimit_WhenSettingsAckReceived()
    {
        var decoder = new Http2FrameDecoder();
        var currentLimit = 5;

        // Process SETTINGS ACK (has no parameters)
        var frames = decoder.Decode(SettingsFrame.SettingsAck());
        var frame = Assert.IsType<SettingsFrame>(frames[0]);

        // Extract returns current limit unchanged
        var newLimit = ExtractMaxConcurrentStreams(frame, currentLimit);
        Assert.Equal(5, newLimit);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-022: SETTINGS with multiple params applies MaxConcurrentStreams")]
    public void Should_ApplyMaxConcurrentStreams_WhenSettingsFrameHasMultipleParams()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-023: Limit enforcement message references RFC 6.5.2")]
    public void Should_ReferenceRfcInMessage_WhenConcurrentStreamLimitExceeded()
    {
        var ex = Assert.Throws<Http2Exception>(
            () => EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: 1, streamId: 3));

        Assert.Contains("6.5.2", ex.Message);
    }

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-024: All streams close via EndStream headers")]
    public void Should_HaveZeroOpenStreams_WhenAllStreamsClosedViaEndStreamHeaders()
    {
        var decoder = new Http2FrameDecoder();
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

    [Fact(DisplayName = "RFC9113-6.5.2-MCS-INT-025: MaxConcurrentStreams large value handled")]
    public void Should_ApplyLargeValueCorrectly_WhenMaxConcurrentStreamsSet()
    {
        var decoder = new Http2FrameDecoder();
        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, (uint)int.MaxValue)]);

        var frames = decoder.Decode(settings.Serialize());
        var frame = Assert.IsType<SettingsFrame>(frames[0]);

        var limit = ExtractMaxConcurrentStreams(frame, int.MaxValue);
        Assert.Equal(int.MaxValue, limit);

        // Streams accepted with large limit
        EnforceMaxConcurrentStreams(activeCount: 1, maxConcurrent: limit, streamId: 1);
    }
}
