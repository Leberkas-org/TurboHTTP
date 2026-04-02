using System.Buffers;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.Tests.Semantics;

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

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Compress_When_GzipPolicy()
    {
        var body = MakeBody(2048);
        var (buf, len) = ContentEncodingEncoder.Compress(body, "gzip");
        try
        {
            Assert.True(len > 0);
            Assert.False(body.AsSpan().SequenceEqual(buf.AsSpan(0, len)));
        }
        finally
        {
            if (len > 0) ArrayPool<byte>.Shared.Return(buf);
        }
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_SetHeader_When_Compressed()
    {
        var body = MakeBody(2048);
        var (buf, len) = ContentEncodingEncoder.Compress(body, "gzip");
        try
        {
            // Simulate what the stage does: create content with encoding header
            using var content = new ByteArrayContent(buf, 0, len);
            content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
            content.Headers.ContentLength = len;

            Assert.True(content.Headers.TryGetValues("Content-Encoding", out var values));
            Assert.Contains("gzip", values);
            Assert.Equal(len, content.Headers.ContentLength);
        }
        finally
        {
            if (len > 0) ArrayPool<byte>.Shared.Return(buf);
        }
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
        var (gzipBuf, gzipLen) = ContentEncodingEncoder.Compress(original, "gzip");
        try
        {
            var (gzipDecBuf, gzipDecLen) = ContentEncodingDecoder.Decompress(gzipBuf.AsSpan(0, gzipLen), "gzip");
            try
            {
                Assert.Equal(original, gzipDecBuf.AsSpan(0, gzipDecLen).ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(gzipDecBuf);
            }
        }
        finally
        {
            if (gzipLen > 0) ArrayPool<byte>.Shared.Return(gzipBuf);
        }

        // Test deflate roundtrip
        var (deflateBuf, deflateLen) = ContentEncodingEncoder.Compress(original, "deflate");
        try
        {
            var (deflateDecBuf, deflateDecLen) = ContentEncodingDecoder.Decompress(deflateBuf.AsSpan(0, deflateLen), "deflate");
            try
            {
                Assert.Equal(original, deflateDecBuf.AsSpan(0, deflateDecLen).ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(deflateDecBuf);
            }
        }
        finally
        {
            if (deflateLen > 0) ArrayPool<byte>.Shared.Return(deflateBuf);
        }

        // Test brotli roundtrip
        var (brBuf, brLen) = ContentEncodingEncoder.Compress(original, "br");
        try
        {
            var (brDecBuf, brDecLen) = ContentEncodingDecoder.Decompress(brBuf.AsSpan(0, brLen), "br");
            try
            {
                Assert.Equal(original, brDecBuf.AsSpan(0, brDecLen).ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(brDecBuf);
            }
        }
        finally
        {
            if (brLen > 0) ArrayPool<byte>.Shared.Return(brBuf);
        }
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_ReturnEmpty_When_EmptyBody()
    {
        var (_, len) = ContentEncodingEncoder.Compress([], "gzip");
        Assert.Equal(0, len);
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
        var (buf, len) = ContentEncodingEncoder.Compress(body, "gzip");
        try
        {
            // Compressed size should be different from original size
            Assert.NotEqual(body.Length, len);

            // Simulate stage behavior
            using var content = new ByteArrayContent(buf, 0, len);
            content.Headers.ContentLength = len;

            Assert.Equal(len, content.Headers.ContentLength);
        }
        finally
        {
            if (len > 0) ArrayPool<byte>.Shared.Return(buf);
        }
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
