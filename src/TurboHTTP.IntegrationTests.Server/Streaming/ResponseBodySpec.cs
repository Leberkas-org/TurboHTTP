using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Streaming;

public sealed class ResponseBodySpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Streaming_response_without_content_length_should_deliver_all_chunks()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/stream-no-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("chunk1chunk2chunk3", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Streaming_response_without_content_length_should_set_content_type()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/stream-no-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
    }

    [Fact(Timeout = 15000)]
    public async Task Response_with_content_length_should_return_exact_body()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/with-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("exact-length-body", body);
        Assert.Equal(17, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 15000)]
    public async Task NoContent_204_should_have_empty_body()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/no-content"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 15000)]
    public async Task NotModified_304_should_have_empty_body()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/not-modified"),
            CancellationToken);

        Assert.Equal((HttpStatusCode)304, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(body);
    }
}
