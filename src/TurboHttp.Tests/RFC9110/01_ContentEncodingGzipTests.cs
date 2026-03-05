using System.IO.Compression;
using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding: gzip decompression tests.
/// Covers gzip compression/decompression cycles across HTTP/1.0, HTTP/1.1, and HTTP/2.
/// </summary>
public sealed class ContentEncodingGzipTests
{
    // ── Compression helper ───────────────────────────────────────────────────

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] BuildHttp11ResponseBytes(
        int statusCode,
        string? contentEncoding,
        byte[] body,
        string contentType = "text/plain")
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} OK\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        if (contentEncoding != null)
        {
            sb.Append($"Content-Encoding: {contentEncoding}\r\n");
        }

        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildHttp10ResponseBytes(
        int statusCode,
        string? contentEncoding,
        byte[] body)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {statusCode} OK\r\n");
        sb.Append("Content-Type: text/plain\r\n");
        if (contentEncoding != null)
        {
            sb.Append($"Content-Encoding: {contentEncoding}\r\n");
        }

        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildHttp2Response(
        int streamId,
        string? contentEncoding,
        byte[] body)
    {
        var hpack = new HpackEncoder();
        var headers = new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "text/plain"),
        };
        if (contentEncoding != null)
        {
            headers.Add(("content-encoding", contentEncoding));
        }

        headers.Add(("content-length", body.Length.ToString()));

        var headerBlock = hpack.Encode(headers);
        var frames = new List<byte>();

        // HEADERS frame
        var payloadLen = headerBlock.Length;
        var headersFrame = new byte[9 + payloadLen];
        headersFrame[0] = (byte)(payloadLen >> 16);
        headersFrame[1] = (byte)(payloadLen >> 8);
        headersFrame[2] = (byte)payloadLen;
        headersFrame[3] = 0x01; // HEADERS
        headersFrame[4] = 0x04; // END_HEADERS
        headersFrame[5] = 0x00;
        headersFrame[6] = 0x00;
        headersFrame[7] = 0x00;
        headersFrame[8] = (byte)streamId;
        headerBlock.Span.CopyTo(headersFrame.AsSpan(9));
        frames.AddRange(headersFrame);

        // DATA frame
        var dataFrame = new byte[9 + body.Length];
        dataFrame[0] = (byte)(body.Length >> 16);
        dataFrame[1] = (byte)(body.Length >> 8);
        dataFrame[2] = (byte)body.Length;
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x01; // END_STREAM
        dataFrame[5] = 0x00;
        dataFrame[6] = 0x00;
        dataFrame[7] = 0x00;
        dataFrame[8] = (byte)streamId;
        body.CopyTo(dataFrame.AsSpan(9));
        frames.AddRange(dataFrame);

        return [.. frames];
    }

    // ── HTTP/1.1 Gzip Decompression Tests ────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-gzip-001: Should_DecompressGzip_When_ContentEncodingIsGzip")]
    public async Task Should_DecompressGzip_When_ContentEncodingIsGzip()
    {
        var original = "Hello, World!"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        Assert.NotNull(responses);
        Assert.Single(responses);

        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-002: Should_RemoveContentEncodingHeader_After_Decompression")]
    public void Should_RemoveContentEncodingHeader_After_Decompression()
    {
        var original = "Hello"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses[0];

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-003: Should_UpdateContentLength_After_Decompression")]
    public void Should_UpdateContentLength_After_Decompression()
    {
        var original = "Hello, World!"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses[0];

        Assert.Equal(original.Length, response.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-004: Should_DecompressGzip_CaseInsensitive")]
    public async Task Should_DecompressGzip_CaseInsensitive()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "GZIP", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-005: Should_DecompressXGzip_WhenContentEncodingIsXGzip")]
    public async Task Should_DecompressXGzip_WhenContentEncodingIsXGzip()
    {
        var original = "x-gzip test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "x-gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-006: Should_HandleEmptyGzip_WhenEmptyBodyWithGzipEncoding")]
    public async Task Should_HandleEmptyGzip_WhenEmptyBodyWithGzipEncoding()
    {
        var compressed = GzipCompress([]);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-007: Should_ThrowDecompressionFailed_WhenCorruptGzipData")]
    public void Should_ThrowDecompressionFailed_WhenCorruptGzipData()
    {
        // Valid gzip header (10 bytes) + garbage deflate data
        var corrupt = new byte[]
        {
            0x1f, 0x8b, // gzip magic
            0x08, // compression method = deflate
            0x00, // flags = none
            0x00, 0x00, 0x00, 0x00, // mtime
            0x00, // xfl
            0xff, // OS = unknown
            0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF // garbage data
        };
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", corrupt);
        var decoder = new Http11Decoder();

        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(responseBytes, out _));
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-008: Should_DecompressLargeGzipBody_64KB")]
    public async Task Should_DecompressLargeGzipBody_64KB()
    {
        var original = new byte[64 * 1024];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 256);
        }

        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder(maxBodySize: 200_000);

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-009: Should_DecompressGzip_Utf8_MultibyteCotent")]
    public async Task Should_DecompressGzip_Utf8_MultibyteContent()
    {
        var original = "こんにちは世界 — Hello, World!"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed,
            contentType: "text/plain; charset=utf-8");
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
        Assert.Equal("こんにちは世界 — Hello, World!", Encoding.UTF8.GetString(body));
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-010: Should_NotDecompress_204_NoBodyResponse")]
    public async Task Should_NotDecompress_204_NoBodyResponse()
    {
        var responseBytes = "HTTP/1.1 204 No Content\r\nContent-Encoding: gzip\r\n\r\n"u8.ToArray();
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── HTTP/1.0 Gzip Decompression Tests ────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-gzip-h10-001: Should_DecompressGzip_When_Http10_ContentEncoding")]
    public async Task Should_DecompressGzip_When_Http10_ContentEncoding()
    {
        var original = "HTTP/1.0 gzip test"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.NotNull(response);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-h10-002: Should_RemoveContentEncoding_After_Http10_Decompression")]
    public void Should_RemoveContentEncoding_After_Http10_Decompression()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.Empty(response!.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-h10-003: Should_UpdateContentLength_After_Http10_Decompression")]
    public void Should_UpdateContentLength_After_Http10_Decompression()
    {
        var original = "hello http10"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.Equal(original.Length, response!.Content.Headers.ContentLength);
    }

    // ── HTTP/2 Gzip Decompression Tests ──────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-gzip-h2-001: Should_DecompressGzip_When_Http2_ContentEncoding")]
    public async Task Should_DecompressGzip_When_Http2_ContentEncoding()
    {
        var original = "HTTP/2 gzip test"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        Assert.Single(session.Responses);

        var body = await session.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-h2-002: Should_RemoveContentEncoding_After_Http2_Decompression")]
    public void Should_RemoveContentEncoding_After_Http2_Decompression()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        var response = session.Responses[0].Response;

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-h2-003: Should_UpdateContentLength_After_Http2_Decompression")]
    public void Should_UpdateContentLength_After_Http2_Decompression()
    {
        var original = "hello http2 gzip"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        var response = session.Responses[0].Response;

        Assert.Equal(original.Length, response.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC9110-8.4-gzip-h2-004: Should_DecompressBrotli_ViaHttp2")]
    public async Task Should_DecompressBrotli_ViaHttp2()
    {
        var original = "HTTP/2 brotli test"u8.ToArray();
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionLevel.Fastest))
        {
            br.Write(original);
        }

        var compressed = output.ToArray();
        var responseBytes = BuildHttp2Response(1, "br", compressed);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        var body = await session.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }
}