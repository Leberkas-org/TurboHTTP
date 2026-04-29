using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class ErrorHandlingSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ErrorHandlingSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpsPort, new Version(1, 1), scheme: "https", system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Delay_route_should_complete_after_server_side_wait_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Timeout_cancellation_should_abort_in_flight_request_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10000");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(Timeout = 20000)]
    public async Task Mid_response_connection_abort_should_raise_exception_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Unknown_content_encoding_should_return_response_gracefully_over_https()
    {
        // When the server returns an unknown Content-Encoding, the pipeline
        // passes the response through without decompression rather than throwing.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Empty_body_with_no_content_length_should_return_empty_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Content_length_zero_should_return_empty_body_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Theory(Timeout = 5000)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Status_4xx_should_be_returned_as_response_not_thrown_over_https(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Theory(Timeout = 5000)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Status_5xx_should_be_returned_as_response_not_thrown_over_https(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }
}
