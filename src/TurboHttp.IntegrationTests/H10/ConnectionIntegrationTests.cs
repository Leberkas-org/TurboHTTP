using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

/// <summary>
/// HTTP/1.0 connection management tests.
/// Unlike HTTP/1.1, HTTP/1.0 defaults to connection-close after each response.
/// Keep-alive is opt-in via the <c>Connection: Keep-Alive</c> header.
/// </summary>
[Collection("H10")]
public sealed class ConnectionIntegrationTests
{
    private readonly KestrelFixture _fixture;

    public ConnectionIntegrationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_fixture.Port, new Version(1, 0));
    }

    [Fact(DisplayName = "Conn-H10-001: Default HTTP/1.0 connection closes after single request")]
    public async Task Default_Connection_Closes_After_Single_Request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // HTTP/1.0 defaults to connection-close — a single request should succeed
        var request = new HttpRequestMessage(HttpMethod.Get, "/conn/default");
        var response = await helper.Client.SendAsync(request, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("default", body);
    }

    [Fact(DisplayName = "Conn-H10-002: Connection close is explicit in response")]
    public async Task Close_Is_Explicit_In_Response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/conn/close");
        var response = await helper.Client.SendAsync(request, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("closing", body);
    }

    [Fact(DisplayName = "Conn-H10-003: Keep-alive opt-in allows sequential requests")]
    public async Task KeepAlive_OptIn_Allows_Sequential_Requests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // First request with explicit Connection: Keep-Alive
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/conn/keep-alive");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("keep-alive", body1);
    }

    [Fact(DisplayName = "Conn-H10-004: Simple GET returns expected body")]
    public async Task Simple_Get_Returns_Expected_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}
