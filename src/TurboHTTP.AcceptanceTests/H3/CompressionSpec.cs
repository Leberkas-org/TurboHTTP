using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class CompressionSpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    private static BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed>
        CreateDecompressingEngine()
    {
        var decomp = BidiFlow.FromGraph(new ContentEncodingBidiStage());
        return decomp.Atop(Engine.CreateFlow());
    }

    private static byte[] MakePayload(int sizeKb)
    {
        var payload = new byte[sizeKb * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('A' + i % 26);
        }
        return payload;
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

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionMode.Compress))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionMode.Compress))
        {
            brotli.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Gzip_should_transparently_decompress_response_to_original_size()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/gzip/4")
        {
            Version = HttpVersion.Version30
        };

        var payload = MakePayload(4);
        var compressed = GzipCompress(payload);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "gzip"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Deflate_should_transparently_decompress_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/deflate/2")
        {
            Version = HttpVersion.Version30
        };

        var payload = MakePayload(2);
        var compressed = DeflateCompress(payload);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "deflate"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2 * 1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Brotli_should_transparently_decompress_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/br/3")
        {
            Version = HttpVersion.Version30
        };

        var payload = MakePayload(3);
        var compressed = BrotliCompress(payload);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "br"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3 * 1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Identity_should_pass_through_unchanged()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/identity/1")
        {
            Version = HttpVersion.Version30
        };

        var payload = MakePayload(1);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "identity"), ("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1 * 1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Content_negotiation_should_return_gzip_response_for_Accept_Encoding_gzip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version30
        };
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        var payload = MakePayload(1);
        var compressed = GzipCompress(payload);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "gzip"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Content_negotiation_should_return_brotli_response_for_Accept_Encoding_br()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version30
        };
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");

        var payload = MakePayload(1);
        var compressed = BrotliCompress(payload);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "br"), ("content-length", compressed.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)compressed)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Content_negotiation_should_return_identity_when_no_Accept_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version30
        };
        request.Headers.Remove("Accept-Encoding");

        var payload = MakePayload(1);

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH3EngineAsync(CreateDecompressingEngine(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }
}
