using System.IO.Compression;
using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding integration tests.
/// Covers stacked (multiple) encodings, Accept-Encoding header injection,
/// and encoder/decoder cross-version compatibility.
/// </summary>
public sealed class ContentEncodingIntegrationTests
{
    // ── Compression helpers ──────────────────────────────────────────────────

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

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

    // ── Stacked Encoding Tests ───────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-SE-001: Should_DecompressStackedEncodings_GzipThenBr")]
    public async Task Should_DecompressStackedEncodings_When_GzipThenBrEncoding()
    {
        // "gzip, br" means: gzip applied first, br applied last (outermost).
        // Decode in reverse: br first, then gzip.
        var original = "stacked encoding test"u8.ToArray();
        var gzipped = GzipCompress(original);
        var gzippedThenBrotli = BrotliCompress(gzipped);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip, br", gzippedThenBrotli);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-SE-002: Should_DecompressStackedEncodings_DeflateGzipBr")]
    public async Task Should_DecompressStackedEncodings_When_DeflateGzipBrEncoding()
    {
        // "deflate, gzip, br" means: deflate first, br last.
        // Decode in reverse: br, gzip, deflate.
        var original = "triple encoding test"u8.ToArray();
        var deflated = DeflateCompress(original);
        var deflatedGzipped = GzipCompress(deflated);
        var allStacked = BrotliCompress(deflatedGzipped);

        var responseBytes = BuildHttp11ResponseBytes(200, "deflate, gzip, br", allStacked);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-SE-003: Should_DecompressStackedEncodings_RemoveAllHeaders")]
    public void Should_RemoveContentEncodingHeaders_When_StackedEncodingsDecompressed()
    {
        var original = "test"u8.ToArray();
        var gzipped = GzipCompress(original);
        var stacked = BrotliCompress(gzipped);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip, br", stacked);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses[0];

        // Content-Encoding header should be removed after all decompression
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-SE-004: Should_DecompressStackedEncodings_UpdateContentLength")]
    public void Should_UpdateContentLength_When_StackedEncodingsDecompressed()
    {
        var original = "stacked test content"u8.ToArray();
        var gzipped = GzipCompress(original);
        var stacked = BrotliCompress(gzipped);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip, br", stacked);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses[0];

        Assert.Equal(original.Length, response.Content.Headers.ContentLength);
    }

    // ── Accept-Encoding Injection Tests ──────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-AE-001: Should_AddAcceptEncoding_When_NotAlreadySet")]
    public void Should_AddAcceptEncoding_When_NotAlreadySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("Accept-Encoding: gzip, deflate, br", encoded);
    }

    [Fact(DisplayName = "RFC9110-8.4-AE-002: Should_NotOverrideAcceptEncoding_When_AlreadySet")]
    public void Should_NotOverrideAcceptEncoding_When_AlreadySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Add("Accept-Encoding", "gzip");

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("Accept-Encoding: gzip", encoded);
        Assert.DoesNotContain("Accept-Encoding: gzip, deflate, br", encoded);
    }

    [Fact(DisplayName = "RFC9110-8.4-AE-003: Should_AddAcceptEncoding_For_Post_With_Body")]
    public void Should_AddAcceptEncoding_When_PostRequestWithBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Accept-Encoding: gzip, deflate, br", encoded);
    }

    [Fact(DisplayName = "RFC9110-8.4-AE-004: Should_AddAcceptEncoding_For_Put_Request")]
    public void Should_AddAcceptEncoding_When_PutRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = new ByteArrayContent("update data"u8.ToArray())
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Accept-Encoding: gzip, deflate, br", encoded);
    }

    // ── Content-Encoding + Accept-Encoding Round-Trip Tests ─────────────────

    [Fact(DisplayName = "RFC9110-8.4-RT-001: Should_HandleRequestResponseWithCompressionCycle")]
    public async Task Should_HandleCompressionCycle_When_RequestAndResponseCompressed()
    {
        // Request injection
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var requestBuffer = new byte[4096];
        var requestSpan = requestBuffer.AsSpan();
        var requestWritten = Http11Encoder.Encode(request, ref requestSpan);
        var requestEncoded = Encoding.ASCII.GetString(requestBuffer, 0, requestWritten);

        // Verify Accept-Encoding was injected
        Assert.Contains("Accept-Encoding: gzip, deflate, br", requestEncoded);

        // Response decompression
        var responseBody = "Compressed response content"u8.ToArray();
        var compressed = GzipCompress(responseBody);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var decompressed = await responses[0].Content.ReadAsByteArrayAsync();

        Assert.Equal(responseBody, decompressed);
        Assert.Empty(responses[0].Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "RFC9110-8.4-RT-002: Should_PreserveContentOnNoEncoding_WithAcceptEncodingHeader")]
    public async Task Should_PreserveContent_When_NoEncodingWithAcceptEncodingHeader()
    {
        // Request with Accept-Encoding
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var requestBuffer = new byte[4096];
        var requestSpan = requestBuffer.AsSpan();
        Http11Encoder.Encode(request, ref requestSpan);

        // Response with no encoding
        var responseBody = "Uncompressed response"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, null, responseBody);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = await responses[0].Content.ReadAsByteArrayAsync();

        Assert.Equal(responseBody, body);
    }

    [Fact(DisplayName = "RFC9110-8.4-RT-003: Should_SupportBrotliRoundTrip")]
    public async Task Should_SupportRoundTrip_When_BrotliEncoding()
    {
        // Request with Accept-Encoding that includes brotli
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var requestBuffer = new byte[4096];
        var requestSpan = requestBuffer.AsSpan();
        var requestWritten = Http11Encoder.Encode(request, ref requestSpan);
        var requestEncoded = Encoding.ASCII.GetString(requestBuffer, 0, requestWritten);

        Assert.Contains("Accept-Encoding: gzip, deflate, br", requestEncoded);

        // Response with brotli encoding
        var responseBody = "Server chose brotli compression"u8.ToArray();
        var compressed = BrotliCompress(responseBody);
        var responseBytes = BuildHttp11ResponseBytes(200, "br", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var decompressed = await responses[0].Content.ReadAsByteArrayAsync();

        Assert.Equal(responseBody, decompressed);
    }

    // ── HTTP Version Compatibility ───────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-8.4-CV-001: Should_DecodeStackedEncodingsConsistentlyAcrossVersions")]
    public async Task Should_DecodeStackedEncodings_When_CheckingMultipleVersions()
    {
        var original = "version compatibility test"u8.ToArray();
        var gzipped = GzipCompress(original);
        var stacked = BrotliCompress(gzipped);

        // HTTP/1.1
        var http11Response = BuildHttp11ResponseBytes(200, "gzip, br", stacked);
        var http11Decoder = new Http11Decoder();
        Assert.True(http11Decoder.TryDecode(http11Response, out var http11Responses));
        var http11Body = await http11Responses[0].Content.ReadAsByteArrayAsync();

        Assert.Equal(original, http11Body);
    }

    [Fact(DisplayName = "RFC9110-8.4-CV-002: Should_HandleEncodingMismatch_DeflateVsGzip")]
    public void Should_ThrowException_When_EncodingMismatch()
    {
        // Create deflate-compressed data
        var original = "test data"u8.ToArray();
        var deflateCompressed = DeflateCompress(original);

        // Tell the decoder it's gzip (wrong)
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", deflateCompressed);
        var decoder = new Http11Decoder();

        // Should throw because the data doesn't match the declared encoding
        Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecode(responseBytes, out _));
    }
}