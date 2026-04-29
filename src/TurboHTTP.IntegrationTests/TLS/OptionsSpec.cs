using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

/// <summary>
/// Integration tests for TurboClientOptions wiring over HTTPS/TLS.
/// Verifies Credentials and PreAuthenticate work correctly when TLS is involved.
/// </summary>
[Collection("TLS")]
public sealed class OptionsSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private ClientHelper? _helper;

    public OptionsSpec(ServerFixture server)
    {
        _server = server;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_inject_authorization_header_over_tls()
    {
        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("testuser", "testpass");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await _helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task PreAuthenticate_should_send_basic_auth_header_with_correct_credentials_over_tls()
    {
        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("alice", "secret");
                opts.PreAuthenticate = true;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/echo");

        var response = await _helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.StartsWith("Basic ", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Auth_should_return_401_when_no_credentials_configured_over_tls()
    {
        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth");

        var response = await _helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
