using TurboHTTP.Client;
using System.IO.Compression;
using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Stages;

public sealed class Http10DecompressionPipelineSpec : EngineTestBase
{
    private static readonly Http10Engine Engine = new(new TurboClientOptions());

    private static BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed>
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_decompress_gzip_body_when_content_encoding_is_gzip()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_remove_content_encoding_header_when_gzip_decompressed()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_produce_correct_body_length_when_gzip_decompressed()
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

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original.Length, body.Length);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_decompress_body_when_content_encoding_is_x_gzip()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_remove_content_encoding_header_when_x_gzip_decompressed()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_pass_body_unchanged_when_content_encoding_is_identity()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_pass_body_unchanged_when_no_content_encoding()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-10.3")]
    public async Task Http10DecompressionPipeline_should_preserve_content_type_when_gzip_decompressed()
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

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(jsonBody, body);
        Assert.Contains("application/json",
            response.Content.Headers.ContentType?.ToString() ?? "");
    }
}