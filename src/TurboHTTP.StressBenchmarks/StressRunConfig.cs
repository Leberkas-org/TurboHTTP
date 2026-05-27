namespace TurboHTTP.StressBenchmarks;

public sealed record StressRunConfig(
    string ScenarioName,
    int Concurrency,
    TimeSpan Duration,
    TimeSpan WarmupDuration,
    int? RequestBodySize,
    bool DisableKeepAlive);
