using System.Buffers;
using TurboHTTP.Features.Caching;

namespace TurboHTTP.Tests.Features.Caching;

public sealed class CacheBodySpec
{
    [Fact(Timeout = 5000)]
    public void CacheBody_should_expose_span_of_correct_length()
    {
        var owner = MemoryPool<byte>.Shared.Rent(100);
        "hello"u8.CopyTo(owner.Memory.Span);
        var body = new CacheBody(owner, 5);

        Assert.Equal(5, body.Length);
        Assert.Equal(5, body.Span.Length);
        Assert.Equal((byte)'h', body.Span[0]);
    }

    [Fact(Timeout = 5000)]
    public void CacheBody_should_expose_memory_of_correct_length()
    {
        var owner = MemoryPool<byte>.Shared.Rent(100);
        "world"u8.CopyTo(owner.Memory.Span);
        var body = new CacheBody(owner, 5);

        Assert.Equal(5, body.Memory.Length);
        Assert.Equal((byte)'w', body.Memory.Span[0]);
    }

    [Fact(Timeout = 5000)]
    public void CacheBody_should_report_empty_when_length_zero()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var body = new CacheBody(owner, 0);

        Assert.True(body.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void CacheBody_should_report_not_empty_when_has_data()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        "x"u8.CopyTo(owner.Memory.Span);
        var body = new CacheBody(owner, 1);

        Assert.False(body.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void CacheBody_should_return_empty_span_after_dispose()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        "test"u8.CopyTo(owner.Memory.Span);
        var body = new CacheBody(owner, 4);

        body.Dispose();

        Assert.Equal(0, body.Span.Length);
        Assert.True(body.Memory.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void CacheBody_should_be_safe_to_dispose_twice()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var body = new CacheBody(owner, 4);

        body.Dispose();
        body.Dispose();
    }
}