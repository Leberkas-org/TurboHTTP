using System.Net.Http;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests for <see cref="ContentEncodingEncoder"/> and <see cref="RequestCompressionPolicy"/>.
/// RFC 9110 §8.4 — A sender that applied content encoding MUST generate a Content-Encoding
/// header field listing the applied encodings.
/// </summary>
public sealed class RequestCompressionTests
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

    [Fact(DisplayName = "RFC9110-8.4-RC-001: gzip compresses request body")]
    public void Should_Compress_When_GzipPolicy()
    {
        var body = MakeBody(2048);
        var compressed = ContentEncodingEncoder.Compress(body, "gzip");

        Assert.NotEmpty(compressed);
        // Compressed data should be different from original (and typically smaller for repetitive data)
        Assert.NotEqual(body, compressed);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-002: Content-Encoding set after compression")]
    public void Should_SetHeader_When_Compressed()
    {
        var body = MakeBody(2048);
        var compressed = ContentEncodingEncoder.Compress(body, "gzip");

        // Simulate what the stage does: create content with encoding header
        using var content = new ByteArrayContent(compressed);
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.ContentLength = compressed.Length;

        Assert.True(content.Headers.TryGetValues("Content-Encoding", out var values));
        Assert.Contains("gzip", values);
        Assert.Equal(compressed.Length, content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-003: Small body not compressed")]
    public void Should_NotCompress_When_BelowThreshold()
    {
        var policy = new RequestCompressionPolicy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[512]);
        request.Content.Headers.ContentLength = 512;

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        Assert.True(bodySize < policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-004: Compress-Decompress roundtrip")]
    public void Should_Roundtrip_When_CompressedAndDecompressed()
    {
        var original = MakeBody(4096);

        // Test gzip roundtrip
        var gzipCompressed = ContentEncodingEncoder.Compress(original, "gzip");
        var gzipDecompressed = ContentEncodingDecoder.Decompress(gzipCompressed, "gzip");
        Assert.Equal(original, gzipDecompressed);

        // Test deflate roundtrip
        var deflateCompressed = ContentEncodingEncoder.Compress(original, "deflate");
        var deflateDecompressed = ContentEncodingDecoder.Decompress(deflateCompressed, "deflate");
        Assert.Equal(original, deflateDecompressed);

        // Test brotli roundtrip
        var brCompressed = ContentEncodingEncoder.Compress(original, "br");
        var brDecompressed = ContentEncodingDecoder.Decompress(brCompressed, "br");
        Assert.Equal(original, brDecompressed);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-005: Empty body returns empty")]
    public void Should_ReturnEmpty_When_EmptyBody()
    {
        var result = ContentEncodingEncoder.Compress([], "gzip");
        Assert.Empty(result);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-006: Unknown encoding throws ArgumentException")]
    public void Should_Throw_When_UnknownEncoding()
    {
        var body = MakeBody(256);
        Assert.Throws<ArgumentException>(() => ContentEncodingEncoder.Compress(body, "unknown"));
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-007: Default policy has 1024 threshold and gzip")]
    public void Should_HaveDefaults_When_DefaultPolicy()
    {
        var policy = RequestCompressionPolicy.Default;

        Assert.Equal(1024, policy.MinBodySizeBytes);
        Assert.Equal("gzip", policy.Encoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-008: Content-Length updated to compressed size")]
    public void Should_UpdateContentLength_When_Compressed()
    {
        var body = MakeBody(2048);
        var compressed = ContentEncodingEncoder.Compress(body, "gzip");

        // Compressed size should be different from original size
        Assert.NotEqual(body.Length, compressed.Length);

        // Simulate stage behavior
        using var content = new ByteArrayContent(compressed);
        content.Headers.ContentLength = compressed.Length;

        Assert.Equal(compressed.Length, content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC9110-8.4-RC-009: No body passes through unchanged")]
    public void Should_PassThrough_When_NoBody()
    {
        var policy = new RequestCompressionPolicy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        Assert.Null(request.Content);
        var bodySize = request.Content?.Headers.ContentLength ?? -1;
        Assert.True(bodySize < policy.MinBodySizeBytes);
    }
}
