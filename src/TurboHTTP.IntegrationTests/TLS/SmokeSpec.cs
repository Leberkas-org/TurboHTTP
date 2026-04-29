using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class SmokeSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private ClientHelper? _helper;

    public SmokeSpec(ServerFixture server)
    {
        _server = server;
    }

    public ValueTask InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_server.HttpsPort, new Version(1, 1), scheme: "https");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_return_200_and_hello_world()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}
