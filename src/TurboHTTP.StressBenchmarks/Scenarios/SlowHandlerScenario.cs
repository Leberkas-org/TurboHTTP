using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.StressBenchmarks.Scenarios;

public sealed class SlowHandlerScenario : IStressScenario
{
    public string Name => "slow-handler";

    public StressRunConfig DefaultConfig => new(
        Name,
        Concurrency: 500,
        Duration: TimeSpan.FromSeconds(30),
        WarmupDuration: TimeSpan.FromSeconds(5),
        RequestBodySize: null,
        DisableKeepAlive: false);

    public void ConfigureRoutes(WebApplication app)
    {
        app.MapGet("/stress", async () =>
        {
            await Task.Delay(2000);
            return Results.Content("OK", "text/plain");
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
