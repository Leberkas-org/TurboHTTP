using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Streams;

public sealed class BiStreamSpec
{
    // --- Stream ID Allocation ---

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void Allocator_ProducesMultiplesOfFour()
    {
        var allocator = new Http3StreamIdAllocator();

        Assert.Equal(0, allocator.Allocate());
        Assert.Equal(4, allocator.Allocate());
        Assert.Equal(8, allocator.Allocate());
        Assert.Equal(12, allocator.Allocate());
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void AllocateStream_ReturnsStreamWithCorrectId()
    {
        var allocator = new Http3StreamIdAllocator();

        var stream0 = allocator.AllocateStream();
        var stream4 = allocator.AllocateStream();

        Assert.Equal(0, stream0.StreamId);
        Assert.Equal(4, stream4.StreamId);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void RequestStream_NegativeId_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(-1));
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void RequestStream_NonClientBidiId_Throws()
    {
        // 1 = server-initiated bidi, 2 = client-initiated uni, 3 = server-initiated uni
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Http3RequestStream(5));
    }

    // --- Full Lifecycle ---

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnHeadersSent_InWrongState_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();

        Assert.Throws<InvalidOperationException>(() => stream.OnHeadersSent());
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnRequestComplete_BeforeHeaders_Throws()
    {
        var stream = new Http3RequestStream(0);

        Assert.Throws<InvalidOperationException>(() => stream.OnRequestComplete());
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnResponseHeadersReceived_BeforeHalfClose_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();

        Assert.Throws<InvalidOperationException>(() => stream.OnResponseHeadersReceived());
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnResponseComplete_BeforeResponseHeaders_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnHeadersSent();
        stream.OnRequestComplete();

        Assert.Throws<InvalidOperationException>(() => stream.OnResponseComplete());
    }

    // --- Reset ---

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnReset_FromActiveState_TransitionsToReset()
    {
        var stream = new Http3RequestStream(0);
        stream.OnReset(Http3ErrorCode.RequestCancelled);

        Assert.True(stream.IsReset);
        Assert.False(stream.IsActive);
        Assert.False(stream.IsClosed);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
    public void OnReset_FromResetState_Throws()
    {
        var stream = new Http3RequestStream(0);
        stream.OnReset(Http3ErrorCode.RequestCancelled);

        Assert.Throws<InvalidOperationException>(() =>
            stream.OnReset(Http3ErrorCode.RequestCancelled));
    }

    // --- Request mapped to single bidi stream ---

    [Fact]
    [Trait("RFC", "RFC9114-6.1")]
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
