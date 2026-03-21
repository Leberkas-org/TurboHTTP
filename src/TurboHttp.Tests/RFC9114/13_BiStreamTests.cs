using System;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class BiStreamTests
{
    // --- Stream ID Allocation ---

    [Fact(DisplayName = "RFC9114-6.1-BS-001: Client-initiated bidi stream IDs are multiples of 4")]
    public void Allocator_ProducesMultiplesOfFour()
    {
        var allocator = new Http3StreamIdAllocator();

        Assert.Equal(0, allocator.Allocate());
        Assert.Equal(4, allocator.Allocate());
        Assert.Equal(8, allocator.Allocate());
        Assert.Equal(12, allocator.Allocate());
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-002: AllocateStream returns stream with correct ID")]
    public void AllocateStream_ReturnsStreamWithCorrectId()
    {
        var allocator = new Http3StreamIdAllocator();

        var stream0 = allocator.AllocateStream();
        var stream4 = allocator.AllocateStream();

        Assert.Equal(0, stream0.StreamId);
        Assert.Equal(4, stream4.StreamId);
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-003: Stream ID must be non-negative")]
    public void RequestStream_NegativeId_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(-1));
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-004: Stream ID must be divisible by 4")]
    public void RequestStream_NonClientBidiId_Throws()
    {
        // 1 = server-initiated bidi, 2 = client-initiated uni, 3 = server-initiated uni
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(5));
    }

    // --- Full Lifecycle ---

    [Fact(DisplayName = "RFC9114-6.1-BS-005: Full request-response lifecycle: Open → HeadersSent → HalfClosedLocal → ResponseHeadersReceived → Closed")]
    public void FullLifecycle_TransitionsCorrectly()
    {
        var stream = new Http3RequestStream(0);
        Assert.Equal(Http3RequestStreamState.Open, stream.State);
        Assert.True(stream.IsActive);

        stream.OnHeadersSent();
        Assert.Equal(Http3RequestStreamState.HeadersSent, stream.State);
        Assert.True(stream.IsActive);

        stream.OnRequestComplete();
        Assert.Equal(Http3RequestStreamState.HalfClosedLocal, stream.State);
        Assert.True(stream.IsActive);

        stream.OnResponseHeadersReceived();
        Assert.Equal(Http3RequestStreamState.ResponseHeadersReceived, stream.State);
        Assert.True(stream.IsActive);

        stream.OnResponseComplete();
        Assert.Equal(Http3RequestStreamState.Closed, stream.State);
        Assert.True(stream.IsClosed);
        Assert.False(stream.IsActive);
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-006: Half-close after request sent, full close after response")]
    public void HalfClose_AfterRequest_FullClose_AfterResponse()
    {
        var stream = new Http3RequestStream(4);

        stream.OnHeadersSent();
        stream.OnRequestComplete();

        // Half-closed local — waiting for response
        Assert.Equal(Http3RequestStreamState.HalfClosedLocal, stream.State);
        Assert.True(stream.IsActive);
        Assert.False(stream.IsClosed);

        stream.OnResponseHeadersReceived();
        stream.OnResponseComplete();

        // Fully closed after response
        Assert.True(stream.IsClosed);
        Assert.False(stream.IsActive);
    }

    // --- Invalid Transitions ---

    [Fact(DisplayName = "RFC9114-6.1-BS-007: Cannot send headers twice")]
    public void OnHeadersSent_InWrongState_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();

        Assert.Throws<InvalidOperationException>(() => stream.OnHeadersSent());
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-008: Cannot complete request before sending headers")]
    public void OnRequestComplete_BeforeHeaders_Throws()
    {
        var stream = new Http3RequestStream(0);

        Assert.Throws<InvalidOperationException>(() => stream.OnRequestComplete());
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-009: Cannot receive response headers before half-close")]
    public void OnResponseHeadersReceived_BeforeHalfClose_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();

        Assert.Throws<InvalidOperationException>(() => stream.OnResponseHeadersReceived());
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-010: Cannot complete response before receiving response headers")]
    public void OnResponseComplete_BeforeResponseHeaders_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();
        stream.OnRequestComplete();

        Assert.Throws<InvalidOperationException>(() => stream.OnResponseComplete());
    }

    // --- Reset ---

    [Fact(DisplayName = "RFC9114-6.1-BS-011: Stream can be reset from any active state")]
    public void OnReset_FromActiveState_TransitionsToReset()
    {
        var stream = new Http3RequestStream(0);
        stream.OnReset(Http3ErrorCode.RequestCancelled);

        Assert.True(stream.IsReset);
        Assert.False(stream.IsActive);
        Assert.False(stream.IsClosed);
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-012: Cannot reset an already closed stream")]
    public void OnReset_FromClosedState_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();
        stream.OnRequestComplete();
        stream.OnResponseHeadersReceived();
        stream.OnResponseComplete();

        Assert.Throws<InvalidOperationException>(() =>
            stream.OnReset(Http3ErrorCode.RequestCancelled));
    }

    [Fact(DisplayName = "RFC9114-6.1-BS-013: Cannot reset an already reset stream")]
    public void OnReset_FromResetState_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnReset(Http3ErrorCode.RequestCancelled);

        Assert.Throws<InvalidOperationException>(() =>
            stream.OnReset(Http3ErrorCode.RequestCancelled));
    }

    // --- Request mapped to single bidi stream ---

    [Fact(DisplayName = "RFC9114-6.1-BS-014: Each request uses a new unique stream")]
    public void EachRequest_UsesUniqueStream()
    {
        var allocator = new Http3StreamIdAllocator();

        var s1 = allocator.AllocateStream();
        var s2 = allocator.AllocateStream();
        var s3 = allocator.AllocateStream();

        Assert.NotEqual(s1.StreamId, s2.StreamId);
        Assert.NotEqual(s2.StreamId, s3.StreamId);
        Assert.NotEqual(s1.StreamId, s3.StreamId);

        // All are even (divisible by 4 implies even)
        Assert.Equal(0, s1.StreamId % 2);
        Assert.Equal(0, s2.StreamId % 2);
        Assert.Equal(0, s3.StreamId % 2);
    }
}
