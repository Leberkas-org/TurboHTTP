using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ErrorHandlingSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Sync_handler_exception_should_return_500()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/throw-sync"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Async_handler_exception_should_return_500()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/throw-async"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_recover_after_handler_exception()
    {
        await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/throw-sync"),
            CancellationToken);

        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/ok"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
