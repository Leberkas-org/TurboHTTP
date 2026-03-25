using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class Http3SmokeTests : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public Http3SmokeTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_server.HttpsPort, new Version(3, 0), scheme: "https", system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Skip = "QUIC is wild")]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}