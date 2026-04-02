using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Connection;

public sealed class PushLimitingSpec
{
    private static readonly List<(string Name, string Value)> ValidHeaders = new()
    {
        (":method", "GET"),
        (":scheme", "https"),
        (":path", "/resource"),
    };


    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void DefaultLimit_Is100()
    {
        var limiter = new Http3PushLimiter();

        Assert.Equal(Http3PushLimiter.DefaultMaxPushCount, limiter.MaxPushCount);
        Assert.Equal(100, limiter.MaxPushCount);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void CustomLimit_Accepted()
    {
        var limiter = new Http3PushLimiter(50);

        Assert.Equal(50, limiter.MaxPushCount);
        Assert.Equal(0, limiter.PushCount);
        Assert.Equal(50, limiter.Remaining);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void ZeroLimit_NoPushesAccepted()
    {
        var limiter = new Http3PushLimiter(0);

        Assert.Equal(0, limiter.MaxPushCount);
        Assert.True(limiter.IsExhausted);
        Assert.Equal(0, limiter.Remaining);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void NegativeLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3PushLimiter(-1));
    }


    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void RecordPush_IncrementsCount()
    {
        var limiter = new Http3PushLimiter(10);

        limiter.RecordPush();

        Assert.Equal(1, limiter.PushCount);
        Assert.Equal(9, limiter.Remaining);
        Assert.False(limiter.IsExhausted);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void ZeroLimit_FirstPush_ThrowsExcessiveLoad()
    {
        var limiter = new Http3PushLimiter(0);

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void SinglePushLimit_AllowsExactlyOne()
    {
        var limiter = new Http3PushLimiter(1);

        limiter.RecordPush();
        Assert.True(limiter.IsExhausted);

        var ex = Assert.Throws<Http3Exception>(() => limiter.RecordPush());
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }


    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void RecommendedMaxPushId_IsCountMinusOne()
    {
        var limiter = new Http3PushLimiter(100);

        Assert.Equal(99, limiter.RecommendedMaxPushId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void RecommendedMaxPushId_ZeroLimit_IsNegativeOne()
    {
        var limiter = new Http3PushLimiter(0);

        Assert.Equal(-1, limiter.RecommendedMaxPushId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
    public void RecommendedMaxPushId_SingleLimit_IsZero()
    {
        var limiter = new Http3PushLimiter(1);

        Assert.Equal(0, limiter.RecommendedMaxPushId);
    }


    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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


    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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

    [Fact]
    [Trait("RFC", "RFC9114-10.5")]
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
