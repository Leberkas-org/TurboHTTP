using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class CancelPushTests
{
    // ───────────── CancelPush — basic creation ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-001: CancelPush creates frame with correct push ID")]
    public void CancelPush_CreatesFrame_WithCorrectPushId()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        var frame = handler.CancelPush(5);

        Assert.NotNull(frame);
        Assert.Equal(Http3FrameType.CancelPush, frame.Type);
        Assert.Equal(5, frame.PushId);
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-002: CancelPush with push ID 0 succeeds")]
    public void CancelPush_PushIdZero_Succeeds()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(0);
        var handler = new Http3CancelPushHandler(maxHandler);

        var frame = handler.CancelPush(0);

        Assert.Equal(0, frame.PushId);
        Assert.True(handler.IsCancelled(0));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-003: CancelPush tracks cancelled count")]
    public void CancelPush_TracksCancelledCount()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        Assert.Equal(0, handler.CancelledCount);

        handler.CancelPush(1);
        Assert.Equal(1, handler.CancelledCount);

        handler.CancelPush(2);
        Assert.Equal(2, handler.CancelledCount);

        handler.CancelPush(3);
        Assert.Equal(3, handler.CancelledCount);
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-004: IsCancelled returns true for cancelled push IDs")]
    public void IsCancelled_ReturnsTrue_ForCancelledPushId()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        handler.CancelPush(5);

        Assert.True(handler.IsCancelled(5));
        Assert.False(handler.IsCancelled(3));
    }

    // ───────────── CancelPush — unknown push ID is not an error ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-005: CancelPush for unknown push ID is not an error")]
    public void CancelPush_UnknownPushId_NotAnError()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(100);
        var handler = new Http3CancelPushHandler(maxHandler);

        // Cancelling a push ID that was never promised is explicitly allowed (§7.2.3)
        var frame = handler.CancelPush(42);

        Assert.NotNull(frame);
        Assert.Equal(42, frame.PushId);
        Assert.True(handler.IsCancelled(42));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-006: HandleReceivedCancelPush for unknown push ID is not an error")]
    public void HandleReceivedCancelPush_UnknownPushId_NotAnError()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(100);
        var handler = new Http3CancelPushHandler(maxHandler);

        // Receiving CANCEL_PUSH for unknown push ID must not be treated as connection error
        var frame = new Http3CancelPushFrame(77);
        handler.HandleReceivedCancelPush(frame);

        Assert.True(handler.IsCancelled(77));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-007: CancelPush for not-yet-promised push ID succeeds")]
    public void CancelPush_NotYetPromised_Succeeds()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(50);
        var handler = new Http3CancelPushHandler(maxHandler);

        // Client may cancel before receiving the PUSH_PROMISE
        var exception = Record.Exception(() => handler.CancelPush(25));

        Assert.Null(exception);
        Assert.True(handler.IsCancelled(25));
    }

    // ───────────── CancelPush — idempotent cancellation ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-008: CancelPush same push ID twice is idempotent")]
    public void CancelPush_SamePushIdTwice_Idempotent()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        handler.CancelPush(5);
        var frame = handler.CancelPush(5);

        Assert.Equal(5, frame.PushId);
        Assert.Equal(1, handler.CancelledCount);  // Still just one entry
    }

    // ───────────── CancelPush — validation ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-009: CancelPush rejects negative push ID")]
    public void CancelPush_NegativePushId_Throws()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => handler.CancelPush(-1));
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-010: CancelPush exceeding MAX_PUSH_ID is H3_ID_ERROR")]
    public void CancelPush_ExceedsMaxPushId_ThrowsIdError()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(5);
        var handler = new Http3CancelPushHandler(maxHandler);

        var ex = Assert.Throws<Http3Exception>(() => handler.CancelPush(6));
        Assert.Equal(Http3ErrorCode.IdError, ex.ErrorCode);
        Assert.Contains("MAX_PUSH_ID", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-011: CancelPush at MAX_PUSH_ID boundary succeeds")]
    public void CancelPush_AtMaxPushIdBoundary_Succeeds()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var handler = new Http3CancelPushHandler(maxHandler);

        var frame = handler.CancelPush(10);

        Assert.Equal(10, frame.PushId);
        Assert.True(handler.IsCancelled(10));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-012: Constructor rejects null maxPushIdHandler")]
    public void Constructor_NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Http3CancelPushHandler(null!));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-013: HandleReceivedCancelPush rejects null frame")]
    public void HandleReceivedCancelPush_NullFrame_Throws()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        var handler = new Http3CancelPushHandler(maxHandler);

        Assert.Throws<ArgumentNullException>(() => handler.HandleReceivedCancelPush(null!));
    }

    // ───────────── CancelPush — no MAX_PUSH_ID sent yet ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-014: CancelPush before MAX_PUSH_ID allows any push ID")]
    public void CancelPush_BeforeMaxPushId_AllowsAnyPushId()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        var handler = new Http3CancelPushHandler(maxHandler);

        // Before MAX_PUSH_ID is sent, the client can still cancel
        // because the validation only applies when a limit is set
        var frame = handler.CancelPush(99);

        Assert.Equal(99, frame.PushId);
    }

    // ───────────── CancelPush — integration with PushPromiseValidator ─────────────

    [Fact(DisplayName = "RFC9114-7.2.3-CP-015: CancelPush after promise cancels unwanted push")]
    public void CancelPush_AfterPromise_CancelsUnwantedPush()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(10);
        var pushValidator = new Http3PushPromiseValidator(maxHandler);
        var cancelHandler = new Http3CancelPushHandler(maxHandler);

        // Server sends a PUSH_PROMISE
        var promiseFrame = new Http3PushPromiseFrame(3, ReadOnlyMemory<byte>.Empty);
        var headers = new[]
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":path", "/resource")
        };
        pushValidator.Validate(promiseFrame, headers);

        // Client decides it doesn't want the push
        var cancelFrame = cancelHandler.CancelPush(3);

        Assert.Equal(3, cancelFrame.PushId);
        Assert.True(cancelHandler.IsCancelled(3));
    }

    [Fact(DisplayName = "RFC9114-7.2.3-CP-016: CancelPush serializes correctly")]
    public void CancelPush_Frame_SerializesCorrectly()
    {
        var maxHandler = new Http3MaxPushIdHandler();
        maxHandler.CreateMaxPushId(100);
        var handler = new Http3CancelPushHandler(maxHandler);

        var frame = handler.CancelPush(42);

        // Verify the frame can be serialized
        var buffer = new byte[frame.SerializedSize];
        var span = buffer.AsSpan();
        var written = frame.WriteTo(ref span);

        Assert.Equal(frame.SerializedSize, written);
        Assert.True(written > 0);
    }
}
