using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class CompressionSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public CompressionSpec(ServerFixture server, ActorSystemFixture systemFixture)
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

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Gzip_should_transparently_decompress_response_to_original_size()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Deflate_should_transparently_decompress_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/deflate/2");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(2 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Brotli_should_transparently_decompress_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/br/3");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(3 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Identity_should_pass_through_unchanged()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/identity/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Content_negotiation_should_return_gzip_response_for_Accept_Encoding_gzip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Content_negotiation_should_return_brotli_response_for_Accept_Encoding_br()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Content_negotiation_should_return_identity_when_no_Accept_Encoding()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.Remove("Accept-Encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }
}
