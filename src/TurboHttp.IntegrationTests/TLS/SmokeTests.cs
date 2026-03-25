using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TlsIntegration")]
public sealed class SmokeTests : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private ClientHelper? _helper;

    public SmokeTests(ServerFixture server)
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

    [Fact]
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