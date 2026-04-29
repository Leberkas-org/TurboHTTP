using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class ExpectContinueSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ExpectContinueSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateExpectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithExpectContinue(),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task SmallBody_should_be_sent_without_expect_header_over_https()
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
    public async Task LargeBody_should_trigger_100_continue_flow_and_return_200_echo_over_https()
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
    public async Task Server_rejection_should_return_417_expectation_failed_over_https()
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
