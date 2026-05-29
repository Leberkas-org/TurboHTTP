using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

[Collection("ServerStress")]
public sealed class BodyFloodReproSpec : ServerSpecBase
{
    private static readonly byte[] Payload = new byte[1 * 1024 * 1024];

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-size", async ctx =>
        {
            long count = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer, CancellationToken)) > 0)
            {
                count += read;
            }

            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(count.ToString(), CancellationToken);
        });
    }

    [Fact(Timeout = 10000)]
    public async Task Post_1mb_body_should_return_correct_size()
    {
        var content = new ByteArrayContent(Payload);
        var response = await Client.PostAsync(
            new Uri($"http://127.0.0.1:{Port}/echo-size"),
            content,
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal((1 * 1024 * 1024).ToString(), body);
    }

    [Fact(Timeout = 120000)]
    public async Task Concurrent_1mb_posts_should_all_succeed()
    {
        var concurrency = 50;
        using var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = concurrency,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };

        var uri = new Uri($"http://127.0.0.1:{Port}/echo-size");
        var errors = new List<string>();
        var succeeded = 0;

        var expectedSize = (1 * 1024 * 1024).ToString();
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            try
            {
                var content = new ByteArrayContent(Payload);
                var response = await client.PostAsync(uri, content, CancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    lock (errors) errors.Add($"[{i}] status={response.StatusCode}");
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                if (body == expectedSize)
                {
                    Interlocked.Increment(ref succeeded);
                }
                else
                {
                    lock (errors) errors.Add($"[{i}] body size mismatch: expected={expectedSize}, actual={body}");
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add($"[{i}] {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        var msg = $"{succeeded}/{concurrency} succeeded";
        if (errors.Count > 0)
        {
            msg += $"\nErrors ({errors.Count}):\n" + string.Join("\n", errors.Take(10));
        }

        Assert.True(succeeded == concurrency, msg);
    }
}
