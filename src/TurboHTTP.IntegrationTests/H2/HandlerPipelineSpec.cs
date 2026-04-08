using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H2;

[Collection("H2")]
public sealed class HandlerPipelineSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public HandlerPipelineSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private sealed class TestHeaderHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Typed-Handler", "active");
            return request;
        }
    }

    [Fact(Timeout = 20000)]
    public async Task UseRequest_should_inject_custom_header()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: b => b.UseRequest(req =>
            {
                req.Headers.TryAddWithoutValidation("X-Custom-Injected", "hello");
                return req;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Custom-Injected", out var vals),
            "X-Custom-Injected header must be echoed back");
        Assert.Equal("hello", string.Join(",", vals));
    }

    [Fact(Timeout = 20000)]
    public async Task AddHandler_should_process_request_with_typed_handler()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: b => b.AddHandler<TestHeaderHandler>(),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Typed-Handler", out var vals),
            "X-Typed-Handler header must be echoed back");
        Assert.Equal("active", string.Join(",", vals));
    }

    [Fact(Timeout = 20000)]
    public async Task Multiple_handlers_should_execute_in_registration_order()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-First", "1");
                    return req;
                })
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Second", "2");
                    return req;
                }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-First", out var firstVals), "X-First must be present");
        Assert.True(response.Headers.TryGetValues("X-Second", out var secondVals), "X-Second must be present");
        Assert.Equal("1", string.Join(",", firstVals));
        Assert.Equal("2", string.Join(",", secondVals));
    }

    [Fact(Timeout = 20000)]
    public async Task Handler_should_work_with_redirect_pipeline()
    {
        // Handler injects X-Handler-Redirect — 302 — /headers/echo
        // Redirect stage forwards the original request (with injected headers) to the new URL.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.H2Port,
            new Version(2, 0),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Handler-Redirect", "present");
                    return req;
                })
                .WithRedirect(),
            system: _systemFixture.System);

        // /redirect/302/headers/echo — 302 — /headers/echo which echoes request headers
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Redirect", out var vals),
            "X-Handler-Redirect must still be present after redirect");
        Assert.Equal("present", string.Join(",", vals));
    }
}
