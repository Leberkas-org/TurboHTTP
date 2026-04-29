using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
public sealed class ResilienceSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ResilienceSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient() =>
        ClientHelper.CreateClient(_server.HttpPort, new Version(1, 1), system: _systemFixture.System);

    [Fact(Timeout = 20000)]
    public async Task Request_should_fail_when_content_length_mismatches()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/content-length-mismatch"), cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(Timeout = 20000)]
    public async Task Corrupt_gzip_response_should_not_crash_client()
    {
        // ContentEncodingBidiStage catches decompression failures and passes the raw
        // response through unmodified — no exception, no hang, no crash.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/corrupt-gzip"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Body is readable (raw bytes passed through) — client does not crash.
        await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "Client must not hang — CTS must not have fired.");
    }

    [Fact(Timeout = 20000)]
    public async Task Corrupt_brotli_response_should_not_crash_client()
    {
        // ContentEncodingBidiStage catches decompression failures and passes the raw
        // response through unmodified — no exception, no hang, no crash.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/corrupt-br"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Body is readable (raw bytes passed through) — client does not crash.
        await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested, "Client must not hang — CTS must not have fired.");
    }

    [Fact(Timeout = 20000)]
    public async Task Truncated_body_should_cause_exception()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/truncated-body/4"), cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(Timeout = 20000)]
    public async Task Slow_headers_within_timeout_should_succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/500"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("slow-headers", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Slow_body_within_timeout_should_succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-body/500"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Contains("slow-body-first-half", body);
        Assert.Contains("slow-body-second-half", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Slow_headers_exceeding_timeout_should_cause_cancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/10000"), cts.Token));
    }

    [Fact(Timeout = 20000)]
    public async Task Empty_response_should_cause_exception()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/empty-response"), cts.Token));
    }
}
