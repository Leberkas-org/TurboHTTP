using BenchmarkDotNet.Running;
using TurboHTTP.MicroBenchmarks.Internal;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var baselineDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Baselines");
var updateBaseline = args.Contains("--update-baseline", StringComparer.OrdinalIgnoreCase);

var allResults = new List<ComparisonResult>();
var newBaselines = new List<string>();

foreach (var summary in summaries)
{
    var jsonExport = summary.ResultsDirectoryPath;
    var jsonFiles = Directory.Exists(jsonExport)
        ? Directory.GetFiles(jsonExport, "*-report-full.json")
        : [];

    foreach (var jsonFile in jsonFiles)
    {
        var baselineName = ToBaselineName(Path.GetFileNameWithoutExtension(jsonFile));
        var baselinePath = Path.Combine(baselineDir, baselineName);
        var baseline = BaselineComparer.LoadBaseline(baselinePath);
        var current = BaselineComparer.LoadBaseline(jsonFile);

        var group = ToGroupName(baselineName);
        var results = BaselineComparer.Compare(baseline, current, group);
        allResults.AddRange(results);

        if (updateBaseline || baseline.Count == 0)
        {
            BaselineComparer.SaveBaseline(jsonFile, baselineDir, baselineName);
            newBaselines.Add(baselineName);
        }
    }
}

Console.WriteLine(BaselineComparer.FormatReport(allResults));

if (newBaselines.Count > 0)
{
    Console.WriteLine("Baselines written:");
    foreach (var name in newBaselines)
    {
        Console.WriteLine(string.Concat("  ", name));
    }
}

static string ToBaselineName(string exportName)
{
    var name = exportName.Replace("-report-full", "");
    var lastDot = name.LastIndexOf('.');
    if (lastDot >= 0)
    {
        name = name[(lastDot + 1)..];
    }

    return name + ".json";
}

static string ToGroupName(string baselineName)
{
    var name = Path.GetFileNameWithoutExtension(baselineName);

    if (name.StartsWith("Hpack", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Huffman", StringComparison.OrdinalIgnoreCase))
    {
        return "HPACK";
    }

    if (name.StartsWith("Http10", StringComparison.OrdinalIgnoreCase))
    {
        return "HTTP/1.0";
    }

    if (name.StartsWith("Http11", StringComparison.OrdinalIgnoreCase))
    {
        return "HTTP/1.1";
    }

    if (name.StartsWith("Http2", StringComparison.OrdinalIgnoreCase))
    {
        return "HTTP/2";
    }

    if (name.StartsWith("Engine", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Feedback", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
    {
        return "Pipeline";
    }

    if (name.StartsWith("Connection", StringComparison.OrdinalIgnoreCase))
    {
        return "Transport";
    }

    return "Other";
}
