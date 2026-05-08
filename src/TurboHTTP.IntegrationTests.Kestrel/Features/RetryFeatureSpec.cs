using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class RetryFeatureSpec : FeatureSpecBase
{
    public RetryFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_exhaust_retries_for_408(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r => r.MaxRetries = 2));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/retry/408"), ct);

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_exhaust_retries_for_503(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r => r.MaxRetries = 2));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/retry/503"), ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_succeed_after_transient_failures(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r => r.MaxRetries = 3));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/retry/succeed-after/2"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_retry_idempotent_put(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r => r.MaxRetries = 2));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, "/retry/503"), ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_not_retry_non_idempotent_post(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r => r.MaxRetries = 3));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503")
            {
                Content = new StringContent("body")
            }, ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Retry_should_respect_retry_after_header(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRetry(r =>
        {
            r.MaxRetries = 1;
            r.RespectRetryAfter = true;
        }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1"), ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
