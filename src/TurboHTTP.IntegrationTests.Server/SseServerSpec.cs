using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SseServerSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_basic_request()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/echo"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_text_request()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("hello world", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_correct_content_type()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/json", response.Content.Headers.ContentType.MediaType ?? "");
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/nonexistent"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_stream_sse_events()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/events"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("data: event1\n\n", body);
        Assert.Contains("data: event2\n\n", body);
    }
}
