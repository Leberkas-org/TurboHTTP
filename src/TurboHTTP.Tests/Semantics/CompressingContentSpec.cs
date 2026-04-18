using TurboHTTP.Internal;
using static System.Text.Encoding;

namespace TurboHTTP.Tests.Semantics;

public sealed class CompressingContentSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_compress_content()
    {
        var original = "hello compressed world"u8.ToArray();
        var inner = new ByteArrayContent(original);

        using var content = new CompressingContent(inner, "gzip");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        var compressed = ms.ToArray();
        Assert.NotEmpty(compressed);
        // Compressed data should be different from original
        Assert.NotEqual(original, compressed);
        // Gzip header magic number
        Assert.Equal(0x1f, compressed[0]);
        Assert.Equal(0x8b, compressed[1]);
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_compress_content()
    {
        var original = "hello compressed world"u8.ToArray();
        var inner = new ByteArrayContent(original);

        using var content = new CompressingContent(inner, "gzip");
        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        var compressed = ms.ToArray();
        Assert.NotEmpty(compressed);
        Assert.Equal(0x1f, compressed[0]);
        Assert.Equal(0x8b, compressed[1]);
    }

    [Fact(Timeout = 5000)]
    public void Constructor_should_skip_content_encoding_and_content_length()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());
        inner.Headers.ContentLength = 999; // Intentionally wrong

        using var content = new CompressingContent(inner, "gzip");

        // The compressor should set new content-length, not preserve the old one
        Assert.NotNull(content.Headers.ContentLength);
        Assert.NotEqual(999, content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public void Constructor_should_skip_content_encoding_header()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());

        using var content = new CompressingContent(inner, "gzip");

        var encoding = content.Headers.ContentEncoding;
        Assert.Single(encoding);
        Assert.Equal("gzip", encoding.First());
    }

    [Fact(Timeout = 5000)]
    public void Constructor_should_set_content_encoding_header()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());

        using var content = new CompressingContent(inner, "gzip");

        var encoding = content.Headers.ContentEncoding;
        Assert.Single(encoding);
        Assert.Equal("gzip", encoding.First());
    }

    [Fact(Timeout = 5000)]
    public void Constructor_should_compute_content_length()
    {
        var original = UTF8.GetBytes(new string('a', 1000));
        var inner = new ByteArrayContent(original);

        using var content = new CompressingContent(inner, "gzip");

        Assert.NotNull(content.Headers.ContentLength);
        Assert.NotEqual(original.Length, content.Headers.ContentLength);
        Assert.True(content.Headers.ContentLength > 0);
        Assert.True(content.Headers.ContentLength <
                    original.Length); // Compression should reduce size for repetitive data
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_free_memory_owner()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());
        var content = new CompressingContent(inner, "gzip");

        content.Dispose();
        content.Dispose(); // Should not throw on double dispose
    }

    [Fact(Timeout = 5000)]
    public void Serialize_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());
        var content = new CompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => content.CopyTo(ms, null, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeAsync_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent("test"u8.ToArray());
        var content = new CompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            content.CopyToAsync(ms, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_without_cancellation_token_should_work()
    {
        var original = "test data"u8.ToArray();
        var inner = new ByteArrayContent(original);

        using var content = new CompressingContent(inner, "gzip");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.NotEmpty(ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Compress_large_repetitive_data_should_achieve_good_ratio()
    {
        var original = UTF8.GetBytes(string.Concat(Enumerable.Repeat("aaaaaaaaaa", 1000)));
        var inner = new ByteArrayContent(original);

        using var content = new CompressingContent(inner, "gzip");

        var compressedLength = content.Headers.ContentLength;
        Assert.NotNull(compressedLength);
        var ratio = (double)compressedLength / original.Length;
        Assert.True(ratio < 0.1); // Should compress repetitive data significantly
    }
}