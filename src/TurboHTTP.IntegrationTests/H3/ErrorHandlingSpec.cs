using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class ErrorHandlingSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ErrorHandlingSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            system: _systemFixture.System);
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
    public async Task ErrorHandling_should_raise_exception_on_stream_abort()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h3/abort");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(request, cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_complete_delay_route_after_server_wait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h3/delay/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_cancel_in_flight_request_on_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h3/delay/10000");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_raise_exception_on_mid_response_connection_abort()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/close-mid-response");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(request, cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(Timeout = 20000)]
    public async Task ErrorHandling_should_return_response_gracefully_with_unknown_content_encoding()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
