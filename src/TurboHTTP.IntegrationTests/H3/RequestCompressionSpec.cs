using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class RequestCompressionSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RequestCompressionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
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
    public async Task Gzip_compressed_request_should_be_verified_by_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b.WithRequestCompression(x => x.Encoding = "gzip"),
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
    public async Task Deflate_compressed_request_should_be_verified_by_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b.WithRequestCompression(x => x.Encoding = "deflate"),
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
    public async Task Brotli_compressed_request_should_be_verified_by_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b.WithRequestCompression(x => x.Encoding = "br"),
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b.WithRequestCompression(),
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
    public async Task Gzip_compression_should_set_content_encoding_header()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b.WithRequestCompression(x => x.Encoding = "gzip"),
            system: _systemFixture.System);

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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: b => b
                .WithRequestCompression(x => x.Encoding = "gzip")
                .WithDecompression(),
            system: _systemFixture.System);

        var payload = MakePayload(4 * 1024);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/compress/verify-gzip")
        {
            Content = new ByteArrayContent(payload)
        };
        var postResponse = await helper.Client.SendAsync(postRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var echoed = await postResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, echoed);

        var getResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var decompressedBody = await getResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1024, decompressedBody.Length);
    }
}