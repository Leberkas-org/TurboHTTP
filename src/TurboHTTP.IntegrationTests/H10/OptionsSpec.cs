using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
public sealed class OptionsSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public OptionsSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_inject_authorization_header_when_credentials_set()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_send_basic_auth_header_with_correct_credentials()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("alice", "secret");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/echo");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.StartsWith("Basic ", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Auth_should_return_401_when_no_credentials_configured()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            system: _systemFixture.System);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_not_inject_header_when_disabled()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = false;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
