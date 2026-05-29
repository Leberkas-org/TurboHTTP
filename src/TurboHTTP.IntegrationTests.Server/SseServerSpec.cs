using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SseServerSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/echo", () => Results.Ok("ok"));
        app.MapGet("/text", () => Results.Ok("hello world"));
        app.MapGet("/events", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            var events = new[] { "event1", "event2" };
            foreach (var evt in events)
            {
                var data = Encoding.UTF8.GetBytes($"data: {evt}\n\n");
                await ctx.Response.Body.WriteAsync(data);
            }
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_basic_request()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/echo"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_text_request()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("hello world", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_correct_content_type()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/json", response.Content.Headers.ContentType.MediaType ?? "");
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/nonexistent"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_stream_sse_events()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/events"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("data: event1\n\n", body);
        Assert.Contains("data: event2\n\n", body);
    }
}
