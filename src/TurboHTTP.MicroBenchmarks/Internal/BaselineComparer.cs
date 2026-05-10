using System.Text;
using System.Text.Json;

namespace TurboHTTP.MicroBenchmarks.Internal;

public sealed record BaselineEntry(
    string Method,
    double MedianNanoseconds,
    long AllocatedBytes);

public sealed record ComparisonResult(
    string Benchmark,
    string Group,
    double BaselineMedian,
    double CurrentMedian,
    double ThroughputDelta,
    long BaselineAlloc,
    long CurrentAlloc,
    double AllocDelta,
    bool IsNew,
    bool IsRegression);

public static class BaselineComparer
{
    private const double ThroughputRegressionThreshold = 0.10;
    private const double AllocationRegressionThreshold = 0.05;
    private const string NamespacePrefix = "TurboHTTP.MicroBenchmarks.";

    public static IReadOnlyList<BaselineEntry> LoadBaseline(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return [];
        }

        using var stream = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(stream);

        var entries = new List<BaselineEntry>();
        var benchmarks = doc.RootElement.GetProperty("Benchmarks");

        foreach (var bm in benchmarks.EnumerateArray())
        {
            var method = bm.GetProperty("FullName").GetString() ?? "";

            if (!bm.TryGetProperty("Statistics", out var stats)
                || stats.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var median = stats.GetProperty("Median").GetDouble();

            long allocated = 0;
            if (bm.TryGetProperty("Memory", out var mem)
                && mem.ValueKind != JsonValueKind.Null
                && mem.TryGetProperty("BytesAllocatedPerOperation", out var allocProp)
                && allocProp.ValueKind != JsonValueKind.Null)
            {
                allocated = allocProp.GetInt64();
            }

            entries.Add(new BaselineEntry(method, median, allocated));
        }

        return entries;
    }

    public static IReadOnlyList<ComparisonResult> Compare(
        IReadOnlyList<BaselineEntry> baseline,
        IReadOnlyList<BaselineEntry> current,
        string group)
    {
        var baselineMap = new Dictionary<string, BaselineEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in baseline)
        {
            baselineMap.TryAdd(entry.Method, entry);
        }

        var results = new List<ComparisonResult>();

        foreach (var entry in current)
        {
            var shortName = ShortenName(entry.Method);

            if (!baselineMap.TryGetValue(entry.Method, out var b))
            {
                results.Add(new ComparisonResult(
                    shortName, group,
                    0, entry.MedianNanoseconds, 0,
                    0, entry.AllocatedBytes, 0,
                    IsNew: true, IsRegression: false));
                continue;
            }

            var throughputDelta = b.MedianNanoseconds > 0
                ? (entry.MedianNanoseconds - b.MedianNanoseconds) / b.MedianNanoseconds
                : 0;

            var allocDelta = b.AllocatedBytes > 0
                ? (double)(entry.AllocatedBytes - b.AllocatedBytes) / b.AllocatedBytes
                : 0;

            var isRegression = throughputDelta > ThroughputRegressionThreshold
                               || allocDelta > AllocationRegressionThreshold;

            results.Add(new ComparisonResult(
                shortName, group,
                b.MedianNanoseconds, entry.MedianNanoseconds, throughputDelta,
                b.AllocatedBytes, entry.AllocatedBytes, allocDelta,
                IsNew: false, IsRegression: isRegression));
        }

        return results;
    }

    public static string FormatReport(IReadOnlyList<ComparisonResult> allResults)
    {
        if (allResults.Count == 0)
        {
            return "No benchmark results to compare.";
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("# Benchmark Regression Report");
        sb.AppendLine();

        var groups = allResults
            .GroupBy(r => r.Group)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            sb.AppendLine(string.Concat("## ", group.Key));
            sb.AppendLine();
            sb.AppendLine("| Benchmark | Median (ns) | Delta | Alloc (B) | Delta | Status |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");

            foreach (var r in group)
            {
                if (r.IsNew)
                {
                    sb.AppendLine(string.Concat(
                        "| ", r.Benchmark,
                        " | ", $"{r.CurrentMedian:N0}",
                        " | NEW",
                        " | ", $"{r.CurrentAlloc:N0}",
                        " | NEW",
                        " | new |"));
                    continue;
                }

                var status = r.IsRegression ? "REGRESSION" : "ok";
                sb.AppendLine(string.Concat(
                    "| ", r.Benchmark,
                    " | ", $"{r.CurrentMedian:N0}",
                    " | ", $"{r.ThroughputDelta:+0.0%;-0.0%;0.0%}",
                    " | ", $"{r.CurrentAlloc:N0}",
                    " | ", $"{r.AllocDelta:+0.0%;-0.0%;0.0%}",
                    " | ", status, " |"));
            }

            sb.AppendLine();
        }

        var regressions = allResults.Where(r => r.IsRegression).ToList();
        var newBenchmarks = allResults.Where(r => r.IsNew).ToList();

        sb.AppendLine("---");
        sb.AppendLine();

        if (regressions.Count > 0)
        {
            sb.AppendLine(string.Concat("## ** ", regressions.Count.ToString(),
                " Regression(s) Detected **"));
            sb.AppendLine();
            foreach (var r in regressions)
            {
                sb.AppendLine(string.Concat(
                    "- **", r.Benchmark, "** [", r.Group, "]",
                    " — throughput ", $"{r.ThroughputDelta:+0.0%;-0.0%}",
                    ", alloc ", $"{r.AllocDelta:+0.0%;-0.0%}"));
            }

            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## All benchmarks within thresholds");
            sb.AppendLine();
        }

        if (newBenchmarks.Count > 0)
        {
            sb.AppendLine(string.Concat(newBenchmarks.Count.ToString(),
                " new benchmark(s) without baseline — will be recorded as new baseline."));
            sb.AppendLine();
        }

        var total = allResults.Count;
        var ok = allResults.Count(r => !r.IsRegression && !r.IsNew);
        sb.AppendLine(string.Concat(
            "Summary: ", total.ToString(), " benchmarks — ",
            ok.ToString(), " ok, ",
            regressions.Count.ToString(), " regression(s), ",
            newBenchmarks.Count.ToString(), " new"));

        return sb.ToString();
    }

    public static void SaveBaseline(string sourcePath, string baselineDir, string targetName)
    {
        Directory.CreateDirectory(baselineDir);
        File.Copy(sourcePath, Path.Combine(baselineDir, targetName), overwrite: true);
    }

    private static string ShortenName(string fullName)
    {
        if (fullName.StartsWith(NamespacePrefix, StringComparison.Ordinal))
        {
            fullName = fullName[NamespacePrefix.Length..];
        }

        var dotIndex = fullName.IndexOf('.');
        if (dotIndex >= 0)
        {
            fullName = fullName[(dotIndex + 1)..];
        }

        return fullName;
    }
}
