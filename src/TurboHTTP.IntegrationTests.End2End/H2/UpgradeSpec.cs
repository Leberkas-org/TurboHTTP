using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.IntegrationTests.End2End.Shared;
using Xunit;

namespace TurboHTTP.IntegrationTests.End2End.H2;

public sealed class UpgradeSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override bool UseTls => false;

    protected override void ConfigureServer(TurboServerOptions options, ushort port, System.Security.Cryptography.X509Certificates.X509Certificate2? cert)
    {
        options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/hello", () => Results.Ok("Hello via h2c"));
    }

    [Fact(Timeout = 15000)]
    public async Task Upgrade_should_communicate_via_h2c()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/hello");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello via h2c", value);
    }
}
