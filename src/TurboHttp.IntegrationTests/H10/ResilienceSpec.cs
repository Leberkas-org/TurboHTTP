using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
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
        ClientHelper.CreateClient(_server.HttpPort, new Version(1, 0), system: _systemFixture.System);

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_cause_exception_on_content_length_mismatch()
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

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_fail_gracefully_on_corrupt_gzip()
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

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_fail_gracefully_on_corrupt_brotli()
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

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_detect_truncated_body()
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

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_succeed_with_slow_headers_within_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/500"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("slow-headers", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_succeed_with_slow_body_within_timeout()
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

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_cause_cancellation_when_slow_headers_exceed_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/10000"), cts.Token));
    }

    [Fact(Timeout = 30000)]
    public async Task Resilience_should_cause_exception_on_empty_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var helper = CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/resilience/empty-response"), cts.Token));
    }
}
