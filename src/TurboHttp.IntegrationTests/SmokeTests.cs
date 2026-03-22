using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests;

[Collection("Http1Integration")]
public sealed class SmokeTests : IAsyncLifetime
{
    private readonly KestrelFixture _fixture;
    private ClientHelper? _helper;

    public SmokeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(1, 1));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}
