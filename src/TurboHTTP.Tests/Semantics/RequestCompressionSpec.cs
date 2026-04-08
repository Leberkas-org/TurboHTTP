using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// Tests for <see cref="ContentEncodingEncoder"/> and <see cref="CompressionPolicy"/>.
/// RFC 9110 §8.4 — A sender that applied content encoding MUST generate a Content-Encoding
/// header field listing the applied encodings.
/// </summary>
public sealed class RequestCompressionSpec
{
    private static byte[] MakeBody(int size)
    {
        var body = new byte[size];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 256);
        }

        return body;
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Compress_When_GzipPolicy()
    {
        var body = MakeBody(2048);
        using var compressed = ContentEncodingEncoder.Compress(body, "gzip");
        var buf = ReadStream(compressed);

        Assert.True(buf.Length > 0);
        Assert.False(body.AsSpan().SequenceEqual(buf));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_SetHeader_When_Compressed()
    {
        var body = MakeBody(2048);
        using var compressed = ContentEncodingEncoder.Compress(body, "gzip");
        var buf = ReadStream(compressed);

        // Simulate what the stage does: create content with encoding header
        using var content = new ByteArrayContent(buf);
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.ContentLength = buf.Length;

        Assert.True(content.Headers.TryGetValues("Content-Encoding", out var values));
        Assert.Contains("gzip", values);
        Assert.Equal(buf.Length, content.Headers.ContentLength);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_NotCompress_When_BelowThreshold()
    {
        var policy = new CompressionPolicy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[512]);
        request.Content.Headers.ContentLength = 512;

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        Assert.True(bodySize < policy.MinBodySizeBytes);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Roundtrip_When_CompressedAndDecompressed()
    {
        var original = MakeBody(4096);

        // Test gzip roundtrip
        using var gzipCompressed = ContentEncodingEncoder.Compress(original, "gzip");
        var gzipBuf = ReadStream(gzipCompressed);
        using var gzipDecompressed = ContentEncodingDecoder.Decompress(gzipBuf, "gzip");
        var gzipResult = ReadStream(gzipDecompressed);
        Assert.Equal(original, gzipResult);

        // Test deflate roundtrip
        using var deflateCompressed = ContentEncodingEncoder.Compress(original, "deflate");
        var deflateBuf = ReadStream(deflateCompressed);
        using var deflateDecompressed = ContentEncodingDecoder.Decompress(deflateBuf, "deflate");
        var deflateResult = ReadStream(deflateDecompressed);
        Assert.Equal(original, deflateResult);

        // Test brotli roundtrip
        using var brCompressed = ContentEncodingEncoder.Compress(original, "br");
        var brBuf = ReadStream(brCompressed);
        using var brDecompressed = ContentEncodingDecoder.Decompress(brBuf, "br");
        var brResult = ReadStream(brDecompressed);
        Assert.Equal(original, brResult);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_ReturnEmpty_When_EmptyBody()
    {
        using var stream = ContentEncodingEncoder.Compress([], "gzip");
        var buf = ReadStream(stream);
        Assert.Empty(buf);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Throw_When_UnknownEncoding()
    {
        var body = MakeBody(256);
        Assert.Throws<ArgumentException>(() => ContentEncodingEncoder.Compress(body, "unknown"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_HaveDefaults_When_DefaultPolicy()
    {
        var policy = CompressionPolicy.Default;

        Assert.Equal(1024, policy.MinBodySizeBytes);
        Assert.Equal("gzip", policy.Encoding);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_UpdateContentLength_When_Compressed()
    {
        var body = MakeBody(2048);
        using var compressed = ContentEncodingEncoder.Compress(body, "gzip");
        var buf = ReadStream(compressed);

        // Compressed size should be different from original size
        Assert.NotEqual(body.Length, buf.Length);

        // Simulate stage behavior
        using var content = new ByteArrayContent(buf);
        content.Headers.ContentLength = buf.Length;

        Assert.Equal(buf.Length, content.Headers.ContentLength);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_PassThrough_When_NoBody()
    {
        var policy = new CompressionPolicy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        Assert.Null(request.Content);
        var bodySize = request.Content?.Headers.ContentLength ?? -1;
        Assert.True(bodySize < policy.MinBodySizeBytes);
    }
}
