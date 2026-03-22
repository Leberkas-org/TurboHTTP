using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class PushLimitingTests
{
    private static readonly List<(string Name, string Value)> ValidHeaders = new()
    {
        (":method", "GET"),
        (":scheme", "https"),
        (":path", "/resource"),
    };

    // ───────────── Construction ─────────────

    [Fact(DisplayName = "RFC9114-10.5-PL-001: Default push limit is 100")]
    public void DefaultLimit_Is100()
    {
        var limiter = new Http3PushLimiter();

        Assert.Equal(Http3PushLimiter.DefaultMaxPushCount, limiter.MaxPushCount);
        Assert.Equal(100, limiter.MaxPushCount);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-002: Custom push limit accepted")]
    public void CustomLimit_Accepted()
    {
        var limiter = new Http3PushLimiter(50);

        Assert.Equal(50, limiter.MaxPushCount);
        Assert.Equal(0, limiter.PushCount);
        Assert.Equal(50, limiter.Remaining);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-003: Zero push limit means no pushes accepted")]
    public void ZeroLimit_NoPushesAccepted()
    {
        var limiter = new Http3PushLimiter(0);

        Assert.Equal(0, limiter.MaxPushCount);
        Assert.True(limiter.IsExhausted);
        Assert.Equal(0, limiter.Remaining);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-004: Negative push limit throws")]
    public void NegativeLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3PushLimiter(-1));
    }

    // ───────────── RecordPush ─────────────

    [Fact(DisplayName = "RFC9114-10.5-PL-005: RecordPush increments count")]
    public void RecordPush_IncrementsCount()
    {
        var limiter = new Http3PushLimiter(10);

        limiter.RecordPush();

        Assert.Equal(1, limiter.PushCount);
        Assert.Equal(9, limiter.Remaining);
        Assert.False(limiter.IsExhausted);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-006: RecordPush up to limit succeeds")]
    public void RecordPush_UpToLimit_Succeeds()
    {
        var limiter = new Http3PushLimiter(5);

        for (var i = 0; i < 5; i++)
        {
            limiter.RecordPush();
        }

        Assert.Equal(5, limiter.PushCount);
        Assert.Equal(0, limiter.Remaining);
        Assert.True(limiter.IsExhausted);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-007: RecordPush beyond limit is H3_EXCESSIVE_LOAD")]
    public void RecordPush_BeyondLimit_ThrowsExcessiveLoad()
    {
        var limiter = new Http3PushLimiter(3);

        limiter.RecordPush();
        limiter.RecordPush();
        limiter.RecordPush();

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
        Assert.Contains("push limit", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-008: Zero limit rejects first push with H3_EXCESSIVE_LOAD")]
    public void ZeroLimit_FirstPush_ThrowsExcessiveLoad()
    {
        var limiter = new Http3PushLimiter(0);

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-009: Single push limit allows exactly one")]
    public void SinglePushLimit_AllowsExactlyOne()
    {
        var limiter = new Http3PushLimiter(1);

        limiter.RecordPush();
        Assert.True(limiter.IsExhausted);

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    // ───────────── RecommendedMaxPushId ─────────────

    [Fact(DisplayName = "RFC9114-10.5-PL-010: RecommendedMaxPushId is MaxPushCount - 1")]
    public void RecommendedMaxPushId_IsCountMinusOne()
    {
        var limiter = new Http3PushLimiter(100);

        Assert.Equal(99, limiter.RecommendedMaxPushId);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-011: RecommendedMaxPushId is -1 when limit is zero")]
    public void RecommendedMaxPushId_ZeroLimit_IsNegativeOne()
    {
        var limiter = new Http3PushLimiter(0);

        Assert.Equal(-1, limiter.RecommendedMaxPushId);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-012: RecommendedMaxPushId is 0 when limit is 1")]
    public void RecommendedMaxPushId_SingleLimit_IsZero()
    {
        var limiter = new Http3PushLimiter(1);

        Assert.Equal(0, limiter.RecommendedMaxPushId);
    }

    // ───────────── Integration with validator ─────────────

    [Fact(DisplayName = "RFC9114-10.5-PL-013: Limiter and validator work together for valid pushes")]
    public void LimiterAndValidator_ValidPushes_Succeed()
    {
        var limiter = new Http3PushLimiter(5);
        var maxPushIdHandler = new Http3MaxPushIdHandler();
        maxPushIdHandler.CreateMaxPushId(limiter.RecommendedMaxPushId);
        var validator = new Http3PushPromiseValidator(maxPushIdHandler);

        for (var i = 0; i < 5; i++)
        {
            var frame = new Http3PushPromiseFrame(i, ReadOnlyMemory<byte>.Empty);
            validator.Validate(frame, ValidHeaders);
            limiter.RecordPush();
        }

        Assert.Equal(5, limiter.PushCount);
        Assert.Equal(5, validator.UsedPushIdCount);
        Assert.True(limiter.IsExhausted);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-014: Limiter triggers before validator when limit reached")]
    public void Limiter_TriggersExcessiveLoad_AfterLimitReached()
    {
        var limiter = new Http3PushLimiter(3);
        var maxPushIdHandler = new Http3MaxPushIdHandler();
        maxPushIdHandler.CreateMaxPushId(10); // protocol allows more than DoS limit

        // Record 3 pushes (at limit)
        for (var i = 0; i < 3; i++)
        {
            limiter.RecordPush();
        }

        // 4th push triggers H3_EXCESSIVE_LOAD even though push ID 3 is within MAX_PUSH_ID
        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-015: MAX_PUSH_ID aligned with limiter prevents over-push")]
    public void MaxPushIdAlignedWithLimiter_ConsistentLimits()
    {
        var limiter = new Http3PushLimiter(10);
        var maxPushIdHandler = new Http3MaxPushIdHandler();

        // Align MAX_PUSH_ID with limiter's recommendation
        var frame = maxPushIdHandler.CreateMaxPushId(limiter.RecommendedMaxPushId);

        Assert.Equal(9, frame.PushId);
        Assert.Equal(9, maxPushIdHandler.CurrentMaxPushId);

        // Push IDs 0-9 are valid (10 total = MaxPushCount)
        Assert.True(maxPushIdHandler.IsPushIdAllowed(0));
        Assert.True(maxPushIdHandler.IsPushIdAllowed(9));
        Assert.False(maxPushIdHandler.IsPushIdAllowed(10));
    }

    // ───────────── Excessive push flood scenario ─────────────

    [Fact(DisplayName = "RFC9114-10.5-PL-016: Large flood of pushes detected at configured boundary")]
    public void PushFlood_DetectedAtBoundary()
    {
        var limiter = new Http3PushLimiter(1000);

        for (var i = 0; i < 1000; i++)
        {
            limiter.RecordPush();
        }

        Assert.True(limiter.IsExhausted);
        Assert.Equal(0, limiter.Remaining);

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-10.5-PL-017: Multiple excess pushes all throw H3_EXCESSIVE_LOAD")]
    public void MultipleExcessPushes_AllThrow()
    {
        var limiter = new Http3PushLimiter(2);
        limiter.RecordPush();
        limiter.RecordPush();

        for (var i = 0; i < 5; i++)
        {
            var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
            Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
        }

        // Count doesn't increase past limit
        Assert.Equal(2, limiter.PushCount);
    }
}
