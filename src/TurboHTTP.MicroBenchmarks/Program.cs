using BenchmarkDotNet.Running;
using TurboHTTP.MicroBenchmarks.Internal;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var baselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Baselines");
var updateBaseline = args.Contains("--update-baseline", StringComparer.OrdinalIgnoreCase);

foreach (var summary in summaries)
{
    var jsonExport = summary.ResultsDirectoryPath;
    var jsonFiles = Directory.Exists(jsonExport)
        ? Directory.GetFiles(jsonExport, "*-report-full.json")
        : [];

    foreach (var jsonFile in jsonFiles)
    {
        var baselinePath = Path.Combine(baselineDir, Path.GetFileName(jsonFile));
        var baseline = BaselineComparer.LoadBaseline(baselinePath);
        var current = BaselineComparer.LoadBaseline(jsonFile);

        var report = BaselineComparer.Compare(baseline, current);
        Console.WriteLine(report);

        if (updateBaseline || baseline.Count == 0)
        {
            BaselineComparer.SaveBaseline(jsonFile, baselineDir);
            Console.WriteLine($"Baseline updated: {baselinePath}");
        }
    }
}
