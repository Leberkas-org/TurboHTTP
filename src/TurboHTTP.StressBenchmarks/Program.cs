using TurboHTTP.StressBenchmarks;
using TurboHTTP.StressBenchmarks.Reporting;
using TurboHTTP.StressBenchmarks.Scenarios;

var scenarios = new Dictionary<string, IStressScenario>(StringComparer.OrdinalIgnoreCase)
{
    ["slow-handler"] = new SlowHandlerScenario(),
    ["connection-storm"] = new ConnectionStormScenario(),
    ["body-flood"] = new BodyFloodScenario(),
    ["memory-endurance"] = new MemoryEnduranceScenario()
};

var scenarioName = "all";
var serverFilter = "both";
int? durationOverride = null;
int? concurrencyOverride = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario" when i + 1 < args.Length:
            scenarioName = args[++i];
            break;
        case "--server" when i + 1 < args.Length:
            serverFilter = args[++i];
            break;
        case "--duration" when i + 1 < args.Length:
            durationOverride = int.Parse(args[++i]);
            break;
        case "--concurrency" when i + 1 < args.Length:
            concurrencyOverride = int.Parse(args[++i]);
            break;
    }
}

ThreadPool.GetMinThreads(out var w, out var io);
ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

Console.WriteLine("TurboHTTP Stress Benchmarks");
Console.WriteLine(string.Concat("Scenario: ", scenarioName, " | Server: ", serverFilter));
Console.WriteLine();

var toRun = scenarioName.Equals("all", StringComparison.OrdinalIgnoreCase)
    ? scenarios.Values.ToList()
    : scenarios.TryGetValue(scenarioName, out var s)
        ? [s]
        : throw new ArgumentException(string.Concat("Unknown scenario: ", scenarioName));

var runTurbo = serverFilter is "both" or "turbo";
var runKestrel = serverFilter is "both" or "kestrel";

var comparisons = new List<(StressResult Turbo, StressResult Kestrel)>();

foreach (var scenario in toRun)
{
    var config = scenario.DefaultConfig;
    if (durationOverride.HasValue)
    {
        config = config with { Duration = TimeSpan.FromSeconds(durationOverride.Value) };
    }
    if (concurrencyOverride.HasValue)
    {
        config = config with { Concurrency = concurrencyOverride.Value };
    }

    Console.WriteLine(string.Concat("=== ", scenario.Name, " (", config.Concurrency.ToString(), " concurrent, ", ((int)config.Duration.TotalSeconds).ToString(), "s) ==="));

    StressResult? turboResult = null;
    StressResult? kestrelResult = null;

    if (runTurbo)
    {
        turboResult = await RunScenario(scenario, config, ServerType.Turbo);
    }

    if (runKestrel)
    {
        kestrelResult = await RunScenario(scenario, config, ServerType.Kestrel);
    }

    if (turboResult is not null && kestrelResult is not null)
    {
        StressReport.PrintScenario(turboResult, kestrelResult);
        comparisons.Add((turboResult, kestrelResult));
    }
    else if (turboResult is not null)
    {
        PrintSingleResult(turboResult);
    }
    else if (kestrelResult is not null)
    {
        PrintSingleResult(kestrelResult);
    }

    await JsonExporter.ExportAsync(scenario.Name, config, turboResult, kestrelResult);
}

if (comparisons.Count > 1)
{
    StressReport.PrintSummary(comparisons);
}

static async Task<StressResult> RunScenario(IStressScenario scenario, StressRunConfig config, ServerType serverType)
{
    Console.Write(string.Concat("  ", serverType.ToString(), ": starting server... "));

    await using var harness = new ServerHarness();
    await harness.StartAsync(serverType, scenario.ConfigureRoutes);

    Console.Write("warmup... ");
    var requestFunc = scenario.CreateRequestFunc();
    using var warmupCts = new CancellationTokenSource(config.WarmupDuration);
    try
    {
        await LoadGenerator.RunAsync(harness.BaseUri!, config with { Concurrency = Math.Min(config.Concurrency, 10) }, requestFunc, _ => { }, warmupCts.Token);
    }
    catch (OperationCanceledException)
    {
    }

    GC.Collect(2, GCCollectionMode.Forced, true);
    GC.WaitForPendingFinalizers();

    Console.Write("load... ");
    var collector = new MetricsCollector();
    using var loadCts = new CancellationTokenSource(config.Duration);
    try
    {
        await LoadGenerator.RunAsync(harness.BaseUri!, config, requestFunc, collector.Record, loadCts.Token);
    }
    catch (OperationCanceledException)
    {
    }

    var result = collector.Build(serverType, config);
    Console.WriteLine(string.Concat("done (", result.Summary.TotalRequests.ToString(), " requests, ", result.Summary.TotalErrors.ToString(), " errors)"));

    return result;
}

static void PrintSingleResult(StressResult result)
{
    Console.WriteLine();
    Console.WriteLine(string.Concat("## ", result.Config.ScenarioName, " — ", result.Server.ToString()));
    Console.WriteLine(string.Concat("  Throughput: ", result.Summary.AvgRps.ToString("F0"), " req/s"));
    Console.WriteLine(string.Concat("  Latency p99: ", result.Summary.P99Ms.ToString("F1"), " ms"));
    Console.WriteLine(string.Concat("  Peak Memory: ", (result.Summary.PeakMemoryBytes / (1024.0 * 1024.0)).ToString("F0"), " MB"));
    Console.WriteLine(string.Concat("  Errors: ", result.Summary.TotalErrors.ToString()));
    Console.WriteLine();
}
