using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class HandlerTimeoutSpec : ServerSpecBase
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.HandlerTimeout = TimeSpan.FromMilliseconds(500);
            options.HandlerGracePeriod = TimeSpan.FromMilliseconds(500);
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/fast", () => Results.Ok("ok"));

        app.MapGet("/block-forever", async () =>
        {
            await Task.Delay(Timeout.Infinite);
        });

        app.MapGet("/block-ignore-cancel", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
        });

        app.MapGet("/started-body-then-block", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("partial");
            await ctx.Response.Body.FlushAsync();
            await Task.Delay(TimeSpan.FromSeconds(30));
        });
    }

    [Fact(Timeout = 10000)]
    public async Task Fast_handler_should_return_200()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Hard_timeout_should_return_503_when_headers_not_started()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-ignore-cancel"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Soft_timeout_cancels_handler_that_ignores_cancel()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-forever"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Hard_timeout_should_not_set_503_when_body_already_started()
    {
        try
        {
            var response = await Client.GetAsync(
                new Uri($"http://127.0.0.1:{Port}/started-body-then-block"),
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken);

            Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        catch (HttpRequestException)
        {
            // Connection reset is acceptable — the key assertion is no 503
        }
    }
}
