using Microsoft.AspNetCore.Builder;

namespace TurboHTTP.StressBenchmarks;

public interface IStressScenario
{
    string Name { get; }
    StressRunConfig DefaultConfig { get; }
    void ConfigureRoutes(WebApplication app);
    Func<HttpClient, Uri, Task<HttpResponseMessage>> CreateRequestFunc();
}
