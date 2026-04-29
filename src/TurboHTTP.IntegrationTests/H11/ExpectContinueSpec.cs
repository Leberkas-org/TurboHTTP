using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
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
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.WithExpectContinue(),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task ExpectContinue_should_send_small_body_without_expect_header()
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
    public async Task ExpectContinue_should_trigger_100_continue_flow_with_large_body()
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
    public async Task ExpectContinue_should_return_417_on_server_rejection()
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
