using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ConnectionInfoSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Connection_should_expose_local_ip_and_port()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/connection"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal("127.0.0.1", json.RootElement.GetProperty("localIp").GetString());
        Assert.Equal(server.Port, json.RootElement.GetProperty("localPort").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_expose_remote_ip()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/connection"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal("127.0.0.1", json.RootElement.GetProperty("remoteIp").GetString());
        Assert.True(json.RootElement.GetProperty("remotePort").GetInt32() > 0);
    }

    [Fact(Timeout = 15000)]
    public async Task Request_should_expose_protocol_version()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/protocol"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        var protocol = json.RootElement.GetProperty("protocol").GetString();
        Assert.Contains("HTTP/1", protocol);
    }
}
