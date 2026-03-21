using System;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class GoAwayTests
{
    // ───────────── Server GOAWAY parsing ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-001: Server GOAWAY sets IsGoingAway and LastStreamId")]
    public void OnServerGoAway_SetsState()
    {
        var handler = new Http3GoAwayHandler();
        Assert.False(handler.IsGoingAway);
        Assert.Equal(-1, handler.LastStreamId);

        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.True(handler.IsGoingAway);
        Assert.Equal(8, handler.LastStreamId);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-002: Server GOAWAY with stream ID 0 means no requests processed")]
    public void OnServerGoAway_StreamIdZero_NoRequestsProcessed()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(0));

        Assert.True(handler.IsGoingAway);
        Assert.Equal(0, handler.LastStreamId);
        Assert.True(handler.IsStreamAffected(0));
        Assert.True(handler.IsStreamAffected(4));
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-003: Multiple server GOAWAYs with decreasing IDs accepted")]
    public void OnServerGoAway_DecreasingIds_Accepted()
    {
        var handler = new Http3GoAwayHandler();

        handler.OnServerGoAway(new Http3GoAwayFrame(12));
        Assert.Equal(12, handler.LastStreamId);

        handler.OnServerGoAway(new Http3GoAwayFrame(8));
        Assert.Equal(8, handler.LastStreamId);

        handler.OnServerGoAway(new Http3GoAwayFrame(4));
        Assert.Equal(4, handler.LastStreamId);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-004: Same GOAWAY stream ID accepted (not increasing)")]
    public void OnServerGoAway_SameId_Accepted()
    {
        var handler = new Http3GoAwayHandler();

        handler.OnServerGoAway(new Http3GoAwayFrame(8));
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.Equal(8, handler.LastStreamId);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-005: Increasing GOAWAY stream ID is connection error H3_ID_ERROR")]
    public void OnServerGoAway_IncreasingId_ThrowsIdError()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(4));

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.OnServerGoAway(new Http3GoAwayFrame(8)));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-006: GOAWAY with non-4-divisible stream ID is H3_ID_ERROR")]
    public void OnServerGoAway_InvalidStreamId_ThrowsIdError()
    {
        var handler = new Http3GoAwayHandler();

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.OnServerGoAway(new Http3GoAwayFrame(5)));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Theory(DisplayName = "RFC9114-5.2-GA-007: Odd stream IDs rejected")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9)]
    public void OnServerGoAway_OddOrNonDivisibleId_ThrowsIdError(long streamId)
    {
        var handler = new Http3GoAwayHandler();

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.OnServerGoAway(new Http3GoAwayFrame(streamId)));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-008: Null frame throws ArgumentNullException")]
    public void OnServerGoAway_NullFrame_Throws()
    {
        var handler = new Http3GoAwayHandler();
        Assert.Throws<ArgumentNullException>(() => handler.OnServerGoAway(null!));
    }

    // ───────────── Stream affected detection ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-009: Streams >= GOAWAY ID are affected")]
    public void IsStreamAffected_AtOrAboveGoAway_ReturnsTrue()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.True(handler.IsStreamAffected(8));
        Assert.True(handler.IsStreamAffected(12));
        Assert.True(handler.IsStreamAffected(100));
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-010: Streams < GOAWAY ID are not affected")]
    public void IsStreamAffected_BelowGoAway_ReturnsFalse()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.False(handler.IsStreamAffected(0));
        Assert.False(handler.IsStreamAffected(4));
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-011: No GOAWAY means no streams affected")]
    public void IsStreamAffected_NoGoAway_ReturnsFalse()
    {
        var handler = new Http3GoAwayHandler();

        Assert.False(handler.IsStreamAffected(0));
        Assert.False(handler.IsStreamAffected(100));
    }

    // ───────────── Retryable stream identification ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-012: GetRetryableStreamIds returns affected streams")]
    public void GetRetryableStreamIds_ReturnsAffectedStreams()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        var active = new long[] { 0, 4, 8, 12, 16 };
        var retryable = handler.GetRetryableStreamIds(active);

        Assert.Equal(3, retryable.Count);
        Assert.Contains(8L, retryable);
        Assert.Contains(12L, retryable);
        Assert.Contains(16L, retryable);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-013: GetRetryableStreamIds empty when no GOAWAY")]
    public void GetRetryableStreamIds_NoGoAway_ReturnsEmpty()
    {
        var handler = new Http3GoAwayHandler();

        var retryable = handler.GetRetryableStreamIds(new long[] { 0, 4, 8 });
        Assert.Empty(retryable);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-014: GetRetryableStreamIds all retryable when GOAWAY 0")]
    public void GetRetryableStreamIds_GoAwayZero_AllRetryable()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(0));

        var active = new long[] { 0, 4, 8 };
        var retryable = handler.GetRetryableStreamIds(active);

        Assert.Equal(3, retryable.Count);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-015: GetRetryableStreamIds null throws")]
    public void GetRetryableStreamIds_NullArg_Throws()
    {
        var handler = new Http3GoAwayHandler();
        Assert.Throws<ArgumentNullException>(() => handler.GetRetryableStreamIds(null!));
    }

    // ───────────── CanSendRequest ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-016: CanSendRequest true before GOAWAY")]
    public void CanSendRequest_NoGoAway_ReturnsTrue()
    {
        var handler = new Http3GoAwayHandler();

        Assert.True(handler.CanSendRequest(0));
        Assert.True(handler.CanSendRequest(100));
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-017: CanSendRequest false for stream ID >= GOAWAY")]
    public void CanSendRequest_AtOrAboveGoAway_ReturnsFalse()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.False(handler.CanSendRequest(8));
        Assert.False(handler.CanSendRequest(12));
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-018: CanSendRequest true for stream ID < GOAWAY")]
    public void CanSendRequest_BelowGoAway_ReturnsTrue()
    {
        var handler = new Http3GoAwayHandler();
        handler.OnServerGoAway(new Http3GoAwayFrame(8));

        Assert.True(handler.CanSendRequest(0));
        Assert.True(handler.CanSendRequest(4));
    }

    // ───────────── Client GOAWAY (shutdown) ─────────────

    [Fact(DisplayName = "RFC9114-7.2.6-GA-019: Client creates GOAWAY frame with push ID")]
    public void CreateClientGoAway_CreatesFrame()
    {
        var handler = new Http3GoAwayHandler();

        var frame = handler.CreateClientGoAway(0);

        Assert.NotNull(frame);
        Assert.Equal(Http3FrameType.GoAway, frame.Type);
        Assert.Equal(0, frame.StreamId);
        Assert.True(handler.ClientGoAwaySent);
        Assert.Equal(0, handler.ClientGoAwayPushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.6-GA-020: Client GOAWAY with decreasing push ID accepted")]
    public void CreateClientGoAway_DecreasingPushId_Accepted()
    {
        var handler = new Http3GoAwayHandler();

        handler.CreateClientGoAway(10);
        Assert.Equal(10, handler.ClientGoAwayPushId);

        handler.CreateClientGoAway(5);
        Assert.Equal(5, handler.ClientGoAwayPushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.6-GA-021: Client GOAWAY with same push ID accepted")]
    public void CreateClientGoAway_SamePushId_Accepted()
    {
        var handler = new Http3GoAwayHandler();

        handler.CreateClientGoAway(5);
        handler.CreateClientGoAway(5);

        Assert.Equal(5, handler.ClientGoAwayPushId);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-022: Client GOAWAY with increasing push ID is H3_ID_ERROR")]
    public void CreateClientGoAway_IncreasingPushId_ThrowsIdError()
    {
        var handler = new Http3GoAwayHandler();
        handler.CreateClientGoAway(5);

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.CreateClientGoAway(10));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.6-GA-023: Client GOAWAY with negative push ID throws")]
    public void CreateClientGoAway_NegativePushId_Throws()
    {
        var handler = new Http3GoAwayHandler();
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.CreateClientGoAway(-1));
    }

    [Fact(DisplayName = "RFC9114-7.2.6-GA-024: Client GOAWAY frame serializes correctly")]
    public void CreateClientGoAway_FrameSerializes()
    {
        var handler = new Http3GoAwayHandler();
        var frame = handler.CreateClientGoAway(0);

        var bytes = frame.Serialize();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // Decode and verify round-trip
        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(bytes, out var decoded, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.NotNull(decoded);
        Assert.IsType<Http3GoAwayFrame>(decoded);
        Assert.Equal(0, ((Http3GoAwayFrame)decoded).StreamId);
    }

    // ───────────── Integration: GOAWAY narrowing ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-025: Multiple GOAWAYs narrow retryable set")]
    public void MultipleGoAways_NarrowRetryableSet()
    {
        var handler = new Http3GoAwayHandler();
        var active = new long[] { 0, 4, 8, 12, 16 };

        handler.OnServerGoAway(new Http3GoAwayFrame(12));
        var retryable1 = handler.GetRetryableStreamIds(active);
        Assert.Equal(2, retryable1.Count); // 12, 16

        handler.OnServerGoAway(new Http3GoAwayFrame(8));
        var retryable2 = handler.GetRetryableStreamIds(active);
        Assert.Equal(3, retryable2.Count); // 8, 12, 16

        handler.OnServerGoAway(new Http3GoAwayFrame(0));
        var retryable3 = handler.GetRetryableStreamIds(active);
        Assert.Equal(5, retryable3.Count); // all
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-026: CanSendRequest narrows with successive GOAWAYs")]
    public void MultipleGoAways_NarrowCanSend()
    {
        var handler = new Http3GoAwayHandler();

        handler.OnServerGoAway(new Http3GoAwayFrame(12));
        Assert.True(handler.CanSendRequest(8));
        Assert.False(handler.CanSendRequest(12));

        handler.OnServerGoAway(new Http3GoAwayFrame(4));
        Assert.True(handler.CanSendRequest(0));
        Assert.False(handler.CanSendRequest(4));
        Assert.False(handler.CanSendRequest(8));
    }

    // ───────────── ControlStream integration ─────────────

    [Fact(DisplayName = "RFC9114-5.2-GA-027: GOAWAY accepted on active control stream")]
    public void ControlStream_AcceptsGoAway()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        // Send SETTINGS first (required)
        var settings = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settings);

        // GOAWAY should be accepted (no exception)
        var goaway = new Http3GoAwayFrame(4);
        cs.OnRemoteFrame(goaway);
    }

    [Fact(DisplayName = "RFC9114-5.2-GA-028: GOAWAY before SETTINGS is connection error")]
    public void ControlStream_GoAwayBeforeSettings_ThrowsMissingSettings()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var goaway = new Http3GoAwayFrame(4);
        var ex = Assert.Throws<Http3ConnectionException>(() => cs.OnRemoteFrame(goaway));
        Assert.Equal(Http3ErrorCode.MissingSettings, ex.ErrorCode);
    }
}
