using System.IO.Compression;
using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class CompressionSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    private static BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed>
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
        using (var deflate = new ZLibStream(output, CompressionMode.Compress))
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

    private static byte[] BuildResponse(byte[] body, string? contentEncoding = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        if (contentEncoding is not null)
        {
            sb.Append($"Content-Encoding: {contentEncoding}\r\n");
        }

        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private async Task<HttpResponseMessage> SendDecompressingAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = CreateDecompressingEngine().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task Compression_should_transparently_decompress_gzip_response_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/gzip/4")
        {
            Version = HttpVersion.Version11
        };

        var payload = MakePayload(4);
        var compressed = GzipCompress(payload);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(compressed, "gzip"));

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
    public async Task Compression_should_transparently_decompress_deflate_response_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/deflate/2")
        {
            Version = HttpVersion.Version11
        };

        var payload = MakePayload(2);
        var compressed = DeflateCompress(payload);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(compressed, "deflate"));

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
    public async Task Compression_should_transparently_decompress_brotli_response_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/br/3")
        {
            Version = HttpVersion.Version11
        };

        var payload = MakePayload(3);
        var compressed = BrotliCompress(payload);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(compressed, "br"));

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
    public async Task Compression_should_pass_identity_encoding_through_unchanged_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/identity/1")
        {
            Version = HttpVersion.Version11
        };

        var payload = MakePayload(1);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(payload, "identity"));

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
    public async Task Compression_should_negotiate_accept_encoding_gzip_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        var payload = MakePayload(1);
        var compressed = GzipCompress(payload);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(compressed, "gzip"));

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
    public async Task Compression_should_negotiate_accept_encoding_br_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");

        var payload = MakePayload(1);
        var compressed = BrotliCompress(payload);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(compressed, "br"));

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
    public async Task Compression_should_return_identity_when_no_accept_encoding_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/negotiate")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.Remove("Accept-Encoding");

        var payload = MakePayload(1);

        var response = await SendDecompressingAsync(request, (_, _) => BuildResponse(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }
}

