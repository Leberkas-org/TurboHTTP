using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class MaxPushIdTests
{
    // ───────────── Initial state ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-001: Initial state has no MAX_PUSH_ID sent")]
    public void InitialState_NoMaxPushIdSent()
    {
        var handler = new Http3MaxPushIdHandler();

        Assert.False(handler.HasSentMaxPushId);
        Assert.Equal(-1, handler.CurrentMaxPushId);
    }

    // ───────────── CreateMaxPushId ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-002: CreateMaxPushId sets current limit")]
    public void CreateMaxPushId_SetsLimit()
    {
        var handler = new Http3MaxPushIdHandler();

        var frame = handler.CreateMaxPushId(10);

        Assert.True(handler.HasSentMaxPushId);
        Assert.Equal(10, handler.CurrentMaxPushId);
        Assert.NotNull(frame);
        Assert.Equal(Http3FrameType.MaxPushId, frame.Type);
        Assert.Equal(10, frame.PushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-003: CreateMaxPushId with zero allows push ID 0")]
    public void CreateMaxPushId_Zero_AllowsPushIdZero()
    {
        var handler = new Http3MaxPushIdHandler();

        var frame = handler.CreateMaxPushId(0);

        Assert.True(handler.HasSentMaxPushId);
        Assert.Equal(0, handler.CurrentMaxPushId);
        Assert.Equal(0, frame.PushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-004: CreateMaxPushId with increasing values accepted")]
    public void CreateMaxPushId_IncreasingValues_Accepted()
    {
        var handler = new Http3MaxPushIdHandler();

        handler.CreateMaxPushId(5);
        Assert.Equal(5, handler.CurrentMaxPushId);

        handler.CreateMaxPushId(10);
        Assert.Equal(10, handler.CurrentMaxPushId);

        handler.CreateMaxPushId(100);
        Assert.Equal(100, handler.CurrentMaxPushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-005: CreateMaxPushId with same value accepted")]
    public void CreateMaxPushId_SameValue_Accepted()
    {
        var handler = new Http3MaxPushIdHandler();

        handler.CreateMaxPushId(5);
        handler.CreateMaxPushId(5);

        Assert.Equal(5, handler.CurrentMaxPushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-006: CreateMaxPushId with decreasing value is H3_ID_ERROR")]
    public void CreateMaxPushId_DecreasingValue_ThrowsIdError()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.CreateMaxPushId(5));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-007: CreateMaxPushId with negative value throws")]
    public void CreateMaxPushId_Negative_Throws()
    {
        var handler = new Http3MaxPushIdHandler();
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.CreateMaxPushId(-1));
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-008: CreateMaxPushId frame serializes correctly")]
    public void CreateMaxPushId_FrameSerializes()
    {
        var handler = new Http3MaxPushIdHandler();
        var frame = handler.CreateMaxPushId(42);

        var bytes = frame.Serialize();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // Round-trip decode
        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(bytes, out var decoded, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.NotNull(decoded);
        Assert.IsType<Http3MaxPushIdFrame>(decoded);
        Assert.Equal(42, ((Http3MaxPushIdFrame)decoded).PushId);
    }

    // ───────────── ValidatePushId ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-009: ValidatePushId rejects when no MAX_PUSH_ID sent")]
    public void ValidatePushId_NoMaxPushIdSent_ThrowsIdError()
    {
        var handler = new Http3MaxPushIdHandler();

        var ex = Assert.Throws<Http3Exception>(
            () => handler.ValidatePushId(0));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-010: ValidatePushId accepts push ID within limit")]
    public void ValidatePushId_WithinLimit_Succeeds()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        // Should not throw
        handler.ValidatePushId(0);
        handler.ValidatePushId(5);
        handler.ValidatePushId(10);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-011: ValidatePushId rejects push ID beyond limit")]
    public void ValidatePushId_BeyondLimit_ThrowsIdError()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.ValidatePushId(11));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Theory(DisplayName = "RFC9114-7.2.7-MP-012: ValidatePushId rejects various values beyond limit")]
    [InlineData(11)]
    [InlineData(100)]
    [InlineData(1000)]
    public void ValidatePushId_VariousExceeding_ThrowsIdError(long pushId)
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        var ex = Assert.Throws<Http3Exception>(
            () => handler.ValidatePushId(pushId));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-013: ValidatePushId respects updated limit")]
    public void ValidatePushId_UpdatedLimit_Succeeds()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(5);

        // Push ID 10 exceeds limit of 5
        Assert.Throws<Http3Exception>(() => handler.ValidatePushId(10));

        // Raise limit to 15
        handler.CreateMaxPushId(15);

        // Now push ID 10 is within limit
        handler.ValidatePushId(10);
    }

    // ───────────── IsPushIdAllowed ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-014: IsPushIdAllowed false when no MAX_PUSH_ID sent")]
    public void IsPushIdAllowed_NoMaxPushId_ReturnsFalse()
    {
        var handler = new Http3MaxPushIdHandler();

        Assert.False(handler.IsPushIdAllowed(0));
        Assert.False(handler.IsPushIdAllowed(5));
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-015: IsPushIdAllowed true for IDs within limit")]
    public void IsPushIdAllowed_WithinLimit_ReturnsTrue()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        Assert.True(handler.IsPushIdAllowed(0));
        Assert.True(handler.IsPushIdAllowed(5));
        Assert.True(handler.IsPushIdAllowed(10));
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-016: IsPushIdAllowed false for IDs beyond limit")]
    public void IsPushIdAllowed_BeyondLimit_ReturnsFalse()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        Assert.False(handler.IsPushIdAllowed(11));
        Assert.False(handler.IsPushIdAllowed(100));
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-017: IsPushIdAllowed false for negative IDs")]
    public void IsPushIdAllowed_Negative_ReturnsFalse()
    {
        var handler = new Http3MaxPushIdHandler();
        handler.CreateMaxPushId(10);

        Assert.False(handler.IsPushIdAllowed(-1));
    }

    // ───────────── ControlStream integration ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-018: MAX_PUSH_ID accepted on active control stream")]
    public void ControlStream_AcceptsMaxPushId()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        // Send SETTINGS first (required)
        var settings = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settings);

        // MAX_PUSH_ID should be accepted (no exception)
        var maxPushId = new Http3MaxPushIdFrame(10);
        cs.OnRemoteFrame(maxPushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.7-MP-019: MAX_PUSH_ID before SETTINGS is connection error")]
    public void ControlStream_MaxPushIdBeforeSettings_ThrowsMissingSettings()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var maxPushId = new Http3MaxPushIdFrame(10);
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(maxPushId));
        Assert.Equal(Http3ErrorCode.MissingSettings, ex.ErrorCode);
    }

    // ───────────── Large push ID values ─────────────

    [Fact(DisplayName = "RFC9114-7.2.7-MP-020: Large MAX_PUSH_ID value accepted")]
    public void CreateMaxPushId_LargeValue_Accepted()
    {
        var handler = new Http3MaxPushIdHandler();

        var frame = handler.CreateMaxPushId(4611686018427387903); // 2^62 - 1 (max QUIC varint)
        Assert.Equal(4611686018427387903, handler.CurrentMaxPushId);
        Assert.Equal(4611686018427387903, frame.PushId);
    }
}
