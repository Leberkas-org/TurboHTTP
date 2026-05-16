using System.IO.Compression;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Protocol.Semantics;

public sealed class DecompressingContentEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_decompress_deflate()
    {
        var original = "deflate test data"u8.ToArray();
        var compressed = ZLibCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "deflate");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_decompress_deflate()
    {
        var original = "deflate sync test"u8.ToArray();
        var compressed = ZLibCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "deflate");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_silently_handle_corrupt_gzip()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF };
        var inner = new ByteArrayContent(corrupt);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(0, ms.Length);
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_silently_handle_corrupt_gzip()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF };
        var inner = new ByteArrayContent(corrupt);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(0, ms.Length);
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_decompress_brotli()
    {
        var original = "brotli test data"u8.ToArray();
        var compressed = BrotliCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "br");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_decompress_brotli()
    {
        var original = "brotli sync test"u8.ToArray();
        var compressed = BrotliCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "br");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(original, ms.ToArray());
    }

    private static byte[] ZLibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zl = new ZLibStream(ms, CompressionLevel.Fastest))
        {
            zl.Write(data);
        }

        return ms.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var br = new BrotliStream(ms, CompressionLevel.Fastest))
        {
            br.Write(data);
        }

        return ms.ToArray();
    }
}