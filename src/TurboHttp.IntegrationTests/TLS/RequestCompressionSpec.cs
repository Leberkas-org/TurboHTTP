using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RequestCompressionSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RequestCompressionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private static byte[] MakePayload(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('A' + i % 26);
        }

        return payload;
    }

    [Fact(Timeout = 20000)]
    public async Task Gzip_request_body_should_be_sent_and_verified_by_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b.WithRequestCompression(new CompressionPolicy { Encoding = "gzip" }),
            system: _systemFixture.System);

        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "/compress/verify-gzip")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 20000)]
    public async Task Deflate_request_body_should_be_sent_and_verified()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b.WithRequestCompression(new CompressionPolicy { Encoding = "deflate" }),
            system: _systemFixture.System);

        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "/compress/verify-deflate")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 20000)]
    public async Task Brotli_request_body_should_be_sent_and_verified()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b.WithRequestCompression(new CompressionPolicy { Encoding = "br" }),
            system: _systemFixture.System);

        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "/compress/verify-br")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 20000)]
    public async Task Small_body_below_threshold_should_not_be_compressed()
    {
        // Default threshold is 1024 bytes — a 100-byte body must pass through uncompressed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b.WithRequestCompression(CompressionPolicy.Default),
            system: _systemFixture.System);

        var payload = MakePayload(100);
        var request = new HttpRequestMessage(HttpMethod.Post, "/compress/echo")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encoding = response.Headers.TryGetValues("X-Content-Encoding", out var vals)
            ? string.Join(",", vals)
            : "identity";
        Assert.Equal("identity", encoding);
    }

    [Fact(Timeout = 20000)]
    public async Task ContentEncoding_header_should_be_set_correctly_for_gzip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b.WithRequestCompression(new CompressionPolicy { Encoding = "gzip" }),
            system: _systemFixture.System);

        // Body must be >= 1024 bytes to trigger compression.
        var payload = MakePayload(4 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "/compress/echo")
        {
            Content = new ByteArrayContent(payload)
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Content-Encoding", out var vals),
            "X-Content-Encoding header must be present");
        Assert.Equal("gzip", string.Join(",", vals));
    }

    [Fact(Timeout = 20000)]
    public async Task Compressed_request_and_decompressed_response_should_roundtrip()
    {
        // Both directions of ContentEncodingBidiStage are active:
        // Out: client compresses the POST body (gzip).
        // In: client decompresses the GET response (gzip).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: b => b
                .WithRequestCompression(new CompressionPolicy { Encoding = "gzip" })
                .WithDecompression(),
            system: _systemFixture.System);

        var payload = MakePayload(4 * 1024);

        // Request direction: client compresses → server verifies valid gzip → echoes decompressed body.
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/compress/verify-gzip")
        {
            Content = new ByteArrayContent(payload)
        };
        var postResponse = await helper.Client.SendAsync(postRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var echoed = await postResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, echoed);

        // Response direction: server sends gzip response → WithDecompression() transparently decompresses it.
        var getResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var decompressedBody = await getResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1024, decompressedBody.Length);
    }
}
