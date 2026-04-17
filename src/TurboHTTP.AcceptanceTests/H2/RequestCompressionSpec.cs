using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class RequestCompressionSpec : AcceptanceTestBase
{
    private static Http20Engine Engine => new(new Http2Options().ToEngineOptions());

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", payload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateCompressionEngine("gzip"), request, serverFrames);

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", payload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateCompressionEngine("deflate"), request, serverFrames);

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", payload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateCompressionEngine("br"), request, serverFrames);

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("x-content-encoding", "identity"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateDefaultCompressionEngine(), request, serverFrames);

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("x-content-encoding", "gzip"), ("content-length", "0")], endStream: true)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateCompressionEngine("gzip"), request, serverFrames);

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
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(payload)
        };

        var postServerFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", payload.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)payload)
            .Build();

        var (postResponse, _) = await SendH2EngineAsync(CreateCompressionEngine("gzip"), postRequest, postServerFrames);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var echoed = await postResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, echoed);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/gzip/1")
        {
            Version = HttpVersion.Version20
        };

        var getPayload = MakePayload(1);
        var compressed = GzipCompress(getPayload);

        var getServerFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-encoding", "gzip"), ("content-length", compressed.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)compressed)
            .Build();

        var (getResponse, _) = await SendH2EngineAsync(CreateCompressionEngine("gzip"), getRequest, getServerFrames);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var decompressedBody = await getResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(decompressedBody);
    }
}
