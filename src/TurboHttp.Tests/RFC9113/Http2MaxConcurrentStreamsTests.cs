using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 32-33: Http2Decoder MAX_CONCURRENT_STREAMS enforcement.
/// RFC 7540 §5.1.2 and §6.5.2.
/// </summary>
public sealed class Http2MaxConcurrentStreamsTests
{
    // ── Helper methods ────────────────────────────────────────────────────────

    private static byte[] MakeResponseHeadersFrame(int streamId, bool endStream = false, bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] MakeDataFrame(int streamId, bool endStream = true)
    {
        return new DataFrame(streamId, "ok"u8.ToArray(), endStream).Serialize();
    }

    private static byte[] MakeMaxConcurrentStreamsSettings(uint limit)
    {
        return new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, limit),
        }).Serialize();
    }

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

    // ── Part 1: API Contract Tests ────────────────────────────────────────────

    [Fact(DisplayName = "MCS-API-001: Default MaxConcurrentStreams is int.MaxValue")]
    public void DefaultMaxConcurrentStreams_IsIntMaxValue()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-002: Default ActiveStreamCount is zero")]
    public void DefaultActiveStreamCount_IsZero()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-003: GetMaxConcurrentStreams returns int.MaxValue before any settings")]
    public void GetMaxConcurrentStreams_BeforeSettings_ReturnsIntMaxValue()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-004: GetActiveStreamCount returns zero before any frames")]
    public void GetActiveStreamCount_BeforeFrames_ReturnsZero()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-005: Reset restores MaxConcurrentStreams to int.MaxValue")]
    public void Reset_RestoresMaxConcurrentStreams_ToIntMaxValue()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(5));
        Assert.Equal(5, session.MaxConcurrentStreams);

        session.Reset();
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-006: Reset restores ActiveStreamCount to zero")]
    public void Reset_RestoresActiveStreamCount_ToZero()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        session.Process(headers);
        Assert.Equal(1, session.ActiveStreamCount);

        session.Reset();
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-007: SETTINGS with MaxConcurrentStreams=1 sets limit to 1")]
    public void Settings_MaxConcurrentStreams1_SetsLimitTo1()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        Assert.Equal(1, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-008: SETTINGS with MaxConcurrentStreams=0 sets limit to 0")]
    public void Settings_MaxConcurrentStreams0_SetsLimitTo0()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(0));
        Assert.Equal(0, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-009: SETTINGS with MaxConcurrentStreams=100 sets limit to 100")]
    public void Settings_MaxConcurrentStreams100_SetsLimitTo100()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(100));
        Assert.Equal(100, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-API-010: ActiveStreamCount is zero before any stream is opened")]
    public void ActiveStreamCount_BeforeAnyStream_IsZero()
    {
        var session = new Http2ProtocolSession();
        session.Process(SettingsFrame.SettingsAck());
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-011: ActiveStreamCount increments when HEADERS opens stream without EndStream")]
    public void ActiveStreamCount_AfterHeadersWithoutEndStream_IsOne()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(1, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-012: ActiveStreamCount is zero after single-frame HEADERS with EndStream")]
    public void ActiveStreamCount_AfterHeadersWithEndStream_IsZero()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-013: ActiveStreamCount decrements after DATA with EndStream")]
    public void ActiveStreamCount_AfterDataWithEndStream_Decrements()
    {
        var session = new Http2ProtocolSession();
        var headersFrame = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var dataFrame = MakeDataFrame(streamId: 1, endStream: true);

        session.Process(headersFrame);
        Assert.Equal(1, session.ActiveStreamCount);

        session.Process(dataFrame);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-014: ActiveStreamCount tracks multiple concurrent streams")]
    public void ActiveStreamCount_MultipleConcurrentStreams_Tracked()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(1, session.ActiveStreamCount);

        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        Assert.Equal(2, session.ActiveStreamCount);

        session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false));
        Assert.Equal(3, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-015: ActiveStreamCount decrements after RST_STREAM")]
    public void ActiveStreamCount_AfterRstStream_Decrements()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(1, session.ActiveStreamCount);

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-API-016: Exceeding MaxConcurrentStreams throws Http2Exception")]
    public void ExceedingLimit_ThrowsHttp2Exception()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        // Open one stream (allowed)
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        // Second stream must be refused
        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));

        Assert.NotNull(ex);
    }

    [Fact(DisplayName = "MCS-API-017: Exceeded limit uses RefusedStream error code")]
    public void ExceedingLimit_UsesRefusedStreamErrorCode()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-API-018: Exceeded limit message includes stream ID")]
    public void ExceedingLimit_MessageIncludesStreamId()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));

        Assert.Contains("3", ex.Message);
    }

    [Fact(DisplayName = "MCS-API-019: Exceeded limit message references MaxConcurrentStreams limit")]
    public void ExceedingLimit_MessageIncludesLimit()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(2));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false)));

        Assert.Contains("2", ex.Message);
    }

    [Fact(DisplayName = "MCS-API-020: After stream closes, new stream is accepted (limit exact)")]
    public void AfterStreamCloses_NewStreamAccepted_WithExactLimit()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        // Open stream 1, then close it via EndStream
        var headers1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data1 = MakeDataFrame(streamId: 1, endStream: true);
        session.Process(headers1);
        session.Process(data1);
        Assert.Equal(0, session.ActiveStreamCount);

        // Now stream 3 should succeed
        var exception = Record.Exception(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));

        Assert.Null(exception);
        Assert.Equal(1, session.ActiveStreamCount);
    }

    // ── Part 2: Integration Tests ─────────────────────────────────────────────

    [Fact(DisplayName = "MCS-INT-001: Single stream under default limit succeeds")]
    public void SingleStream_UnderDefaultLimit_Succeeds()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: true);
        var frames = session.Process(headers);

        Assert.NotEmpty(frames);
        Assert.Single(session.Responses);
        Assert.Equal(1, session.Responses[0].StreamId);
    }

    [Fact(DisplayName = "MCS-INT-002: Multiple streams under limit all succeed")]
    public void MultipleStreams_UnderLimit_AllSucceed()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(10));

        for (var i = 0; i < 5; i++)
        {
            var streamId = 1 + (i * 2); // odd IDs: 1, 3, 5, 7, 9
            var exception = Record.Exception(() =>
                session.Process(MakeResponseHeadersFrame(streamId, endStream: false)));
            Assert.Null(exception);
        }

        Assert.Equal(5, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-003: Stream at exact limit is refused")]
    public void StreamAtExactLimit_IsRefused()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(2));

        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        Assert.Equal(2, session.ActiveStreamCount);

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false)));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-INT-004: Limit enforcement applies only to new streams (existing unaffected)")]
    public void LimitEnforcement_DoesNotAffectExistingStreams()
    {
        var session = new Http2ProtocolSession();
        // Open two streams with no limit
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));

        // Now set limit to 1 (below current active count)
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        // Existing streams can still receive DATA
        var exception = Record.Exception(() =>
            session.Process(MakeDataFrame(streamId: 1, endStream: true)));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "MCS-INT-005: Counter decrements on EndStream DATA allowing new stream")]
    public void CounterDecrement_OnEndStreamData_AllowsNewStream()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeDataFrame(streamId: 1, endStream: true));
        Assert.Equal(0, session.ActiveStreamCount);

        var exception = Record.Exception(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));
        Assert.Null(exception);
        Assert.Equal(1, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-006: SETTINGS frame updates MaxConcurrentStreams limit")]
    public void SettingsFrame_UpdatesLimit()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);

        session.Process(MakeMaxConcurrentStreamsSettings(5));
        Assert.Equal(5, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-INT-007: Second SETTINGS frame updates limit again")]
    public void SecondSettingsFrame_UpdatesLimitAgain()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(5));
        Assert.Equal(5, session.MaxConcurrentStreams);

        session.Process(MakeMaxConcurrentStreamsSettings(20));
        Assert.Equal(20, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-INT-008: RST_STREAM decrements active stream counter")]
    public void RstStream_DecrementsActiveCount()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        Assert.Equal(2, session.ActiveStreamCount);

        session.Process(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize());
        Assert.Equal(1, session.ActiveStreamCount);

        session.Process(new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize());
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-009: Limit of 1 allows sequential streams")]
    public void Limit1_AllowsSequentialStreams()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        // First stream
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(0, session.ActiveStreamCount);

        // Second stream (sequential, not concurrent)
        var exception = Record.Exception(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: true)));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "MCS-INT-010: Limit of 0 refuses all new streams")]
    public void Limit0_RefusesAllStreams()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(0));

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false)));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-INT-011: Multiple streams over limit all throw RefusedStream")]
    public void MultipleStreamsOverLimit_AllThrowRefusedStream()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        for (var i = 1; i <= 3; i++)
        {
            var streamId = 1 + (i * 2); // 3, 5, 7
            var localStreamId = streamId;
            var ex = Assert.Throws<Http2Exception>(() =>
                session.Process(MakeResponseHeadersFrame(localStreamId, endStream: false)));
            Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
        }
    }

    [Fact(DisplayName = "MCS-INT-012: Headers-only response (EndStream in HEADERS) counts as zero active")]
    public void HeadersOnlyResponse_EndStreamInHeaders_ZeroActive()
    {
        var session = new Http2ProtocolSession();

        // HEADERS with END_STREAM — single-frame response
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));

        Assert.True(session.Responses.Count > 0);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-013: Headers+DATA response decrements count correctly")]
    public void HeadersPlusData_DecrementsCountCorrectly()
    {
        var session = new Http2ProtocolSession();

        var combined = Concat(
            MakeResponseHeadersFrame(streamId: 1, endStream: false),
            MakeDataFrame(streamId: 1, endStream: true));

        session.Process(combined);

        Assert.True(session.Responses.Count > 0);
        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-014: Continuation frames do not double-count stream")]
    public void ContinuationFrames_DoNotDoubleCountStream()
    {
        var session = new Http2ProtocolSession();

        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("content-type", "text/plain")]);

        var split = headerBlock.Length / 2;
        var block1 = headerBlock[..split];
        var block2 = headerBlock[split..];

        var headersFrame = new HeadersFrame(1, block1, endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block2, endHeaders: true).Serialize();

        var combined = Concat(headersFrame, contFrame);
        session.Process(combined);

        // Must be exactly 1, not 2
        Assert.Equal(1, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-015: SETTINGS change while streams active doesn't close existing streams")]
    public void SettingsChange_WhileStreamsActive_DoesNotCloseExistingStreams()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        Assert.Equal(2, session.ActiveStreamCount);

        // Change settings (limit decrease)
        session.Process(MakeMaxConcurrentStreamsSettings(1));

        // Existing streams are still active
        Assert.Equal(2, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-016: Increasing limit allows more streams")]
    public void IncreasingLimit_AllowsMoreStreams()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(2));

        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));

        // At limit — third would be refused
        Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false)));

        // Increase limit
        session.Process(MakeMaxConcurrentStreamsSettings(5));

        // Now more streams are accepted
        var ex = Record.Exception(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false)));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "MCS-INT-017: Decreasing limit below active count allows existing streams to complete")]
    public void DecreasingLimit_BelowActiveCount_ExistingStreamsCanComplete()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 5, endStream: false));
        Assert.Equal(3, session.ActiveStreamCount);

        // Lower limit below active count
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        Assert.Equal(1, session.MaxConcurrentStreams);

        // Existing streams still complete normally
        session.Process(MakeDataFrame(streamId: 1, endStream: true));
        Assert.Equal(2, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-018: Reset allows reconnection with fresh limits")]
    public void Reset_AllowsReconnectionWithFreshLimits()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(1, session.ActiveStreamCount);

        session.Reset();

        Assert.Equal(0, session.ActiveStreamCount);
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);

        // Fresh connection — limit is unconstrained again
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false));
        Assert.Equal(2, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-019: ActiveStreamCount is accurate across multiple open/close cycles")]
    public void ActiveStreamCount_AccurateAcrossOpenCloseCycles()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(10));

        // Open 5 streams
        for (var i = 0; i < 5; i++)
        {
            var streamId = 1 + (i * 2);
            session.Process(MakeResponseHeadersFrame(streamId, endStream: false));
        }

        Assert.Equal(5, session.ActiveStreamCount);

        // Close 3 via EndStream DATA
        for (var i = 0; i < 3; i++)
        {
            var streamId = 1 + (i * 2);
            session.Process(MakeDataFrame(streamId, endStream: true));
        }

        Assert.Equal(2, session.ActiveStreamCount);

        // Open 3 more
        for (var i = 5; i < 8; i++)
        {
            var streamId = 1 + (i * 2);
            session.Process(MakeResponseHeadersFrame(streamId, endStream: false));
        }

        Assert.Equal(5, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-020: RST_STREAM on unknown stream does not decrement counter below zero")]
    public void RstStream_OnUnknownStream_DoesNotDecrementBelowZero()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(0, session.ActiveStreamCount);

        // RST_STREAM for a stream we never opened (no HEADERS received)
        var rst = new RstStreamFrame(99, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);

        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-021: SETTINGS ACK frame does not affect MaxConcurrentStreams")]
    public void SettingsAck_DoesNotAffectMaxConcurrentStreams()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(5));
        Assert.Equal(5, session.MaxConcurrentStreams);

        session.Process(SettingsFrame.SettingsAck());
        Assert.Equal(5, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-INT-022: SETTINGS frame with multiple parameters applies MaxConcurrentStreams")]
    public void SettingsFrame_WithMultipleParams_AppliesMaxConcurrentStreams()
    {
        var session = new Http2ProtocolSession();
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 32768),
            (SettingsParameter.MaxConcurrentStreams, 7),
            (SettingsParameter.MaxFrameSize, 32768),
        }).Serialize();

        session.Process(settings);
        Assert.Equal(7, session.MaxConcurrentStreams);
    }

    [Fact(DisplayName = "MCS-INT-023: Limit enforcement message references RFC 7540 §6.5.2")]
    public void ExceedingLimit_MessageReferencesRfc()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeMaxConcurrentStreamsSettings(1));
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: false)));

        Assert.Contains("6.5.2", ex.Message);
    }

    [Fact(DisplayName = "MCS-INT-024: ActiveStreamCount zero after all streams close via EndStream headers")]
    public void AllStreams_CloseViaEndStreamHeaders_CountIsZero()
    {
        var session = new Http2ProtocolSession();

        // Open and immediately close 3 streams
        for (var i = 0; i < 3; i++)
        {
            var streamId = 1 + (i * 2);
            session.Process(MakeResponseHeadersFrame(streamId, endStream: true));
        }

        Assert.Equal(0, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "MCS-INT-025: MaxConcurrentStreams limit of uint.MaxValue boundary handled")]
    public void MaxConcurrentStreams_LargeValue_AppliedCorrectly()
    {
        var session = new Http2ProtocolSession();
        // Apply a very large (but valid as uint) limit; stored as int (capped)
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, int.MaxValue),
        }).Serialize();

        session.Process(settings);
        Assert.Equal(int.MaxValue, session.MaxConcurrentStreams);

        // Streams still accepted
        var exception = Record.Exception(() =>
            session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false)));
        Assert.Null(exception);
    }
}
