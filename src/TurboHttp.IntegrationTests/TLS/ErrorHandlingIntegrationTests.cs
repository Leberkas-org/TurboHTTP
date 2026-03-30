using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class ErrorHandlingIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ErrorHandlingIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpsPort, new Version(1, 1), scheme: "https", system: _systemFixture.System);
    }

    [Fact(DisplayName = "Error-TLS-001: Delay route completes after server-side wait over HTTPS")]
    public async Task Delay_Route_Completes_After_Wait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("delayed", body);
    }

    [Fact(DisplayName = "Error-TLS-002: Timeout cancellation aborts in-flight request over HTTPS")]
    public async Task Timeout_Cancellation_Aborts_InFlight_Request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10000");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(DisplayName = "Error-TLS-003: Mid-response connection abort raises exception over HTTPS")]
    public async Task MidResponse_Connection_Abort_Raises_Exception()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/close-mid-response");

        // The server aborts after sending partial body — reading should throw
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(request, cts.Token);
            // Even if SendAsync succeeds, reading the full body should fail
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(DisplayName = "Error-TLS-004: Unknown Content-Encoding returns response gracefully over HTTPS")]
    public async Task Unknown_ContentEncoding_Returns_Response_Gracefully()
    {
        // When the server returns an unknown Content-Encoding, the pipeline
        // passes the response through without decompression rather than throwing.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Error-TLS-005: Empty body with no Content-Length returns empty over HTTPS")]
    public async Task Empty_Body_No_ContentLength_Returns_Empty()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Fact(DisplayName = "Error-TLS-006: Content-Length 0 returns empty body over HTTPS")]
    public async Task ContentLength_Zero_Returns_Empty_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Theory(DisplayName = "Error-TLS-007: 4xx status codes returned as response, not thrown over HTTPS")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Status_4xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Theory(DisplayName = "Error-TLS-008: 5xx status codes returned as response, not thrown over HTTPS")]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Status_5xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }
}
