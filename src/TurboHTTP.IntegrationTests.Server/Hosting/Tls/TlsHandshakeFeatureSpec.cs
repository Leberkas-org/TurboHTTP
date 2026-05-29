using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.IntegrationTests.Server.Hosting.Tls;

[Collection("Infrastructure")]
public sealed class TlsHandshakeFeatureSpec : ServerSpecBase
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        var certificate = CreateSelfSignedCertificate("localhost");
        builder.Host.UseTurboHttp(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(certificate);
                listen.Protocols = HttpProtocols.Http1;
            });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/tls-info", (HttpContext context) =>
        {
            var tls = context.Features.Get<ITlsHandshakeFeature>();
            if (tls is null)
            {
                return Results.NotFound();
            }

            var response = new
            {
                Protocol = tls.Protocol.ToString(),
                CipherSuite = tls.NegotiatedCipherSuite?.ToString(),
                tls.HostName
            };

            return Results.Ok(response);
        });
    }

    protected override HttpClient CreateHttpClient() => CreateTlsClient();

    [Fact(Timeout = 15000)]
    public async Task TlsHandshakeFeature_should_be_available_in_context()
    {
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/tls-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task TlsHandshakeFeature_should_contain_protocol()
    {
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/tls-info"),
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
        var response = await Client.GetAsync(
            new Uri($"https://127.0.0.1:{Port}/tls-info"),
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
