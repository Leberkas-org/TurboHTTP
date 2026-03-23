using System.IO.Compression;
using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// Verifies that the HTTP/1.0 engine pipeline correctly decompresses
/// Content-Encoding responses when the <see cref="ContentEncodingBidiStage"/> is composed with the engine.
/// </summary>
/// <remarks>
/// Pipeline under test: ContentEncodingBidiStage ∘ Http10Engine.
/// RFC 9110 §8.4: Content-Encoding and transparent decompression.
/// RFC 1945 §10.3: Content-Encoding entity-header in HTTP/1.0.
/// </remarks>
public sealed class Http10DecompressionPipelineTests : EngineTestBase
{
    private static readonly Http10Engine Engine = new();

    /// <summary>
    /// Composes ContentEncodingBidiStage atop the Http10Engine so that responses
    /// are automatically decompressed before reaching the caller.
    /// </summary>
    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDecompressingEngine()
    {
        var decomp = BidiFlow.FromGraph(new ContentEncodingBidiStage());
        return decomp.Atop(Engine.CreateFlow());
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] BuildHttp10Response(byte[] body, string? contentEncoding = null,
        string contentType = "text/plain")
    {
        var headers = new StringBuilder();
        headers.Append("HTTP/1.0 200 OK\r\n");
        headers.Append($"Content-Type: {contentType}\r\n");
        headers.Append($"Content-Length: {body.Length}\r\n");
        if (contentEncoding is not null)
        {
            headers.Append($"Content-Encoding: {contentEncoding}\r\n");
        }
        headers.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(headers.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    // ============================
    // gzip decompression
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-001: Content-Encoding: gzip → body decompressed through Http10Engine")]
    public async Task Should_DecompressGzipBody_When_ContentEncodingIsGzip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzip")
        {
            Version = HttpVersion.Version10
        };

        const string expectedBody = "Hello, gzip-compressed world!";
        var compressed = GzipCompress(Encoding.UTF8.GetBytes(expectedBody));
        var rawResponse = BuildHttp10Response(compressed, "gzip");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-002: Content-Encoding header removed after gzip decompression")]
    public async Task Should_RemoveContentEncodingHeader_When_GzipDecompressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzip-header")
        {
            Version = HttpVersion.Version10
        };

        var compressed = GzipCompress("test body"u8.ToArray());
        var rawResponse = BuildHttp10Response(compressed, "gzip");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.False(response.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-003: Content-Length updated to decompressed size after gzip")]
    public async Task Should_UpdateContentLength_When_GzipDecompressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzip-len")
        {
            Version = HttpVersion.Version10
        };

        var original = "content length verification body"u8.ToArray();
        var compressed = GzipCompress(original);
        var rawResponse = BuildHttp10Response(compressed, "gzip");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.Equal(original.Length, response.Content.Headers.ContentLength);
    }

    // ============================
    // x-gzip decompression
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-004: Content-Encoding: x-gzip → body decompressed through Http10Engine")]
    public async Task Should_DecompressBody_When_ContentEncodingIsXGzip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/x-gzip")
        {
            Version = HttpVersion.Version10
        };

        const string expectedBody = "Hello, x-gzip-compressed world!";
        var compressed = GzipCompress(Encoding.UTF8.GetBytes(expectedBody));
        var rawResponse = BuildHttp10Response(compressed, "x-gzip");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-005: Content-Encoding header removed after x-gzip decompression")]
    public async Task Should_RemoveContentEncodingHeader_When_XGzipDecompressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/x-gzip-header")
        {
            Version = HttpVersion.Version10
        };

        var compressed = GzipCompress("x-gzip test"u8.ToArray());
        var rawResponse = BuildHttp10Response(compressed, "x-gzip");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.False(response.Content.Headers.Contains("Content-Encoding"));
    }

    // ============================
    // identity encoding (pass-through)
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-006: Content-Encoding: identity → body passes through unchanged")]
    public async Task Should_PassBodyUnchanged_When_ContentEncodingIsIdentity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/identity")
        {
            Version = HttpVersion.Version10
        };

        const string expectedBody = "This body should not be modified";
        var bodyBytes = Encoding.UTF8.GetBytes(expectedBody);
        var rawResponse = BuildHttp10Response(bodyBytes, "identity");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-007: no Content-Encoding header → body passes through unchanged")]
    public async Task Should_PassBodyUnchanged_When_NoContentEncoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/plain")
        {
            Version = HttpVersion.Version10
        };

        const string expectedBody = "Plain response with no encoding";
        var bodyBytes = Encoding.UTF8.GetBytes(expectedBody);
        var rawResponse = BuildHttp10Response(bodyBytes);

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
    }

    // ============================
    // Content-Type preservation
    // ============================

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-10.3-10DEC-008: Content-Type preserved after gzip decompression")]
    public async Task Should_PreserveContentType_When_GzipDecompressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/json-gzip")
        {
            Version = HttpVersion.Version10
        };

        const string jsonBody = "{\"status\":\"ok\"}";
        var compressed = GzipCompress(Encoding.UTF8.GetBytes(jsonBody));
        var rawResponse = BuildHttp10Response(compressed, "gzip", "application/json");

        var (response, _) = await SendAsync(
            CreateDecompressingEngine(),
            request,
            () => rawResponse);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(jsonBody, body);
        Assert.Contains("application/json",
            response.Content.Headers.ContentType?.ToString() ?? "");
    }
}
