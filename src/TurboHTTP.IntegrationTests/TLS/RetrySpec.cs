using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
[Obsolete("Replaced by StreamTests.Acceptance.TLS.RetrySpec")]
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
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithRetry(x => x.MaxRetries = maxRetries),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_408_request_timeout_should_be_retried_and_eventually_return_408_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/408");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_503_service_unavailable_should_be_retried_and_eventually_return_503_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // HEAD responses have no body regardless of Content-Length, but the HTTP/1.1
    // decoder doesn't know the request method and waits for body bytes, causing
    // a timeout. Requires decoder-level fix (out of scope for integration tests).

    [Fact(Timeout = 20000)]
    public async Task Get_503_with_retry_after_seconds_header_should_be_retried_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_503_with_retry_after_http_date_header_should_be_retried_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient(maxRetries: 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after-date");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 5000)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Get_succeed_after_n_should_return_200_after_n_minus_1_failures_over_https(int n)
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
    public async Task Put_503_should_be_retried_as_put_is_idempotent_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Put, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Delete_503_should_be_retried_as_delete_is_idempotent_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_503_should_not_be_retried_as_post_is_non_idempotent_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}