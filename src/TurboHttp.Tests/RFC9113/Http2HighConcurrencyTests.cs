using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 30: High-Concurrency Validation
///
/// Tests Http2ProtocolSession and HpackEncoder robustness under high-throughput and concurrent
/// access scenarios across four areas:
///
///   HC-001..005 — 10k stream creation attempts
///   HC-006..010 — Parallel header decoding (independent session instances)
///   HC-011..015 — Flow control saturation
///   HC-016..020 — Connection teardown under load
///
/// Note: Http2ProtocolSession is NOT thread-safe by design — one session per HTTP/2 connection.
/// Parallel tests use independent session instances, mirroring real production usage.
///
/// Test IDs: HC-001..HC-020
/// </summary>
public sealed class Http2HighConcurrencyTests
{
    // ── Frame building helpers ────────────────────────────────────────────────

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

    /// <summary>
    /// Builds a HEADERS frame.  Minimal HPACK: 0x88 = :status: 200 (static index 8).
    /// END_HEADERS = 0x4, END_STREAM = 0x1.
    /// </summary>
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

    // ── HC-001..005: 10k Stream Creation Attempts ────────────────────────────

    [Fact(DisplayName = "HC-001: 1000 sequential streams with END_STREAM HEADERS — all close cleanly")]
    public void Should_Handle1000SequentialStreams_WithEndStreamHeaders()
    {
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 1000; i++)
        {
            var streamId = 2 * i + 1; // odd IDs: 1, 3, 5, ..., 1999
            session.Process(BuildHeadersFrame(streamId, endStream: true).AsMemory());
        }

        Assert.Equal(0, session.ActiveStreamCount);
        Assert.Equal(1000, session.ClosedStreamCount);
    }

    [Fact(DisplayName = "HC-002: 1000 streams with END_STREAM HEADERS produce exactly 1000 decoded responses")]
    public void Should_Decode1000Responses_From1000Streams()
    {
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 1000; i++)
        {
            var streamId = 2 * i + 1;
            session.Process(BuildHeadersFrame(streamId, endStream: true).AsMemory());
        }

        Assert.Equal(1000, session.Responses.Count);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "HC-003: MAX_CONCURRENT_STREAMS=500 accepts exactly 500 simultaneous open streams")]
    public void Should_AcceptExactly500ConcurrentStreams_WhenLimitIs500()
    {
        var session = new Http2ProtocolSession();
        // MAX_CONCURRENT_STREAMS = SettingsParameter id 3
        session.Process(BuildSettingsFrame(false, (3, 500)).AsMemory());

        for (var i = 0; i < 500; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        Assert.Equal(500, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "HC-004: Bulk open-close cycle: 100 streams opened and DATA-closed, then 100 fresh streams opened")]
    public void Should_RecycleStreamCapacity_AfterBulkDataClose()
    {
        var session = new Http2ProtocolSession();
        // MAX_CONCURRENT_STREAMS=100
        session.Process(BuildSettingsFrame(false, (3, 100)).AsMemory());

        // Open 100 streams
        for (var i = 0; i < 100; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        Assert.Equal(100, session.ActiveStreamCount);

        // Close all 100 via DATA + END_STREAM
        var oneByte = new byte[] { 0x42 };
        for (var i = 0; i < 100; i++)
        {
            var streamId = 2 * i + 1;
            session.SetConnectionReceiveWindow(65535);
            session.SetStreamReceiveWindow(streamId, 65535);
            session.Process(BuildDataFrame(streamId, oneByte, endStream: true).AsMemory());
        }

        Assert.Equal(0, session.ActiveStreamCount);

        // Open 100 new streams (IDs 201..399) — all should be accepted within the MAX limit
        for (var i = 100; i < 200; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        Assert.Equal(100, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "HC-005: 10001 sequential streams all close correctly — unbounded closed-stream tracking")]
    public void Should_TrackAllClosedStreams_WithNoCapOnClosedStreamCount()
    {
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 10001; i++)
        {
            var streamId = 2 * i + 1; // 1, 3, ..., 20001
            session.Process(BuildHeadersFrame(streamId, endStream: true).AsMemory());
        }

        Assert.Equal(10001, session.ClosedStreamCount);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    // ── HC-006..010: Parallel Header Decoding ────────────────────────────────

    [Fact(DisplayName = "HC-006: 50 independent sessions decode same HEADERS frame in parallel — no exceptions")]
    public async Task Should_Decode50IndependentSessionInstances_InParallel_WithoutException()
    {
        var headersFrame = BuildHeadersFrame(1, endStream: true);

        var tasks = Enumerable.Range(0, 50).Select(_idx => Task.Run(() =>
        {
            var session = new Http2ProtocolSession();
            session.Process(headersFrame.AsMemory()); // must not throw
        }));

        await Task.WhenAll(tasks);
    }

    [Fact(DisplayName = "HC-007: 100 independent sessions each decode 20 streams in parallel — all active counts are zero")]
    public async Task Should_Handle100SessionInstances_EachDecoding20Streams_InParallel()
    {
        var tasks = Enumerable.Range(0, 100).Select(_idx => Task.Run(() =>
        {
            var session = new Http2ProtocolSession();
            for (var i = 0; i < 20; i++)
            {
                session.Process(BuildHeadersFrame(2 * i + 1, endStream: true).AsMemory());
            }

            return session.ActiveStreamCount;
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, count => Assert.Equal(0, count));
    }

    [Fact(DisplayName = "HC-008: Independent session instances maintain isolated stream counts under parallel load")]
    public async Task Should_MaintainIsolatedStreamState_AcrossParallelSessionInstances()
    {
        // Session i opens (i + 1) streams; verify each session's active count matches its expectation
        var tasks = Enumerable.Range(0, 20).Select(n => Task.Run(() =>
        {
            var session = new Http2ProtocolSession();
            for (var i = 0; i < n + 1; i++)
            {
                session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
            }

            return (Expected: n + 1, Actual: session.ActiveStreamCount);
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(r.Expected, r.Actual));
    }

    [Fact(DisplayName = "HC-009: 50 independent HpackEncoder instances encode the same headers in parallel — identical output")]
    public async Task Should_ProduceIdenticalHpackOutput_When50EncoderInstancesRunInParallel()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "42"),
        };

        // Sequential baseline using a fresh encoder
        var baseline = new HpackEncoder(useHuffman: false).Encode(headers).ToArray();

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            new HpackEncoder(useHuffman: false).Encode(headers).ToArray()));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, bytes => Assert.Equal(baseline, bytes));
    }

    [Fact(DisplayName = "HC-010: Parallel sessions produce the same closed-stream count as sequential baseline")]
    public async Task Should_ProduceConsistentClosedStreamCount_WhenParallelMatchesSequential()
    {
        const int streamCount = 10;

        // Sequential baseline: decode 10 streams on one session
        var seqSession = new Http2ProtocolSession();
        for (var i = 0; i < streamCount; i++)
        {
            seqSession.Process(BuildHeadersFrame(2 * i + 1, endStream: true).AsMemory());
        }

        var expectedClosed = seqSession.ClosedStreamCount;

        // Parallel: 20 independent sessions each decode the same 10 streams
        var tasks = Enumerable.Range(0, 20).Select(_idx => Task.Run(() =>
        {
            var session = new Http2ProtocolSession();
            for (var i = 0; i < streamCount; i++)
            {
                session.Process(BuildHeadersFrame(2 * i + 1, endStream: true).AsMemory());
            }

            return session.ClosedStreamCount;
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, count => Assert.Equal(expectedClosed, count));
    }

    // ── HC-011..015: Flow Control Saturation ─────────────────────────────────

    [Fact(DisplayName = "HC-011: Three sequential DATA frames totalling 45000 bytes stay within 65535-byte connection window")]
    public void Should_AcceptData_WhenTotalBytesDoNotExceedConnectionWindow()
    {
        var session = new Http2ProtocolSession();
        session.Process(BuildHeadersFrame(1, endStream: false).AsMemory());

        // Use 15000-byte chunks — each well within the 16384 MAX_FRAME_SIZE limit
        var chunk = new byte[15000];

        session.Process(BuildDataFrame(1, chunk, endStream: false).AsMemory());
        session.SetConnectionReceiveWindow(50535); // simulate WINDOW_UPDATE sent to peer

        session.Process(BuildDataFrame(1, chunk, endStream: false).AsMemory());
        session.SetConnectionReceiveWindow(35535);

        session.Process(BuildDataFrame(1, chunk, endStream: true).AsMemory());
    }

    [Fact(DisplayName = "HC-012: DATA exceeding the connection receive window triggers FlowControlError")]
    public void Should_ThrowFlowControlError_WhenDataExceedsConnectionReceiveWindow()
    {
        var session = new Http2ProtocolSession();
        session.SetConnectionReceiveWindow(100);
        session.Process(BuildHeadersFrame(1, endStream: false).AsMemory());
        session.SetStreamReceiveWindow(1, 65535);

        var oversized = new byte[101];
        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(BuildDataFrame(1, oversized, endStream: false).AsMemory()));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "HC-013: SetConnectionReceiveWindow restores capacity so subsequent DATA frames succeed")]
    public void Should_AcceptFurtherData_AfterConnectionWindowRestored()
    {
        var session = new Http2ProtocolSession();
        session.Process(BuildHeadersFrame(1, endStream: false).AsMemory());

        // Exhaust the window
        session.SetConnectionReceiveWindow(50);
        session.SetStreamReceiveWindow(1, 65535);
        var chunk = new byte[50];
        session.Process(BuildDataFrame(1, chunk, endStream: false).AsMemory());

        // Restore (simulates sending WINDOW_UPDATE to the remote peer)
        session.SetConnectionReceiveWindow(65535);
        session.SetStreamReceiveWindow(1, 65535);

        // Should now accept another chunk
        session.Process(BuildDataFrame(1, chunk, endStream: true).AsMemory());
    }

    [Fact(DisplayName = "HC-014: Per-stream window saturation is independent — other streams remain unaffected")]
    public void Should_EnforcePerStreamWindow_WithoutAffectingOtherStreams()
    {
        var session = new Http2ProtocolSession();

        // Open two streams
        session.Process(BuildHeadersFrame(1, endStream: false).AsMemory());
        session.Process(BuildHeadersFrame(3, endStream: false).AsMemory());

        // Saturate stream 1's receive window
        session.SetStreamReceiveWindow(1, 50);
        var oversized = new byte[51];
        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(BuildDataFrame(1, oversized, endStream: false).AsMemory()));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);

        // Stream 3 (different stream, fresh window) should be unaffected
        session.SetStreamReceiveWindow(3, 65535);
        session.Process(BuildDataFrame(3, new byte[100], endStream: true).AsMemory());
    }

    [Fact(DisplayName = "HC-015: Five sequential open-send-close cycles all succeed with correct final state")]
    public void Should_HandleSequentialOpenSendCloseCycles_WithCorrectFinalState()
    {
        var session = new Http2ProtocolSession();

        for (var round = 0; round < 5; round++)
        {
            var streamId = 2 * round + 1;
            session.Process(BuildHeadersFrame(streamId, endStream: false).AsMemory());

            session.SetConnectionReceiveWindow(65535);
            session.SetStreamReceiveWindow(streamId, 65535);
            session.Process(BuildDataFrame(streamId, new byte[1024], endStream: true).AsMemory());
        }

        Assert.Equal(0, session.ActiveStreamCount);
        Assert.Equal(5, session.ClosedStreamCount);
    }

    // ── HC-016..020: Connection Teardown Under Load ───────────────────────────

    [Fact(DisplayName = "HC-016: Reset() after 1000 open streams clears active count to zero")]
    public void Should_ClearActiveStreamCount_AfterResetFollowing1000OpenStreams()
    {
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 1000; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        Assert.Equal(1000, session.ActiveStreamCount);
        session.Reset();
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "HC-017: Reset() after GOAWAY clears IsGoingAway and resets GoAway last stream ID to int.MaxValue")]
    public void Should_ClearGoAwayState_OnReset()
    {
        var session = new Http2ProtocolSession();

        // GOAWAY frame: type=0x7, flags=0, stream=0, payload=[lastStreamId(4B) + errorCode(4B)]
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 5);        // lastStreamId = 5
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), 0); // errorCode = NO_ERROR
        session.Process(BuildRawFrame(0x7, 0, 0, payload).AsMemory());

        Assert.True(session.IsGoingAway);
        Assert.Equal(5, session.GoAwayLastStreamId);

        session.Reset();

        Assert.False(session.IsGoingAway);
        Assert.Equal(int.MaxValue, session.GoAwayLastStreamId);
    }

    [Fact(DisplayName = "HC-018: Reset() zeroes security counters so full flood thresholds are available again")]
    public void Should_ZeroAllSecurityCounters_OnReset()
    {
        var session = new Http2ProtocolSession();

        // Drive SETTINGS counter to 50
        var settingsFrame = BuildRawFrame(0x4, 0x0, 0, []);
        for (var i = 0; i < 50; i++)
        {
            session.Process(settingsFrame.AsMemory());
        }

        // Drive PING counter to 100
        var pingFrame = BuildRawFrame(0x6, 0x0, 0, new byte[8]);
        for (var i = 0; i < 100; i++)
        {
            session.Process(pingFrame.AsMemory());
        }

        session.Reset();

        // After Reset, full 100-frame SETTINGS threshold is available again
        for (var i = 0; i < 100; i++)
        {
            session.Process(settingsFrame.AsMemory()); // must not throw
        }

        // After Reset, full 1000-frame PING threshold is available again
        for (var i = 0; i < 1000; i++)
        {
            session.Process(pingFrame.AsMemory()); // must not throw
        }
    }

    [Fact(DisplayName = "HC-019: Multiple sequential Reset() calls are idempotent — state is fully cleared each time")]
    public void Should_BeIdempotent_WhenResetCalledMultipleTimes()
    {
        var session = new Http2ProtocolSession();

        for (var i = 0; i < 100; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        for (var i = 0; i < 5; i++)
        {
            session.Reset();
        }

        Assert.Equal(0, session.ActiveStreamCount);
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);
        Assert.False(session.IsGoingAway);
        Assert.Equal(int.MaxValue, session.GoAwayLastStreamId);
        Assert.Equal(0, session.ClosedStreamCount);
    }

    [Fact(DisplayName = "HC-020: Reset() then immediate decode of fresh streams succeeds without prior-state interference")]
    public void Should_DecodeNewStreams_AfterReset_WithoutPriorStateInterference()
    {
        var session = new Http2ProtocolSession();

        // Load the session with 500 open streams
        for (var i = 0; i < 500; i++)
        {
            session.Process(BuildHeadersFrame(2 * i + 1, endStream: false).AsMemory());
        }

        session.Reset();

        // Reuse stream IDs 1..20 — after Reset they are not in _closedStreamIds,
        // so they are treated as fresh idle streams and each should produce a response.
        for (var i = 0; i < 20; i++)
        {
            var streamId = 2 * i + 1;
            session.Process(BuildHeadersFrame(streamId, endStream: true).AsMemory());
        }

        Assert.Equal(20, session.Responses.Count);
        Assert.Equal(0, session.ActiveStreamCount);
    }
}
