using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.StressBenchmarks.Scenarios;

public sealed class BodyFloodScenario : IStressScenario
{
    private static readonly byte[] Payload = GeneratePayload(1 * 1024 * 1024);

    public string Name => "body-flood";

    public StressRunConfig DefaultConfig => new(
        Name,
        Concurrency: 200,
        Duration: TimeSpan.FromSeconds(30),
        WarmupDuration: TimeSpan.FromSeconds(5),
        RequestBodySize: 1 * 1024 * 1024,
        DisableKeepAlive: false);

    public void ConfigureRoutes(WebApplication app)
    {
        app.MapPost("/stress", async ctx =>
        {
            long count = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                count += read;
            }
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(string.Concat("received:", count.ToString()));
        });
    }

    public Func<HttpClient, Uri, Task<HttpResponseMessage>> CreateRequestFunc()
    {
        return static (client, baseUri) =>
        {
            var uri = new Uri(baseUri, "/stress");
            var content = new ByteArrayContent(Payload);
            return client.PostAsync(uri, content);
        };
    }

    private static byte[] GeneratePayload(int sizeBytes)
    {
        var payload = new byte[sizeBytes];
        for (var i = 0; i < sizeBytes; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }
}
