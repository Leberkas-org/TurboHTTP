using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Hosting;

[Collection("Tls")]
public sealed class HttpsConnectionSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateTlsClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_over_https()
    {
        var response = await _client.GetAsync(
            new Uri($"https://127.0.0.1:{server.HttpsPort}/secure-hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from HTTPS", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_over_https_for_unknown_route()
    {
        var response = await _client.GetAsync(
            new Uri($"https://127.0.0.1:{server.HttpsPort}/unknown"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
