using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
public sealed class RetrySpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RetrySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateRetryClient(int maxRetries = 3)
    {
        return ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            configure: builder => builder.WithRetry(x => x.MaxRetries = maxRetries),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_get_408_and_return_408_eventually()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/408");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 408, so after exhausting retries we get 408
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_get_503_and_return_503_eventually()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 503, so after exhausting retries we get 503
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_get_503_with_retry_after_seconds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 503 with Retry-After, eventually exhausts retries
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_get_503_with_retry_after_date()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient(maxRetries: 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after-date");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 30000)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Retry_should_return_200_after_n_failures(int n)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient(maxRetries: n);

        var key = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/{n}?key={key}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_put_503_because_put_is_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Put, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_retry_delete_503_because_delete_is_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Retry_should_not_retry_post_503_because_post_is_non_idempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // POST is non-idempotent — should return 503 without retry
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}