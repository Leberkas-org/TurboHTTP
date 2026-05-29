using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Lifecycle;

public sealed class ServerSmokeSpec(TurboServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_get_request()
    {
        var response = await server.Client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from TurboHTTP Server", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_echo_post_body()
    {
        var payload = "test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{server.Port}/echo")
        {
            Content = new StringContent(payload)
        };

        var response = await server.Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal(payload, value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await server.Client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/nonexistent"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_expose_remote_ip()
    {
        var response = await server.Client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/connection-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("127.0.0.1", value);
    }
}
