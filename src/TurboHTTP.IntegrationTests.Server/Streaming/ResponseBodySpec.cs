using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Streaming;

public sealed class ResponseBodySpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/stream-no-cl", () =>
        {
            var chunks = new[] { "chunk1", "chunk2", "chunk3" };
            return Results.Stream(async stream =>
            {
                foreach (var chunk in chunks)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(chunk));
                }
            }, "text/plain");
        });

        app.MapGet("/with-cl", (HttpContext ctx) =>
        {
            var body = Encoding.UTF8.GetBytes("exact-length-body");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = body.Length;
            return ctx.Response.Body.WriteAsync(body).AsTask();
        });

        app.MapGet("/no-content", () => Results.NoContent());

        app.MapGet("/not-modified", () => Results.StatusCode(304));
    }

    [Fact(Timeout = 15000)]
    public async Task Streaming_response_without_content_length_should_deliver_all_chunks()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-no-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("chunk1chunk2chunk3", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Streaming_response_without_content_length_should_set_content_type()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-no-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
    }

    [Fact(Timeout = 15000)]
    public async Task Response_with_content_length_should_return_exact_body()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/with-cl"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("exact-length-body", body);
        Assert.Equal(17, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 15000)]
    public async Task NoContent_204_should_have_empty_body()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/no-content"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 15000)]
    public async Task NotModified_304_should_have_empty_body()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/not-modified"),
            CancellationToken);

        Assert.Equal((HttpStatusCode)304, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(body);
    }
}
