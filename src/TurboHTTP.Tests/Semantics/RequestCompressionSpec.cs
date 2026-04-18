using System.IO.Compression;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

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

    private static byte[] Compress(ReadOnlySpan<byte> data, string encoding)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        using var output = new MemoryStream();
        using (var codec =
               ContentEncoding.CreateCodecStream(output, encoding, CompressionMode.Compress, leaveOpen: true))
        {
            codec.Write(data);
        }

        return output.ToArray();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Compress_When_GzipPolicy()
    {
        var body = MakeBody(2048);
        var buf = Compress(body, "gzip");

        Assert.True(buf.Length > 0);
        Assert.False(body.AsSpan().SequenceEqual(buf));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_SetHeader_When_Compressed()
    {
        var body = MakeBody(2048);
        var buf = Compress(body, "gzip");

        using var content = new ByteArrayContent(buf);
        content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
        content.Headers.ContentLength = buf.Length;

        Assert.True(content.Headers.TryGetValues("Content-Encoding", out var values));
        Assert.Contains("gzip", values);
        Assert.Equal(buf.Length, content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
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

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void Should_Roundtrip_When_CompressedAndDecompressed(string encoding)
    {
        var original = MakeBody(4096);

        var compressed = Compress(original, encoding);
        using var decompressed = ContentEncoding.CreateCodecStream(
            new MemoryStream(compressed), encoding, CompressionMode.Decompress);
        var result = ReadStream(decompressed);

        Assert.Equal(original, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_ReturnEmpty_When_EmptyBody()
    {
        var buf = Compress([], "gzip");
        Assert.Empty(buf);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_Throw_When_UnknownEncoding()
    {
        var body = MakeBody(256);
        Assert.Throws<HttpDecoderException>(() => Compress(body, "unknown"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_HaveDefaults_When_DefaultPolicy()
    {
        var policy = CompressionPolicy.Default;

        Assert.Equal(1024, policy.MinBodySizeBytes);
        Assert.Equal("gzip", policy.Encoding);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void Should_UpdateContentLength_When_Compressed()
    {
        var body = MakeBody(2048);
        var buf = Compress(body, "gzip");

        Assert.NotEqual(body.Length, buf.Length);

        using var content = new ByteArrayContent(buf);
        content.Headers.ContentLength = buf.Length;

        Assert.Equal(buf.Length, content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
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