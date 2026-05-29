using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Streaming;

public sealed class RawStreamingSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/stream-bytes", () =>
        {
            var chunks = new[]
            {
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new byte[] { 7, 8, 9 }
            };
            return Results.Stream(async stream =>
            {
                foreach (var chunk in chunks)
                {
                    await stream.WriteAsync(chunk);
                }
            }, "application/octet-stream");
        });

        app.MapGet("/stream-text", () =>
        {
            var lines = new[] { "line1\n", "line2\n", "line3\n" };
            return Results.Stream(async stream =>
            {
                foreach (var line in lines)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
                }
            }, "text/plain");
        });

        app.MapGet("/stream-large", () =>
        {
            return Results.Stream(async stream =>
            {
                var chunk = new byte[1024];
                Array.Fill(chunk, (byte)0xAB);
                for (var i = 0; i < 100; i++)
                {
                    await stream.WriteAsync(chunk);
                }
            }, "application/octet-stream");
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Stream_should_return_all_bytes()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-bytes"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
    }

    [Fact(Timeout = 15000)]
    public async Task Stream_should_set_custom_content_type()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("line1\nline2\nline3\n", body);
    }

    [Fact(Timeout = 30000)]
    public async Task Stream_should_handle_large_payload()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/stream-large"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(100 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(0xAB, b));
    }
}
