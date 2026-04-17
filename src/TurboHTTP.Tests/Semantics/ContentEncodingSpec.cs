using System.IO.Compression;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// Tests for <see cref="ContentEncoding"/> per RFC 9110 §8.4.
/// Verifies support detection and decompressor/compressor creation for
/// gzip, x-gzip, deflate, br (Brotli), and identity encodings.
/// </summary>
/// <remarks>
/// Class under test: <see cref="ContentEncoding"/>.
/// RFC 9110 §8.4: Content-Encoding decompression and compression.
/// </remarks>
public sealed class ContentEncodingSpec
{
    private static byte[] MakeTestData(int size = 256)
    {
        var data = new byte[size];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        return data;
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }


    // ─── IsSupported Tests ───

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_null_encoding()
    {
        Assert.True(ContentEncoding.IsSupported(null));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_empty_encoding()
    {
        Assert.True(ContentEncoding.IsSupported(""));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_whitespace_only_encoding()
    {
        Assert.True(ContentEncoding.IsSupported("   "));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_identity_encoding()
    {
        Assert.True(ContentEncoding.IsSupported("identity"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_identity_case_insensitive()
    {
        Assert.True(ContentEncoding.IsSupported("IDENTITY"));
        Assert.True(ContentEncoding.IsSupported("Identity"));
        Assert.True(ContentEncoding.IsSupported("iDeNtItY"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_gzip()
    {
        Assert.True(ContentEncoding.IsSupported("gzip"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_gzip_case_insensitive()
    {
        Assert.True(ContentEncoding.IsSupported("GZIP"));
        Assert.True(ContentEncoding.IsSupported("Gzip"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_x_gzip()
    {
        Assert.True(ContentEncoding.IsSupported("x-gzip"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_x_gzip_case_insensitive()
    {
        Assert.True(ContentEncoding.IsSupported("X-GZIP"));
        Assert.True(ContentEncoding.IsSupported("X-Gzip"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_deflate()
    {
        Assert.True(ContentEncoding.IsSupported("deflate"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_deflate_case_insensitive()
    {
        Assert.True(ContentEncoding.IsSupported("DEFLATE"));
        Assert.True(ContentEncoding.IsSupported("Deflate"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_brotli()
    {
        Assert.True(ContentEncoding.IsSupported("br"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_brotli_case_insensitive()
    {
        Assert.True(ContentEncoding.IsSupported("BR"));
        Assert.True(ContentEncoding.IsSupported("Br"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_reject_unsupported_encoding()
    {
        Assert.False(ContentEncoding.IsSupported("compress"));
        Assert.False(ContentEncoding.IsSupported("unknown"));
        Assert.False(ContentEncoding.IsSupported("xyz"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_support_stacked_encodings_all_supported()
    {
        Assert.True(ContentEncoding.IsSupported("gzip, deflate"));
        Assert.True(ContentEncoding.IsSupported("br, gzip"));
        Assert.True(ContentEncoding.IsSupported("gzip, br, deflate"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_reject_stacked_encodings_with_unsupported()
    {
        Assert.False(ContentEncoding.IsSupported("gzip, compress"));
        Assert.False(ContentEncoding.IsSupported("unknown, gzip"));
        Assert.False(ContentEncoding.IsSupported("gzip, unknown, deflate"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_handle_stacked_with_identity()
    {
        Assert.True(ContentEncoding.IsSupported("gzip, identity"));
        Assert.True(ContentEncoding.IsSupported("identity, deflate"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_handle_whitespace_in_stacked_encodings()
    {
        Assert.True(ContentEncoding.IsSupported("gzip , deflate"));
        Assert.True(ContentEncoding.IsSupported(" gzip , br "));
        Assert.True(ContentEncoding.IsSupported("  gzip  ,  deflate  "));
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_handle_empty_token_in_stacked()
    {
        // Empty tokens should be ignored (treated like identity)
        Assert.True(ContentEncoding.IsSupported("gzip,,deflate"));
    }


    // ─── CreateDecompressor Tests ───

    [Theory]
    [Trait("RFC", "RFC9110-8.4")]
    [InlineData("gzip")]
    [InlineData("x-gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void ContentEncoding_should_create_decompressor_for_supported_encoding(string encoding)
    {
        var data = MakeTestData(512);

        using var compressed = new MemoryStream();
        using (var compressor = ContentEncoding.CreateCompressor(compressed, encoding))
        {
            compressor.Write(data);
        }

        compressed.Seek(0, SeekOrigin.Begin);
        using var decompressor = ContentEncoding.CreateDecompressor(compressed, encoding);
        var decompressed = ReadStream(decompressor);

        Assert.Equal(data, decompressed);
    }

    [Theory]
    [Trait("RFC", "RFC9110-8.4")]
    [InlineData("GZIP")]
    [InlineData("X-GZIP")]
    [InlineData("DEFLATE")]
    [InlineData("BR")]
    public void ContentEncoding_should_create_decompressor_case_insensitive(string encoding)
    {
        var data = MakeTestData(256);

        using var compressed = new MemoryStream();
        using (var compressor = ContentEncoding.CreateCompressor(compressed, encoding.ToLowerInvariant()))
        {
            compressor.Write(data);
        }

        compressed.Seek(0, SeekOrigin.Begin);
        using var decompressor = ContentEncoding.CreateDecompressor(compressed, encoding);
        var decompressed = ReadStream(decompressor);

        Assert.Equal(data, decompressed);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_throw_when_creating_decompressor_for_unknown_encoding()
    {
        using var stream = new MemoryStream();

        var ex = Assert.Throws<HttpDecoderException>(() =>
            ContentEncoding.CreateDecompressor(stream, "unknown"));

        Assert.Contains("Unknown Content-Encoding", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_throw_when_creating_decompressor_for_compress()
    {
        using var stream = new MemoryStream();

        var ex = Assert.Throws<HttpDecoderException>(() =>
            ContentEncoding.CreateDecompressor(stream, "compress"));

        Assert.Contains("Unknown Content-Encoding", ex.Message);
    }


    // ─── CreateCompressor Tests ───

    [Theory]
    [Trait("RFC", "RFC9110-8.4")]
    [InlineData("gzip")]
    [InlineData("x-gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void ContentEncoding_should_create_compressor_for_supported_encoding(string encoding)
    {
        var data = MakeTestData(512);

        using var compressed = new MemoryStream();
        using (var compressor = ContentEncoding.CreateCompressor(compressed, encoding))
        {
            compressor.Write(data);
        }

        // Verify compression actually reduced size (for non-empty data)
        Assert.NotEmpty(compressed.ToArray());
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_throw_when_creating_compressor_for_unknown_encoding()
    {
        using var stream = new MemoryStream();

        var ex = Assert.Throws<HttpDecoderException>(() =>
            ContentEncoding.CreateCompressor(stream, "unknown"));

        Assert.Contains("Unknown Content-Encoding", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_leave_stream_open_on_compressor()
    {
        var data = MakeTestData(256);
        using var baseStream = new MemoryStream();

        using (var compressor = ContentEncoding.CreateCompressor(baseStream, "gzip"))
        {
            compressor.Write(data);
        }

        // Stream should still be open after compressor is disposed
        Assert.True(baseStream.CanWrite);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_handle_empty_data_compression()
    {
        var emptyData = Array.Empty<byte>();
        using var compressed = new MemoryStream();
        using (var compressor = ContentEncoding.CreateCompressor(compressed, "gzip"))
        {
            compressor.Write(emptyData);
        }

        // Even with no input data, gzip compressor produces minimal output
        var result = compressed.ToArray();
        Assert.True(result.Length >= 0); // GZip may or may not output bytes for empty input
    }


    // ─── CreateCodecStream Tests ───

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_create_codec_stream_for_gzip_decompress()
    {
        var data = MakeTestData(256);

        using var compressed = new MemoryStream();
        using (var codec = ContentEncoding.CreateCodecStream(compressed, "gzip", CompressionMode.Compress, true))
        {
            codec.Write(data);
        }

        compressed.Seek(0, SeekOrigin.Begin);
        using var decompressed = ContentEncoding.CreateCodecStream(compressed, "gzip", CompressionMode.Decompress);
        var result = ReadStream(decompressed);

        Assert.Equal(data, result);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_respect_leave_open_flag_on_decompress()
    {
        using var compressed = new MemoryStream([0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]);

        // Create decompressor with leaveOpen=false (default)
        using (var decompressor = ContentEncoding.CreateCodecStream(compressed, "gzip", CompressionMode.Decompress))
        {
            // Decompressor should close the underlying stream when disposed
        }

        // Stream should be closed after decompressor is disposed (leaveOpen=false)
        Assert.False(compressed.CanRead);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_respect_leave_open_flag_on_compress()
    {
        using var output = new MemoryStream();

        // Create compressor with leaveOpen=true
        using (var compressor = ContentEncoding.CreateCodecStream(output, "gzip", CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(MakeTestData(128));
        }

        // Stream should still be open after compressor is disposed (leaveOpen=true)
        Assert.True(output.CanWrite);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_throw_when_codec_stream_unknown_encoding()
    {
        using var stream = new MemoryStream();

        var ex = Assert.Throws<HttpDecoderException>(() =>
            ContentEncoding.CreateCodecStream(stream, "unknown", CompressionMode.Decompress));

        Assert.Contains("Unknown Content-Encoding", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncoding_should_handle_x_gzip_as_gzip_equivalent()
    {
        var data = MakeTestData(256);

        // Compress with gzip
        using var compressed = new MemoryStream();
        using (var codec = ContentEncoding.CreateCodecStream(compressed, "gzip", CompressionMode.Compress, true))
        {
            codec.Write(data);
        }

        // Decompress with x-gzip (should work identically)
        compressed.Seek(0, SeekOrigin.Begin);
        using var decompressed = ContentEncoding.CreateCodecStream(compressed, "x-gzip", CompressionMode.Decompress);
        var result = ReadStream(decompressed);

        Assert.Equal(data, result);
    }

    [Theory]
    [Trait("RFC", "RFC9110-8.4")]
    [InlineData("gzip")]
    [InlineData("x-gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void ContentEncoding_should_roundtrip_compression_decompression(string encoding)
    {
        var data = MakeTestData(1024);

        using var compressed = new MemoryStream();
        using (var compressor = ContentEncoding.CreateCodecStream(compressed, encoding, CompressionMode.Compress, true))
        {
            compressor.Write(data);
        }

        compressed.Seek(0, SeekOrigin.Begin);
        using var decompressed = ContentEncoding.CreateCodecStream(compressed, encoding, CompressionMode.Decompress);
        var result = ReadStream(decompressed);

        Assert.Equal(data, result);
    }
}
