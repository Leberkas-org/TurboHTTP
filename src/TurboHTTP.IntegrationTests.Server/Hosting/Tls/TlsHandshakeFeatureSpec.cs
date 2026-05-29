using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

[Collection("Tls")]
public sealed class TlsHandshakeFeatureSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateTlsClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task TlsHandshakeFeature_should_be_available_in_context()
    {
        var response = await _client.GetAsync(
            new Uri($"https://127.0.0.1:{server.HttpsPort}/tls-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task TlsHandshakeFeature_should_contain_protocol()
    {
        var response = await _client.GetAsync(
            new Uri($"https://127.0.0.1:{server.HttpsPort}/tls-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("protocol", out var protocolElement), "Protocol property should exist");
        var protocolValue = protocolElement.GetString();
        Assert.NotNull(protocolValue);

        var validProtocols = new[] { "Tls12", "Tls13" };
        Assert.Contains(protocolValue, validProtocols);
    }

    [Fact(Timeout = 15000)]
    public async Task TlsHandshakeFeature_should_contain_negotiated_cipher_suite()
    {
        var response = await _client.GetAsync(
            new Uri($"https://127.0.0.1:{server.HttpsPort}/tls-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("cipherSuite", out var cipherSuiteElement), "CipherSuite property should exist");
        if (cipherSuiteElement.ValueKind != JsonValueKind.Null)
        {
            var cipherSuite = cipherSuiteElement.GetString();
            Assert.NotNull(cipherSuite);
            Assert.NotEmpty(cipherSuite);
        }
    }
}
