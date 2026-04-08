using System.Net;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_send_and_verify_gzip_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_send_and_verify_deflate_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_send_and_verify_brotli_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_not_compress_small_body_below_threshold()
    {
        // Default threshold is 1024 bytes — a 100-byte body must pass through uncompressed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_set_content_encoding_header_correctly_for_gzip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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

    [Fact(Timeout = 30000)]
    public async Task RequestCompression_should_roundtrip_compressed_request_and_decompressed_response()
    {
        // Both directions of ContentEncodingBidiStage are active:
        // Out: client compresses the POST body (gzip).
        // In: client decompresses the GET response (gzip).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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
