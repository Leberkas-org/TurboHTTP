using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class ResilienceSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ResilienceSpec(ServerFixture server, ActorSystemFixture systemFixture)
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

    [Fact(Timeout = 20000)]
    public async Task Timeout_should_cancel_request_after_deadline()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h3/delay/30000");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => helper.Client.SendAsync(request, cts.Token));
    }

    [Fact(Timeout = 20000)]
    public async Task Connection_reuse_should_survive_multiple_requests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
            var response = await helper.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Large_response_body_should_be_fully_received()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/large/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, body.Length);
    }

    [Fact(Timeout = 20000)]
    public async Task Connection_should_survive_pipeline_stress()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var tasks = Enumerable.Range(0, 20)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
                return helper.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 20000)]
    public async Task Partial_body_read_should_not_corrupt_next_request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response1 = await helper.Client.SendAsync(request1, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response2 = await helper.Client.SendAsync(request2, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Interleaved_concurrent_requests_should_not_corrupt_responses()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
                return helper.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, async r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync(cts.Token);
            Assert.Equal("Hello World", body);
        });
    }

    [Fact(Timeout = 20000)]
    public async Task Slow_body_should_be_fully_received()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-body/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Contains("slow-body-first-half", body);
        Assert.Contains("slow-body-second-half", body);
    }
}
