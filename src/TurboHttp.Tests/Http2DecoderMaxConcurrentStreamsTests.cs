using System.Collections.Generic;
using System.Linq;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Phase 32-33: Http2Decoder MAX_CONCURRENT_STREAMS enforcement.
/// RFC 7540 §5.1.2 and §6.5.2.
/// </summary>
public sealed class Http2DecoderMaxConcurrentStreamsTests
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
        var decoder = new Http2Decoder();
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-002: Default ActiveStreamCount is zero")]
    public void DefaultActiveStreamCount_IsZero()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-003: GetMaxConcurrentStreams returns int.MaxValue before any settings")]
    public void GetMaxConcurrentStreams_BeforeSettings_ReturnsIntMaxValue()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-004: GetActiveStreamCount returns zero before any frames")]
    public void GetActiveStreamCount_BeforeFrames_ReturnsZero()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-005: Reset restores MaxConcurrentStreams to int.MaxValue")]
    public void Reset_RestoresMaxConcurrentStreams_ToIntMaxValue()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(5), out _);
        Assert.Equal(5, decoder.GetMaxConcurrentStreams());

        decoder.Reset();
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-006: Reset restores ActiveStreamCount to zero")]
    public void Reset_RestoresActiveStreamCount_ToZero()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        decoder.TryDecode(headers, out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        decoder.Reset();
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-007: SETTINGS with MaxConcurrentStreams=1 sets limit to 1")]
    public void Settings_MaxConcurrentStreams1_SetsLimitTo1()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        Assert.Equal(1, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-008: SETTINGS with MaxConcurrentStreams=0 sets limit to 0")]
    public void Settings_MaxConcurrentStreams0_SetsLimitTo0()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(0), out _);
        Assert.Equal(0, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-009: SETTINGS with MaxConcurrentStreams=100 sets limit to 100")]
    public void Settings_MaxConcurrentStreams100_SetsLimitTo100()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(100), out _);
        Assert.Equal(100, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-API-010: ActiveStreamCount is zero before any stream is opened")]
    public void ActiveStreamCount_BeforeAnyStream_IsZero()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(SettingsFrame.SettingsAck(), out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-011: ActiveStreamCount increments when HEADERS opens stream without EndStream")]
    public void ActiveStreamCount_AfterHeadersWithoutEndStream_IsOne()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-012: ActiveStreamCount is zero after single-frame HEADERS with EndStream")]
    public void ActiveStreamCount_AfterHeadersWithEndStream_IsZero()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-013: ActiveStreamCount decrements after DATA with EndStream")]
    public void ActiveStreamCount_AfterDataWithEndStream_Decrements()
    {
        var decoder = new Http2Decoder();
        var headersFrame = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var dataFrame = MakeDataFrame(streamId: 1, endStream: true);

        decoder.TryDecode(headersFrame, out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        decoder.TryDecode(dataFrame, out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-014: ActiveStreamCount tracks multiple concurrent streams")]
    public void ActiveStreamCount_MultipleConcurrentStreams_Tracked()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());

        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _);
        Assert.Equal(3, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-015: ActiveStreamCount decrements after RST_STREAM")]
    public void ActiveStreamCount_AfterRstStream_Decrements()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-API-016: Exceeding MaxConcurrentStreams throws Http2Exception")]
    public void ExceedingLimit_ThrowsHttp2Exception()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        // Open one stream (allowed)
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        // Second stream must be refused
        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));

        Assert.NotNull(ex);
    }

    [Fact(DisplayName = "MCS-API-017: Exceeded limit uses RefusedStream error code")]
    public void ExceedingLimit_UsesRefusedStreamErrorCode()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-API-018: Exceeded limit message includes stream ID")]
    public void ExceedingLimit_MessageIncludesStreamId()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));

        Assert.Contains("3", ex.Message);
    }

    [Fact(DisplayName = "MCS-API-019: Exceeded limit message references MaxConcurrentStreams limit")]
    public void ExceedingLimit_MessageIncludesLimit()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(2), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _));

        Assert.Contains("2", ex.Message);
    }

    [Fact(DisplayName = "MCS-API-020: After stream closes, new stream is accepted (limit exact)")]
    public void AfterStreamCloses_NewStreamAccepted_WithExactLimit()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        // Open stream 1, then close it via EndStream
        var headers1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data1 = MakeDataFrame(streamId: 1, endStream: true);
        decoder.TryDecode(headers1, out _);
        decoder.TryDecode(data1, out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());

        // Now stream 3 should succeed
        var exception = Record.Exception(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));

        Assert.Null(exception);
        Assert.Equal(1, decoder.GetActiveStreamCount());
    }

    // ── Part 2: Integration Tests ─────────────────────────────────────────────

    [Fact(DisplayName = "MCS-INT-001: Single stream under default limit succeeds")]
    public void SingleStream_UnderDefaultLimit_Succeeds()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: true);
        var ok = decoder.TryDecode(headers, out var result);

        Assert.True(ok);
        Assert.Single(result.Responses);
        Assert.Equal(1, result.Responses[0].StreamId);
    }

    [Fact(DisplayName = "MCS-INT-002: Multiple streams under limit all succeed")]
    public void MultipleStreams_UnderLimit_AllSucceed()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(10), out _);

        for (var i = 0; i < 5; i++)
        {
            var streamId = 1 + (i * 2); // odd IDs: 1, 3, 5, 7, 9
            var exception = Record.Exception(() =>
                decoder.TryDecode(MakeResponseHeadersFrame(streamId, endStream: false), out _));
            Assert.Null(exception);
        }

        Assert.Equal(5, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-003: Stream at exact limit is refused")]
    public void StreamAtExactLimit_IsRefused()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(2), out _);

        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-INT-004: Limit enforcement applies only to new streams (existing unaffected)")]
    public void LimitEnforcement_DoesNotAffectExistingStreams()
    {
        var decoder = new Http2Decoder();
        // Open two streams with no limit
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);

        // Now set limit to 1 (below current active count)
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        // Existing streams can still receive DATA
        var exception = Record.Exception(() =>
            decoder.TryDecode(MakeDataFrame(streamId: 1, endStream: true), out _));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "MCS-INT-005: Counter decrements on EndStream DATA allowing new stream")]
    public void CounterDecrement_OnEndStreamData_AllowsNewStream()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeDataFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());

        var exception = Record.Exception(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));
        Assert.Null(exception);
        Assert.Equal(1, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-006: SETTINGS frame updates MaxConcurrentStreams limit")]
    public void SettingsFrame_UpdatesLimit()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());

        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(5), out _);
        Assert.Equal(5, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-INT-007: Second SETTINGS frame updates limit again")]
    public void SecondSettingsFrame_UpdatesLimitAgain()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(5), out _);
        Assert.Equal(5, decoder.GetMaxConcurrentStreams());

        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(20), out _);
        Assert.Equal(20, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-INT-008: RST_STREAM decrements active stream counter")]
    public void RstStream_DecrementsActiveCount()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());

        decoder.TryDecode(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize(), out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        decoder.TryDecode(new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize(), out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-009: Limit of 1 allows sequential streams")]
    public void Limit1_AllowsSequentialStreams()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        // First stream
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(0, decoder.GetActiveStreamCount());

        // Second stream (sequential, not concurrent)
        var exception = Record.Exception(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: true), out _));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "MCS-INT-010: Limit of 0 refuses all new streams")]
    public void Limit0_RefusesAllStreams()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(0), out _);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    [Fact(DisplayName = "MCS-INT-011: Multiple streams over limit all throw RefusedStream")]
    public void MultipleStreamsOverLimit_AllThrowRefusedStream()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        for (var i = 1; i <= 3; i++)
        {
            var streamId = 1 + (i * 2); // 3, 5, 7
            var localStreamId = streamId;
            var ex = Assert.Throws<Http2Exception>(() =>
                decoder.TryDecode(MakeResponseHeadersFrame(localStreamId, endStream: false), out _));
            Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
        }
    }

    [Fact(DisplayName = "MCS-INT-012: Headers-only response (EndStream in HEADERS) counts as zero active")]
    public void HeadersOnlyResponse_EndStreamInHeaders_ZeroActive()
    {
        var decoder = new Http2Decoder();

        // HEADERS with END_STREAM — single-frame response
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-013: Headers+DATA response decrements count correctly")]
    public void HeadersPlusData_DecrementsCountCorrectly()
    {
        var decoder = new Http2Decoder();

        var combined = Concat(
            MakeResponseHeadersFrame(streamId: 1, endStream: false),
            MakeDataFrame(streamId: 1, endStream: true));

        decoder.TryDecode(combined, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-014: Continuation frames do not double-count stream")]
    public void ContinuationFrames_DoNotDoubleCountStream()
    {
        var decoder = new Http2Decoder();

        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("content-type", "text/plain")]);

        var split = headerBlock.Length / 2;
        var block1 = headerBlock[..split];
        var block2 = headerBlock[split..];

        var headersFrame = new HeadersFrame(1, block1, endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block2, endHeaders: true).Serialize();

        var combined = Concat(headersFrame, contFrame);
        decoder.TryDecode(combined, out _);

        // Must be exactly 1, not 2
        Assert.Equal(1, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-015: SETTINGS change while streams active doesn't close existing streams")]
    public void SettingsChange_WhileStreamsActive_DoesNotCloseExistingStreams()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());

        // Change settings (limit decrease)
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);

        // Existing streams are still active
        Assert.Equal(2, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-016: Increasing limit allows more streams")]
    public void IncreasingLimit_AllowsMoreStreams()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(2), out _);

        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);

        // At limit — third would be refused
        Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _));

        // Increase limit
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(5), out _);

        // Now more streams are accepted
        var ex = Record.Exception(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "MCS-INT-017: Decreasing limit below active count allows existing streams to complete")]
    public void DecreasingLimit_BelowActiveCount_ExistingStreamsCanComplete()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 5, endStream: false), out _);
        Assert.Equal(3, decoder.GetActiveStreamCount());

        // Lower limit below active count
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        Assert.Equal(1, decoder.GetMaxConcurrentStreams());

        // Existing streams still complete normally
        decoder.TryDecode(MakeDataFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-018: Reset allows reconnection with fresh limits")]
    public void Reset_AllowsReconnectionWithFreshLimits()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(1, decoder.GetActiveStreamCount());

        decoder.Reset();

        Assert.Equal(0, decoder.GetActiveStreamCount());
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());

        // Fresh connection — limit is unconstrained again
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-019: ActiveStreamCount is accurate across multiple open/close cycles")]
    public void ActiveStreamCount_AccurateAcrossOpenCloseCycles()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(10), out _);

        // Open 5 streams
        for (var i = 0; i < 5; i++)
        {
            var streamId = 1 + (i * 2);
            decoder.TryDecode(MakeResponseHeadersFrame(streamId, endStream: false), out _);
        }

        Assert.Equal(5, decoder.GetActiveStreamCount());

        // Close 3 via EndStream DATA
        for (var i = 0; i < 3; i++)
        {
            var streamId = 1 + (i * 2);
            decoder.TryDecode(MakeDataFrame(streamId, endStream: true), out _);
        }

        Assert.Equal(2, decoder.GetActiveStreamCount());

        // Open 3 more
        for (var i = 5; i < 8; i++)
        {
            var streamId = 1 + (i * 2);
            decoder.TryDecode(MakeResponseHeadersFrame(streamId, endStream: false), out _);
        }

        Assert.Equal(5, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-020: RST_STREAM on unknown stream does not decrement counter below zero")]
    public void RstStream_OnUnknownStream_DoesNotDecrementBelowZero()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(0, decoder.GetActiveStreamCount());

        // RST_STREAM for a stream we never opened (no HEADERS received)
        var rst = new RstStreamFrame(99, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);

        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-021: SETTINGS ACK frame does not affect MaxConcurrentStreams")]
    public void SettingsAck_DoesNotAffectMaxConcurrentStreams()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(5), out _);
        Assert.Equal(5, decoder.GetMaxConcurrentStreams());

        decoder.TryDecode(SettingsFrame.SettingsAck(), out _);
        Assert.Equal(5, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-INT-022: SETTINGS frame with multiple parameters applies MaxConcurrentStreams")]
    public void SettingsFrame_WithMultipleParams_AppliesMaxConcurrentStreams()
    {
        var decoder = new Http2Decoder();
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 32768),
            (SettingsParameter.MaxConcurrentStreams, 7),
            (SettingsParameter.MaxFrameSize, 32768),
        }).Serialize();

        decoder.TryDecode(settings, out _);
        Assert.Equal(7, decoder.GetMaxConcurrentStreams());
    }

    [Fact(DisplayName = "MCS-INT-023: Limit enforcement message references RFC 7540 §6.5.2")]
    public void ExceedingLimit_MessageReferencesRfc()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeMaxConcurrentStreamsSettings(1), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: false), out _));

        Assert.Contains("6.5.2", ex.Message);
    }

    [Fact(DisplayName = "MCS-INT-024: ActiveStreamCount zero after all streams close via EndStream headers")]
    public void AllStreams_CloseViaEndStreamHeaders_CountIsZero()
    {
        var decoder = new Http2Decoder();

        // Open and immediately close 3 streams
        for (var i = 0; i < 3; i++)
        {
            var streamId = 1 + (i * 2);
            decoder.TryDecode(MakeResponseHeadersFrame(streamId, endStream: true), out _);
        }

        Assert.Equal(0, decoder.GetActiveStreamCount());
    }

    [Fact(DisplayName = "MCS-INT-025: MaxConcurrentStreams limit of uint.MaxValue boundary handled")]
    public void MaxConcurrentStreams_LargeValue_AppliedCorrectly()
    {
        var decoder = new Http2Decoder();
        // Apply a very large (but valid as uint) limit; stored as int (capped)
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, (uint)int.MaxValue),
        }).Serialize();

        decoder.TryDecode(settings, out _);
        Assert.Equal(int.MaxValue, decoder.GetMaxConcurrentStreams());

        // Streams still accepted
        var exception = Record.Exception(() =>
            decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _));
        Assert.Null(exception);
    }
}
