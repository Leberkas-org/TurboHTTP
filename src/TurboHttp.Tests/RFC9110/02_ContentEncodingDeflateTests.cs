using System.IO.Compression;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding: deflate, br, and identity tests.
/// Covers deflate (zlib), brotli, identity, and no-encoding scenarios across HTTP versions.
/// </summary>
public sealed class ContentEncodingDeflateTests
{
    // ── Compression helpers ──────────────────────────────────────────────────

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionLevel.Fastest))
        {
            br.Write(data);
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

    // ── HTTP/1.1 Deflate Tests ──────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-deflate-001: Should_DecompressDeflate_When_ContentEncodingIsDeflate")]
    public async Task Should_DecompressDeflate_When_ContentEncodingIsDeflate()
    {
        var original = "Hello, deflate!"u8.ToArray();
        var compressed = DeflateCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "deflate", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-deflate-002: Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity")]
    public async Task Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity()
    {
        var original = "plain text"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, "identity", original);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-deflate-003: Should_LeaveBodyUnchanged_When_NoContentEncoding")]
    public async Task Should_LeaveBodyUnchanged_When_NoContentEncoding()
    {
        var original = "plain text"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, null, original);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-deflate-004: Should_ThrowDecompressionFailed_When_UnknownEncoding")]
    public void Should_ThrowDecompressionFailed_When_UnknownEncoding()
    {
        var body = "test"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, "zstd", body);
        var decoder = new Http11Decoder();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(responseBytes, out _));

        Assert.Equal(HttpDecodeError.DecompressionFailed, ex.DecodeError);
    }

    // ── HTTP/1.1 Brotli Tests ────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-br-001: Should_DecompressBrotli_When_ContentEncodingIsBr")]
    public async Task Should_DecompressBrotli_When_ContentEncodingIsBr()
    {
        var original = "Hello, Brotli!"u8.ToArray();
        var compressed = BrotliCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "br", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-br-002: Should_DecompressBrotli_LargeContent")]
    public async Task Should_DecompressBrotli_LargeContent()
    {
        var original = new byte[16 * 1024];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 256);
        }

        var compressed = BrotliCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "br", compressed);
        var decoder = new Http11Decoder(maxBodySize: 100_000);

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    // ── HTTP/1.0 Deflate Tests ──────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-deflate-h10-001: Should_DecompressDeflate_In_Http10_Response")]
    public async Task Should_DecompressDeflate_In_Http10_Response()
    {
        var original = "deflate in http/1.0"u8.ToArray();
        var compressed = DeflateCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "deflate", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        var body = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    // ── HTTP/1.0 Brotli Tests ────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-br-h10-001: Should_DecompressBrotli_In_Http10_Response")]
    public async Task Should_DecompressBrotli_In_Http10_Response()
    {
        var original = "brotli in http/1.0"u8.ToArray();
        var compressed = BrotliCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "br", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        var body = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    // ── HTTP/2 Deflate Tests ─────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-deflate-h2-001: Should_DecompressDeflate_In_Http2_Response")]
    public async Task Should_DecompressDeflate_In_Http2_Response()
    {
        var original = "deflate in http/2"u8.ToArray();
        var compressed = DeflateCompress(original);
        var responseBytes = BuildHttp2Response(1, "deflate", compressed);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        var body = await session.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    // ── HTTP/2 Identity and No-Encoding Tests ────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-identity-h2-001: Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding")]
    public async Task Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding()
    {
        var original = "plain http2"u8.ToArray();
        var responseBytes = BuildHttp2Response(1, null, original);
        var session = new Http2ProtocolSession();

        Assert.NotEmpty(session.Process(responseBytes.AsMemory()));
        var body = await session.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    // ── Transfer-Encoding vs Content-Encoding Distinction ────────────────────

    [Fact(DisplayName = "RFC9110-8.4-distinction-001: Should_NotConfuse_TransferEncoding_WithContentEncoding")]
    public async Task Should_NotConfuse_TransferEncoding_WithContentEncoding()
    {
        var original = "hello chunked"u8.ToArray();
        var chunkHex = original.Length.ToString("x");
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Type: text/plain\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        sb.Append($"{chunkHex}\r\n");
        sb.Append(Encoding.UTF8.GetString(original));
        sb.Append("\r\n0\r\n\r\n");

        var responseBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }
}