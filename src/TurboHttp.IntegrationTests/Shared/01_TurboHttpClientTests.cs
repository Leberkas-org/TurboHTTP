using System.Net;
using Akka.Actor;
using TurboHttp.Client;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Shared;

public sealed class TurboHttpClientTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;

    public TurboHttpClientTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private TurboHttpClient CreateClient()
        => new(new TurboClientOptions(), Sys);

    [Fact(DisplayName = "CLIENT-001: SendAsync returns successful response")]
    public async Task SendAsync_ReturnsSuccessfulResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "CLIENT-002: BaseAddress resolves relative URIs")]
    public async Task BaseAddress_ResolvesRelativeUris()
    {
        var client = CreateClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{_fixture.Port}/");
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("ping", UriKind.Relative));

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("pong", body);
    }

    [Fact(DisplayName = "CLIENT-003: DefaultRequestHeaders are sent with every request")]
    public async Task DefaultRequestHeaders_SentWithEveryRequest()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Custom-Test", "turbo-value");
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/headers/echo")
        {
            Version = HttpVersion.Version11
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("turbo-value", response.Headers.GetValues("X-Custom-Test"));
    }

    [Fact(DisplayName = "CLIENT-004: DefaultRequestVersion overrides request version")]
    public async Task DefaultRequestVersion_OverridesRequestVersion()
    {
        var client = CreateClient();
        client.DefaultRequestVersion = HttpVersion.Version10;
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "CLIENT-005: CancellationToken cancels in-flight request")]
    public async Task CancellationToken_CancelsInflightRequest()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/delay/5000")
        {
            Version = HttpVersion.Version11
        };

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(request, cts.Token));
    }

    [Fact(DisplayName = "CLIENT-006: Timeout throws TimeoutException for slow responses")]
    public async Task Timeout_ThrowsForSlowResponses()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(200);
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/delay/5000")
        {
            Version = HttpVersion.Version11
        };

        await Assert.ThrowsAnyAsync<TimeoutException>(
            () => client.SendAsync(request, CancellationToken.None));
    }

    [Fact(DisplayName = "CLIENT-007: CancelPendingRequests cancels outstanding requests")]
    public async Task CancelPendingRequests_CancelsOutstandingRequests()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/delay/5000")
        {
            Version = HttpVersion.Version11
        };

        var sendTask = client.SendAsync(request, CancellationToken.None);

        await Task.Delay(100);
        client.CancelPendingRequests();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
    }

    [Fact(DisplayName = "CLIENT-008: 10 sequential requests all return successfully")]
    public async Task SequentialRequests_AllReturnSuccessfully()
    {
        var client = CreateClient();
        var results = new List<HttpResponseMessage>();
        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version11
            };
            results.Add(await client.SendAsync(request, CancellationToken.None));
        }

        Assert.Equal(10, results.Count);
        foreach (var response in results)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("pong", body);
        }
    }

    [Fact(DisplayName = "CLIENT-009: Completing Requests channel shuts down pipeline")]
    public async Task Dispose_CompletingRequestsChannelShutsDownPipeline()
    {
        var client = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
        {
            Version = HttpVersion.Version11
        };
        var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        client.Requests.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var _ in client.Responses.ReadAllAsync(cts.Token))
        {
            // drain remaining responses
        }

        Assert.False(client.Responses.TryRead(out _));
    }

    [Fact(DisplayName = "CLIENT-010: Channel API allows direct request/response streaming")]
    public async Task ChannelApi_AllowsDirectRequestResponseStreaming()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version11
        };

        await client.Requests.WriteAsync(request);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.Responses.ReadAsync(cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }
}
