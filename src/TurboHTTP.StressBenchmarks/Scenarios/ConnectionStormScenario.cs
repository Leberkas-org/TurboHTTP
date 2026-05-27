using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.StressBenchmarks.Scenarios;

public sealed class ConnectionStormScenario : IStressScenario
{
    public string Name => "connection-storm";

    public StressRunConfig DefaultConfig => new(
        Name,
        Concurrency: 200,
        Duration: TimeSpan.FromSeconds(30),
        WarmupDuration: TimeSpan.FromSeconds(5),
        RequestBodySize: null,
        DisableKeepAlive: true);

    public void ConfigureRoutes(WebApplication app)
    {
        app.MapGet("/stress", () => Results.Content("OK", "text/plain"));
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
