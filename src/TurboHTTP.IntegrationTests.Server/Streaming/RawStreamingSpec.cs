using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Streaming;

public sealed class RawStreamingSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Stream_should_return_all_bytes()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/stream-bytes"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
    }

    [Fact(Timeout = 15000)]
    public async Task Stream_should_set_custom_content_type()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/stream-text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("line1\nline2\nline3\n", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Stream_should_handle_large_payload()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/stream-large"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(100 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(0xAB, b));
    }
}
