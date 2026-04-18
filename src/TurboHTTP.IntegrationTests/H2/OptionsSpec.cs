using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H2;

[Collection("H2")]
[Obsolete("Replaced by StreamTests.Acceptance.H2.OptionsSpec")]
public sealed class OptionsSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public OptionsSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_inject_authorization_header_when_credentials_set()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_send_basic_auth_header_with_correct_credentials()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("alice", "secret");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_not_inject_header_when_disabled()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = false;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PooledConnectionLifetime_should_allow_requests_after_connection_expires()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.PooledConnectionLifetime = TimeSpan.FromSeconds(2);
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Wait for connection lifetime to expire
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

        // Should succeed on a new connection
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_work_across_multiple_requests()
    {
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            system: _systemFixture.System,
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/auth");
            var response = await helper.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
