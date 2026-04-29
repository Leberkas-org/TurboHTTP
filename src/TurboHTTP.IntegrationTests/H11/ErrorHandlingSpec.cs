using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
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
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 1), system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_complete_delay_route_after_server_wait()
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
    public async Task ErrorHandling_should_abort_in_flight_request_on_timeout_cancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/delay/10000");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_raise_exception_on_mid_response_connection_abort()
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

    [Theory(Timeout = 20000)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ErrorHandling_should_receive_large_response_headers(int kb)
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

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_return_response_gracefully_with_unknown_content_encoding()
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
    public async Task ErrorHandling_should_return_empty_for_empty_body_with_no_content_length()
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
    public async Task ErrorHandling_should_return_empty_body_for_content_length_zero()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Theory(Timeout = 20000)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task ErrorHandling_should_return_4xx_status_codes_as_response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Theory(Timeout = 20000)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task ErrorHandling_should_return_5xx_status_codes_as_response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_access_custom_unknown_headers()
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
