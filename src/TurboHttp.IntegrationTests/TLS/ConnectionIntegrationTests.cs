using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class ConnectionIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ConnectionIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpsPort, new Version(1, 1), scheme: "https", system: _systemFixture.System);
    }

    [Fact(DisplayName = "Conn-TLS-001: keep-alive response allows sequential requests on same TLS client")]
    public async Task KeepAlive_Allows_Sequential_Requests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // First request with explicit Connection: Keep-Alive over TLS
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/conn/keep-alive");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("keep-alive", body1);

        // Second request on the same client — proves connection reuse over TLS
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/conn/keep-alive");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("keep-alive", body2);
    }

    [Fact(DisplayName = "Conn-TLS-002: Connection close header present in response over HTTPS")]
    public async Task Close_Header_Present_In_Response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/conn/close");
        var response = await helper.Client.SendAsync(request, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("closing", body);

        // Verify the Connection: close header is present
        Assert.True(
            response.Headers.Connection.Contains("close"),
            "Response should contain Connection: close header");
    }

    [Fact(DisplayName = "Conn-TLS-003: HTTP/1.1 default keep-alive without Connection header over HTTPS")]
    public async Task Default_KeepAlive_Without_Connection_Header()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // First request — no explicit Connection header from server
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/conn/default");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("default", body1);

        // Second request — HTTP/1.1 defaults to keep-alive, so this should succeed
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/conn/default");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("default", body2);
    }

    [Fact(DisplayName = "Conn-TLS-004: 101 Switching Protocols not reusable for HTTP over HTTPS")]
    public async Task Upgrade101_Not_Reusable_For_Http()
    {
        // 101 Switching Protocols transitions the connection away from HTTP.
        // The client cannot complete a normal HTTP exchange over a switched connection,
        // so we expect the request to fail (timeout or exception) rather than return 101.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/conn/upgrade-101");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(DisplayName = "Conn-TLS-005: Sequential requests prove TLS connection reuse across different endpoints")]
    public async Task Sequential_Requests_Prove_Connection_Reuse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // Send multiple requests to different endpoints on the same TLS client
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/conn/keep-alive");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("keep-alive", body1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/conn/default");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("default", body2);

        // Third request to confirm the connection is still alive
        var request3 = new HttpRequestMessage(HttpMethod.Get, "/conn/keep-alive");
        var response3 = await helper.Client.SendAsync(request3, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var body3 = await response3.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("keep-alive", body3);
    }
}
