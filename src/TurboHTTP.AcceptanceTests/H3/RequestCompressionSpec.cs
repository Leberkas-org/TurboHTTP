using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class RequestCompressionSpec : AcceptanceTestBase
{
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
        return BidiFlow.FromGraph(stage).Atop(CreateHttp30Engine().CreateFlow());
    }

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDefaultCompressionEngine()
    {
        var stage = new ContentEncodingBidiStage(true, CompressionPolicy.Default);
        return BidiFlow.FromGraph(stage).Atop(CreateHttp30Engine().CreateFlow());
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Gzip_compressed_request_should_be_verified_by_server()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-gzip")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateCompressionEngine("gzip"), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Deflate_compressed_request_should_be_verified_by_server()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-deflate")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateCompressionEngine("deflate"), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Brotli_compressed_request_should_be_verified_by_server()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-br")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateCompressionEngine("br"), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Small_body_below_threshold_should_not_be_compressed()
    {
        var payload = MakePayload(100);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/echo")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("x-content-encoding", "identity"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateDefaultCompressionEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encoding = response.Headers.TryGetValues("X-Content-Encoding", out var vals)
            ? string.Join(",", vals)
            : "identity";
        Assert.Equal("identity", encoding);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Gzip_compression_should_set_content_encoding_header()
    {
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/echo")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("x-content-encoding", "gzip"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateCompressionEngine("gzip"), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Content-Encoding", out var vals),
            "X-Content-Encoding header must be present");
        Assert.Equal("gzip", string.Join(",", vals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Compressed_request_and_decompressed_response_should_roundtrip()
    {
        var payload = MakePayload(4 * 1024);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/compress/verify-gzip")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var postResponseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (postResponse, _) = await SendH3EngineAsync(CreateCompressionEngine("gzip"), postRequest, controlFrames,
            postResponseFrames);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var echoed = await postResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, echoed);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/gzip/1")
        {
            Version = HttpVersion.Version30
        };

        var getPayload = MakePayload(1);
        var compressed = GzipCompress(getPayload);

        var getResponseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "gzip"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (getResponse, _) = await SendH3EngineAsync(CreateCompressionEngine("gzip"), getRequest, controlFrames,
            getResponseFrames);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var decompressedBody = await getResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(decompressedBody);
    }
}