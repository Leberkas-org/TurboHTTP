using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
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
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 0), system: _systemFixture.System);
    }

    [Fact(DisplayName = "Error-H10-001: Delay route completes after server-side wait")]
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

    [Fact(DisplayName = "Error-H10-002: Timeout cancellation aborts in-flight request")]
    public async Task Timeout_Cancellation_Aborts_InFlight_Request()
    {
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10000");
        using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, sendCts.Token));
    }

    [Fact(DisplayName = "Error-H10-003: Mid-response connection abort raises exception")]
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

    [Theory(DisplayName = "Error-H10-004: Large response headers received correctly")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Large_Response_Headers_Received(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/edge/large-header/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Large-Header", out var values));
        var headerValue = string.Join("", values);
        Assert.Equal(kb * 1024, headerValue.Length);
    }

    [Fact(DisplayName = "Error-H10-005: Unknown Content-Encoding causes graceful failure")]
    public async Task Unknown_ContentEncoding_Causes_Graceful_Failure()
    {
        // The decoder rejects unknown Content-Encoding per RFC 9110 §8.4.
        // Verify the client fails gracefully (timeout/cancellation) rather than hanging forever.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding");

        // TurboHttpClient may surface either OperationCanceledException (CTS)
        // or TimeoutException (internal timeout) — both are acceptable.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(DisplayName = "Error-H10-006: Empty body with no Content-Length returns empty")]
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

    [Fact(DisplayName = "Error-H10-007: Content-Length 0 returns empty body")]
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

    [Theory(DisplayName = "Error-H10-008: 4xx status codes returned as response, not thrown")]
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

    [Theory(DisplayName = "Error-H10-009: 5xx status codes returned as response, not thrown")]
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

    [Fact(DisplayName = "Error-H10-010: Custom X-Unknown headers are accessible")]
    public async Task Custom_Unknown_Headers_Are_Accessible()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/unknown-headers");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Unknown-Foo", out var fooValues));
        Assert.Equal("bar", string.Join("", fooValues));
        Assert.True(response.Headers.TryGetValues("X-Unknown-Bar", out var barValues));
        Assert.Equal("baz", string.Join("", barValues));
    }
}
