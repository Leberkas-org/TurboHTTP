using System.IO.Compression;
using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class RequestCompressionSpec : AcceptanceTestBase
{
    private static Http11Engine Engine => new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    private static byte[] MakePayload(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('A' + i % 26);
        }
        return payload;
    }

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateCompressionEngine(string encoding)
    {
        var stage = new ContentEncodingBidiStage(true, new CompressionPolicy { Encoding = encoding });
        return BidiFlow.FromGraph(stage).Atop(Engine.CreateFlow());
    }

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDefaultCompressionEngine()
    {
        var stage = new ContentEncodingBidiStage(true, CompressionPolicy.Default);
        return BidiFlow.FromGraph(stage).Atop(Engine.CreateFlow());
    }

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDecompressingAndCompressingEngine(string encoding)
    {
        var stage = new ContentEncodingBidiStage(true, new CompressionPolicy { Encoding = encoding });
        return BidiFlow.FromGraph(stage).Atop(Engine.CreateFlow());
    }

    private static byte[] BuildResponse(byte[] body, string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        if (extraHeaders is not null)
        {
            sb.Append(extraHeaders);
        }
        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] GzipDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DeflateDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] BrotliDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
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

    private async Task<(HttpResponseMessage Response, ScriptedFakeConnectionStage Fake)> SendCompressedAsync(
        BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return (response, fake);
    }

    /// <summary>
    /// Extracts the body bytes from a raw HTTP/1.1 request by finding the \r\n\r\n separator.
    /// </summary>
    private static byte[] ExtractRequestBody(byte[] rawRequest)
    {
        var separator = "\r\n\r\n"u8;
        var span = rawRequest.AsSpan();
        var idx = span.IndexOf(separator);
        if (idx < 0)
        {
            return [];
        }
        return span[(idx + 4)..].ToArray();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_send_and_verify_gzip_request_body()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-gzip")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (response, fake) = await SendCompressedAsync(
            CreateCompressionEngine("gzip"),
            request,
            (_, outbound) =>
            {
                // Server: decompress the gzip body and echo it back
                var body = ExtractRequestBody(outbound);
                var decompressed = GzipDecompress(body);
                return BuildResponse(decompressed);
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_send_and_verify_deflate_request_body()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-deflate")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (response, _) = await SendCompressedAsync(
            CreateCompressionEngine("deflate"),
            request,
            (_, outbound) =>
            {
                var body = ExtractRequestBody(outbound);
                var decompressed = DeflateDecompress(body);
                return BuildResponse(decompressed);
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_send_and_verify_brotli_request_body()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-br")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (response, _) = await SendCompressedAsync(
            CreateCompressionEngine("br"),
            request,
            (_, outbound) =>
            {
                var body = ExtractRequestBody(outbound);
                var decompressed = BrotliDecompress(body);
                return BuildResponse(decompressed);
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_not_compress_small_body_below_threshold()
    {
        var payload = MakePayload(100);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/echo")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (response, fake) = await SendCompressedAsync(
            CreateDefaultCompressionEngine(),
            request,
            (_, outbound) =>
            {
                // Check if Content-Encoding header is present in the raw request
                var rawStr = Encoding.Latin1.GetString(outbound);
                var hasContentEncoding = rawStr.Contains("Content-Encoding:", StringComparison.OrdinalIgnoreCase);
                var encoding = hasContentEncoding ? "compressed" : "identity";
                return BuildResponse(Array.Empty<byte>(), $"X-Content-Encoding: {encoding}\r\n");
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encodingHeader = response.Headers.TryGetValues("X-Content-Encoding", out var vals)
            ? string.Join(",", vals)
            : "identity";
        Assert.Equal("identity", encodingHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_set_content_encoding_header_correctly_for_gzip()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/echo")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (response, _) = await SendCompressedAsync(
            CreateCompressionEngine("gzip"),
            request,
            (_, outbound) =>
            {
                var rawStr = Encoding.Latin1.GetString(outbound);
                var encoding = rawStr.Contains("Content-Encoding: gzip", StringComparison.OrdinalIgnoreCase)
                    ? "gzip"
                    : "identity";
                return BuildResponse(Array.Empty<byte>(), $"X-Content-Encoding: {encoding}\r\n");
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Content-Encoding", out var vals),
            "X-Content-Encoding header must be present");
        Assert.Equal("gzip", string.Join(",", vals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task RequestCompression_should_roundtrip_compressed_request_and_decompressed_response()
    {
        var payload = MakePayload(4 * 1024);

        // Request direction: client compresses → server decompresses → echoes back
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-gzip")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(payload)
        };

        var (postResponse, _) = await SendCompressedAsync(
            CreateDecompressingAndCompressingEngine("gzip"),
            postRequest,
            (_, outbound) =>
            {
                var body = ExtractRequestBody(outbound);
                var decompressed = GzipDecompress(body);
                return BuildResponse(decompressed);
            });

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var echoed = await postResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, echoed);

        // Response direction: server sends gzip response → client decompresses
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/gzip/1")
        {
            Version = HttpVersion.Version11
        };

        var compressedPayload = GzipCompress(MakePayload(1024));
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Length: {compressedPayload.Length}\r\n");
        sb.Append("Content-Encoding: gzip\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var gzipResponse = new byte[headerBytes.Length + compressedPayload.Length];
        headerBytes.CopyTo(gzipResponse, 0);
        compressedPayload.CopyTo(gzipResponse, headerBytes.Length);

        var fake2 = new ScriptedFakeConnectionStage((_, _) => gzipResponse);
        var flow2 = CreateDecompressingAndCompressingEngine("gzip")
            .Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake2));

        var tcs2 = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(getRequest)
            .Via(flow2)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs2.TrySetResult(res)), Materializer);

        var getResponse = await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var decompressedBody = await getResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, decompressedBody.Length);
    }
}
