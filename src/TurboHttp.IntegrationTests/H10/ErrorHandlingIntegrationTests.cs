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

    [Fact(Timeout = 30000, DisplayName = "Error-H10-001: Delay route completes after server-side wait")]
    public async Task Delay_Route_Completes_After_Wait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-002: Timeout cancellation aborts in-flight request")]
    public async Task Timeout_Cancellation_Aborts_InFlight_Request()
    {
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10000");
        using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, sendCts.Token));
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-003: Mid-response connection abort returns truncated body")]
    public async Task MidResponse_Connection_Abort_Returns_Truncated_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/close-mid-response");

        // HTTP/1.0 reads until EOF — server aborts after writing 7 bytes ("partial")
        // despite claiming Content-Length: 10000. The decoder returns the truncated body.
        var response = await helper.Client.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.True(body.Length < 10000, $"Body should be truncated but was {body.Length} bytes");
    }

    [Theory(Timeout = 30000, DisplayName = "Error-H10-004: Large response headers received correctly")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Large_Response_Headers_Received(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/edge/large-header/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Large-Header", out var values));
        var headerValue = string.Join("", values);
        Assert.Equal(kb * 1024, headerValue.Length);
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-005: Unknown Content-Encoding returns response gracefully")]
    public async Task Unknown_ContentEncoding_Returns_Response_Gracefully()
    {
        // When the server returns an unknown Content-Encoding, the pipeline
        // passes the response through without decompression rather than throwing.
        // Verify the client completes without hanging or crashing.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-006: Empty body with no Content-Length returns empty")]
    public async Task Empty_Body_No_ContentLength_Returns_Empty()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-007: Content-Length 0 returns empty body")]
    public async Task ContentLength_Zero_Returns_Empty_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Theory(Timeout = 30000, DisplayName = "Error-H10-008: 4xx status codes returned as response, not thrown")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Status_4xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Theory(Timeout = 30000, DisplayName = "Error-H10-009: 5xx status codes returned as response, not thrown")]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Status_5xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(Timeout = 30000, DisplayName = "Error-H10-010: Custom X-Unknown headers are accessible")]
    public async Task Custom_Unknown_Headers_Are_Accessible()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
