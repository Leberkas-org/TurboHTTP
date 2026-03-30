using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
public sealed class ExpectContinueIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ExpectContinueIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateExpectClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: builder => builder.Services.Configure<TurboClientDescriptor>(
                builder.Name,
                d => d.Expect100Policy = Expect100Policy.Default),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Expect-001: Small body sent without Expect header returns 200 echo")]
    public async Task SmallBody_Sent_Without_Expect_Header()
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

    [Fact(DisplayName = "Expect-002: Large body triggers 100-continue flow and returns 200 echo")]
    public async Task LargeBody_Triggers_100Continue_Flow()
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

    [Fact(DisplayName = "Expect-003: Server rejection returns 417 Expectation Failed")]
    public async Task Server_Rejection_Returns_417()
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
