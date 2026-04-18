using System.IO.Compression;
using TurboHTTP.Internal;
using static System.Text.Encoding;

namespace TurboHTTP.Tests.Semantics;

public sealed class DecompressingContentSpec
{
    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public async Task SerializeToStreamAsync_should_decompress_gzip()
    {
        var original = "hello compressed world"u8.ToArray();
        var compressed = GzipCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void SerializeToStream_should_decompress_gzip()
    {
        var original = "hello compressed world"u8.ToArray();
        var compressed = GzipCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void Serialize_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => content.CopyTo(ms, null, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public async Task SerializeAsync_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.CopyToAsync(ms, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void Double_dispose_should_not_throw()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();
        content.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public async Task SerializeToStreamAsync_without_cancellation_token_should_work()
    {
        var original = "test data"u8.ToArray();
        var compressed = GzipCompress(original);
        var inner = new ByteArrayContent(compressed);

        using var content = new DecompressingContent(inner, "gzip");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void Constructor_should_accept_gzip_with_large_data()
    {
        var original = UTF8.GetBytes(new string('a', 1000));
        var compressed = GzipCompress(original);
        var inner = new ByteArrayContent(compressed);

        using var content = new DecompressingContent(inner, "gzip");

        // Should be able to construct without error
        Assert.NotNull(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public async Task Decompress_large_gzip_data_should_restore_original()
    {
        var original = new byte[10000];
        Random.Shared.NextBytes(original);
        var compressed = GzipCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 10000)]
    [Trait("Coverage", "DecompressingContent")]
    public async Task Dispose_during_async_decompression_should_not_corrupt_data()
    {
        var original = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray();
        var compressed = GzipCompress(original);
        var inner = new ByteArrayContent(compressed);

        var content = new DecompressingContent(inner, "gzip");
        var gate = new SemaphoreSlim(0, 1);
        var slowStream = new GatedWriteStream(gate);

        var writeTask = content.CopyToAsync(slowStream, TestContext.Current.CancellationToken);

        await slowStream.WaitUntilWriteStarted();

        content.Dispose();

        gate.Release();
        await writeTask;

        Assert.Equal(original, slowStream.WrittenBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void Constructor_should_accept_gzip_encoding()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var inner = new ByteArrayContent(compressed);

        using var content = new DecompressingContent(inner, "gzip");
        Assert.NotNull(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("Coverage", "DecompressingContent")]
    public void Constructor_should_accept_deflate_encoding()
    {
        var original = "test"u8.ToArray();
        var compressed = DeflateCompress(original);
        var inner = new ByteArrayContent(compressed);

        using var content = new DecompressingContent(inner, "deflate");
        Assert.NotNull(content);
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
        {
            gz.Write(data);
        }

        return ms.ToArray();
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var df = new DeflateStream(ms, CompressionLevel.Fastest))
        {
            df.Write(data);
        }

        return ms.ToArray();
    }

    private sealed class GatedWriteStream(SemaphoreSlim gate) : MemoryStream
    {
        private readonly TaskCompletionSource _writeStarted = new();

        public byte[] WrittenBytes => ToArray();

        public Task WaitUntilWriteStarted() => _writeStarted.Task;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writeStarted.TrySetResult();
            await gate.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
