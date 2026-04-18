using System.Buffers;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Semantics;

public sealed class PooledBodyContentSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_write_correct_bytes()
    {
        var data = "hello world"u8.ToArray();
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);

        using var content = new PooledBodyContent(owner, data.Length);
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(data, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_write_correct_bytes()
    {
        var data = "hello world"u8.ToArray();
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory.Span);

        using var content = new PooledBodyContent(owner, data.Length);
        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(data, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Serialize_after_dispose_should_throw_ObjectDisposedException()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();

        using var ms = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => content.CopyTo(ms, null, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeAsync_after_dispose_should_throw_ObjectDisposedException()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            content.CopyToAsync(ms, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_should_not_throw()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        var content = new PooledBodyContent(owner, 16);
        content.Dispose();
        content.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void TryComputeLength_should_return_exact_length()
    {
        var owner = MemoryPool<byte>.Shared.Rent(128);
        using var content = new PooledBodyContent(owner, 42);

        Assert.Equal(42, content.Headers.ContentLength);
    }
}