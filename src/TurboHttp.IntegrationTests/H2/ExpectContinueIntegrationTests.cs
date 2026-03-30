using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests.H2;

[Collection("H2")]
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
            _server.H2Port,
            new Version(2, 0),
            configure: builder => builder.Services.Configure<TurboClientDescriptor>(
                builder.Name,
                d => d.Expect100Policy = Expect100Policy.Default),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Expect-H2-001: Small body sent without Expect header returns 200 echo over HTTP/2")]
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

    [Fact(DisplayName = "Expect-H2-002: Large body sent without 100-continue returns 200 echo over HTTP/2")]
    public async Task LargeBody_Sent_Without_100Continue()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateExpectClient();

        // HTTP/2 does not use 100-continue — body is sent with the request stream directly
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

    [Fact(DisplayName = "Expect-H2-003: Server rejection returns 417 over HTTP/2")]
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
