using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
public sealed class SmokeSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public SmokeSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_server.H1Port, new Version(1, 0), system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task SmokeTest_should_return_200_hello_world()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}
