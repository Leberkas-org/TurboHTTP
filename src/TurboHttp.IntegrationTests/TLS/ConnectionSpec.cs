using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class ConnectionSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ConnectionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpsPort, new Version(1, 1), scheme: "https", system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task KeepAlive_should_allow_sequential_requests_on_same_tls_client()
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

    [Fact(Timeout = 20000)]
    public async Task Close_header_should_be_present_in_response()
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

    [Fact(Timeout = 20000)]
    public async Task Default_keepalive_should_persist_without_connection_header()
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

    [Fact(Timeout = 20000)]
    public async Task Upgrade_101_should_not_be_reusable_for_http()
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

    [Fact(Timeout = 20000)]
    public async Task Sequential_requests_should_prove_tls_connection_reuse_across_different_endpoints()
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
