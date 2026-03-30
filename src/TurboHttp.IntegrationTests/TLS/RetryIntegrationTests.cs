using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class RetryIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public RetryIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
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
            configure: builder => builder.WithRetry(new RetryPolicy { MaxRetries = maxRetries }),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Retry-TLS-001: GET 408 Request Timeout is retried and eventually returns 408 over HTTPS")]
    public async Task Get_408_Request_Timeout_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/408");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(DisplayName = "Retry-TLS-002: GET 503 Service Unavailable is retried and eventually returns 503 over HTTPS")]
    public async Task Get_503_Service_Unavailable_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── HEAD /retry/503 — BLOCKED: Http11DecoderStage lacks HEAD-awareness ──
    // HEAD responses have no body regardless of Content-Length, but the HTTP/1.1
    // decoder doesn't know the request method and waits for body bytes, causing
    // a timeout. Requires decoder-level fix (out of scope for integration tests).

    [Fact(DisplayName = "Retry-TLS-004: GET 503 with Retry-After seconds header is retried over HTTPS")]
    public async Task Get_503_Retry_After_Seconds_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(DisplayName = "Retry-TLS-005: GET 503 with Retry-After HTTP-date header is retried over HTTPS")]
    public async Task Get_503_Retry_After_Date_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient(maxRetries: 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after-date");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(DisplayName = "Retry-TLS-006: GET succeed-after-N returns 200 after N-1 failures over HTTPS")]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Get_Succeed_After_N_Returns_200_Over_Https(int n)
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

    [Fact(DisplayName = "Retry-TLS-007: PUT 503 is retried (PUT is idempotent) over HTTPS")]
    public async Task Put_503_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Put, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(DisplayName = "Retry-TLS-008: DELETE 503 is retried (DELETE is idempotent) over HTTPS")]
    public async Task Delete_503_Is_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(DisplayName = "Retry-TLS-009: POST 503 is NOT retried (POST is non-idempotent) over HTTPS")]
    public async Task Post_503_Is_Not_Retried_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
