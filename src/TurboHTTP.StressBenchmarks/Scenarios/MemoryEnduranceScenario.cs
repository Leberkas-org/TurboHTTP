using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.StressBenchmarks.Scenarios;

public sealed class MemoryEnduranceScenario : IStressScenario
{
    public string Name => "memory-endurance";

    public StressRunConfig DefaultConfig => new(
        Name,
        Concurrency: 100,
        Duration: TimeSpan.FromSeconds(120),
        WarmupDuration: TimeSpan.FromSeconds(5),
        RequestBodySize: null,
        DisableKeepAlive: false);

    public void ConfigureRoutes(WebApplication app)
    {
        app.MapGet("/stress", async () =>
        {
            await Task.Delay(10);
            return Results.Json(new { message = "Hello, World!", timestamp = DateTime.UtcNow });
        });
    }

    public Func<HttpClient, Uri, Task<HttpResponseMessage>> CreateRequestFunc()
    {
        return static (client, baseUri) =>
        {
            var uri = new Uri(baseUri, "/stress");
            return client.GetAsync(uri);
        };
    }
}
