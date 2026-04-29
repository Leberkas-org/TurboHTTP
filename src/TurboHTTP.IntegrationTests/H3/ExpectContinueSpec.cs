using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class ExpectContinueSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ExpectContinueSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private ClientHelper CreateExpectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: builder => builder.WithExpectContinue(),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task SmallBody_should_be_sent_without_Expect_header()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateExpectClient();

        var body = "hello";
        var request = new HttpRequestMessage(HttpMethod.Post, "/expect/echo")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 20000)]
    public async Task LargeBody_should_be_sent_over_quic_stream()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateExpectClient();

        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "/expect/large")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 20000)]
    public async Task Server_rejection_should_return_417()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateExpectClient();

        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "/expect/reject")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ExpectationFailed, response.StatusCode);
    }
}
