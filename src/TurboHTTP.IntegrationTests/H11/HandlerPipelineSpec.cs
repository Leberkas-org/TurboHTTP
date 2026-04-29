using System.Net;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
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
    public async Task HandlerPipeline_should_inject_custom_header_with_use_request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
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
    public async Task HandlerPipeline_should_add_header_to_response_with_use_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.UseResponse((_, res) =>
            {
                res.Headers.TryAddWithoutValidation("X-Handler-Added", "injected");
                return res;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Added", out var vals),
            "X-Handler-Added header must be present on the response");
        Assert.Equal("injected", string.Join(",", vals));
    }

    [Fact(Timeout = 20000)]
    public async Task HandlerPipeline_should_process_request_with_typed_handler()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
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
    public async Task HandlerPipeline_should_execute_multiple_handlers_in_registration_order()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
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
    public async Task HandlerPipeline_should_see_original_request_on_response()
    {
        string? capturedOriginalUrl = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b.UseResponse((original, res) =>
            {
                capturedOriginalUrl = original.RequestUri?.PathAndQuery;
                return res;
            }),
            system: _systemFixture.System);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/hello", capturedOriginalUrl);
    }

    [Fact(Timeout = 20000)]
    public async Task HandlerPipeline_should_work_with_redirect_pipeline()
    {
        // Handler injects X-Handler-Redirect → 302 → /headers/echo
        // Redirect stage forwards the original request (with injected headers) to the new URL.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Handler-Redirect", "present");
                    return req;
                })
                .WithRedirect(),
            system: _systemFixture.System);

        // /redirect/302/headers/echo → 302 → /headers/echo which echoes request headers
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/headers/echo"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Redirect", out var vals),
            "X-Handler-Redirect must still be present after redirect");
        Assert.Equal("present", string.Join(",", vals));
    }

    [Fact(Timeout = 20000)]
    public async Task HandlerPipeline_should_work_with_compression_pipeline()
    {
        // UseResponse handler receives a response after decompression stage has run.
        // The handler should see a normal (decompressed) response.
        int? capturedContentLength = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .WithDecompression()
                .UseResponse((_, res) =>
                {
                    capturedContentLength = (int?)res.Content.Headers.ContentLength;
                    return res;
                }),
            system: _systemFixture.System);

        // /compress/gzip/1 → gzip-encoded 1 KB body; WithDecompression decompresses it
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1024, body.Length);
    }

    [Fact(Timeout = 20000)]
    public async Task HandlerPipeline_should_work_with_cookie_pipeline()
    {
        // UseRequest injects X-From-Handler.
        // WithCookies causes the jar to inject a Cookie header on subsequent requests.
        // /interaction/echo-all-headers echoes X-* headers AND Cookie as X-Received-Cookie.
        var jar = new MemoryCookieStore();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            configure: b => b
                .UseRequest(req =>
                {
                    req.Headers.TryAddWithoutValidation("X-From-Handler", "yes");
                    return req;
                })
                .WithCookies(jar),
            system: _systemFixture.System);

        // Seed the jar with a cookie via the set endpoint
        var seedResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookie/set/testcookie/testvalue"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        // Now both the handler header and the cookie should be present
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/interaction/echo-all-headers"), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-From-Handler", out var handlerVals),
            "X-From-Handler must be echoed back");
        Assert.Equal("yes", string.Join(",", handlerVals));
        Assert.True(
            response.Headers.TryGetValues("X-Received-Cookie", out var cookieVals),
            "X-Received-Cookie must be present (cookie jar injected cookie)");
        Assert.Contains("testcookie=testvalue", string.Join(",", cookieVals));
    }
}
