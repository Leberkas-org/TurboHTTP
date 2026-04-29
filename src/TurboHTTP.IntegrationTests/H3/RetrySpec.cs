using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class RetrySpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RetrySpec(ServerFixture server, ActorSystemFixture systemFixture)
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

    private ClientHelper CreateRetryClient(int maxRetries = 3)
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            configure: builder => builder.WithRetry(x => x.MaxRetries = maxRetries),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_retry_503_and_eventually_return_503()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_retry_429_and_eventually_return_429()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/429");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Theory(Timeout = 20000)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Get_should_succeed_after_N_retries(int n)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient(maxRetries: n);

        var key = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/{n}?key={key}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Put_should_retry_on_503_because_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Put, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Delete_should_retry_on_503_because_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_must_not_retry_on_503_because_non_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_retry_408_and_eventually_return_408()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/408");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_retry_after_seconds_header_on_503()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}