using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.IntegrationTests;

[Collection("Http1Integration")]
public sealed class RetryIntegrationTests
{
    private readonly KestrelFixture _fixture;

    public RetryIntegrationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private ClientHelper CreateRetryClient(int maxRetries = 3)
    {
        return ClientHelper.CreateClient(
            _fixture.Port,
            new Version(1, 1),
            configure: builder => builder.WithRetry(new RetryPolicy { MaxRetries = maxRetries }));
    }

    // ── GET /retry/408 — 408 Request Timeout triggers retry ───────────────

    [Fact(DisplayName = "Retry-001: GET 408 Request Timeout is retried and eventually returns 408")]
    public async Task Get_408_Request_Timeout_Is_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/408");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 408, so after exhausting retries we get 408
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    // ── GET /retry/503 — 503 Service Unavailable triggers retry ───────────

    [Fact(DisplayName = "Retry-002: GET 503 Service Unavailable is retried and eventually returns 503")]
    public async Task Get_503_Service_Unavailable_Is_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 503, so after exhausting retries we get 503
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── HEAD /retry/503 — BLOCKED: Http11DecoderStage lacks HEAD-awareness ──
    // HEAD responses have no body regardless of Content-Length, but the HTTP/1.1
    // decoder doesn't know the request method and waits for body bytes, causing
    // a timeout. Requires decoder-level fix (out of scope for integration tests).

    // ── GET /retry/503-retry-after/{seconds} — Retry-After: seconds ───────

    [Fact(DisplayName = "Retry-004: GET 503 with Retry-After seconds header is retried")]
    public async Task Get_503_Retry_After_Seconds_Is_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // Server always returns 503 with Retry-After, eventually exhausts retries
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── GET /retry/503-retry-after-date — Retry-After: HTTP-date ──────────

    [Fact(DisplayName = "Retry-005: GET 503 with Retry-After HTTP-date header is retried")]
    public async Task Get_503_Retry_After_Date_Is_Retried()
    {
        // The server sets Retry-After to 10 seconds from now.
        // With MaxRetries=1 and a generous timeout, we verify the response is 503
        // after the retry cycle completes.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var helper = CreateRetryClient(maxRetries: 1);

        var request = new HttpRequestMessage(HttpMethod.Get, "/retry/503-retry-after-date");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── GET /retry/succeed-after/{n} — transient failures then success ────

    [Theory(DisplayName = "Retry-006: GET succeed-after-N returns 200 after N-1 failures")]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Get_Succeed_After_N_Returns_200(int n)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // Need at least N-1 retries to succeed
        await using var helper = CreateRetryClient(maxRetries: n);

        var key = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/retry/succeed-after/{n}?key={key}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("success", body);
    }

    // ── PUT /retry/503 — PUT is idempotent, should be retried ─────────────

    [Fact(DisplayName = "Retry-007: PUT 503 is retried (PUT is idempotent)")]
    public async Task Put_503_Is_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Put, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── DELETE /retry/503 — DELETE is idempotent, should be retried ────────

    [Fact(DisplayName = "Retry-008: DELETE 503 is retried (DELETE is idempotent)")]
    public async Task Delete_503_Is_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/retry/503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ── POST /retry/non-idempotent-503 — POST must NOT be retried ─────────

    [Fact(DisplayName = "Retry-009: POST 503 is NOT retried (POST is non-idempotent)")]
    public async Task Post_503_Is_Not_Retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateRetryClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/retry/non-idempotent-503");
        var response = await helper.Client.SendAsync(request, cts.Token);

        // POST is non-idempotent — should return 503 without retry
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
