using System.Text;
using System.Text.Json;

namespace TurboHTTP.MicroBenchmarks.Internal;

public sealed record BaselineEntry(
    string Method,
    double MedianNanoseconds,
    long AllocatedBytes);

public static class BaselineComparer
{
    private const double ThroughputRegressionThreshold = 0.10;
    private const double AllocationRegressionThreshold = 0.05;

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

    public static string Compare(
        IReadOnlyList<BaselineEntry> baseline,
        IReadOnlyList<BaselineEntry> current)
    {
        if (baseline.Count == 0)
        {
            return "No baseline found — current results will become the new baseline.";
        }

        var baselineMap = new Dictionary<string, BaselineEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in baseline)
        {
            baselineMap.TryAdd(entry.Method, entry);
        }
        var sb = new StringBuilder();
        sb.AppendLine("# Performance Regression Report");
        sb.AppendLine();
        sb.AppendLine("| Benchmark | Baseline Median (ns) | Current Median (ns) | Δ% | Baseline Alloc (B) | Current Alloc (B) | Δ% | Status |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");

        var regressions = 0;

        foreach (var entry in current)
        {
            if (!baselineMap.TryGetValue(entry.Method, out var b))
            {
                sb.AppendLine($"| {entry.Method} | — | {entry.MedianNanoseconds:N0} | NEW | — | {entry.AllocatedBytes:N0} | NEW | ℹ️ |");
                continue;
            }

            var throughputDelta = b.MedianNanoseconds > 0
                ? (entry.MedianNanoseconds - b.MedianNanoseconds) / b.MedianNanoseconds
                : 0;

            var allocDelta = b.AllocatedBytes > 0
                ? (double)(entry.AllocatedBytes - b.AllocatedBytes) / b.AllocatedBytes
                : 0;

            var throughputRegression = throughputDelta > ThroughputRegressionThreshold;
            var allocRegression = allocDelta > AllocationRegressionThreshold;
            var status = throughputRegression || allocRegression ? "REGRESSION" : "OK";

            if (throughputRegression || allocRegression)
            {
                regressions++;
            }

            sb.AppendLine(string.Concat(
                $"| {entry.Method} ",
                $"| {b.MedianNanoseconds:N0} ",
                $"| {entry.MedianNanoseconds:N0} ",
                $"| {throughputDelta:+0.0%;-0.0%;0.0%} ",
                $"| {b.AllocatedBytes:N0} ",
                $"| {entry.AllocatedBytes:N0} ",
                $"| {allocDelta:+0.0%;-0.0%;0.0%} ",
                $"| {status} |"));
        }

        sb.AppendLine();
        sb.AppendLine(regressions > 0
            ? $"**{regressions} regression(s) detected.**"
            : "**No regressions detected.**");

        return sb.ToString();
    }

    public static void SaveBaseline(string sourcePath, string baselineDir)
    {
        Directory.CreateDirectory(baselineDir);
        var fileName = Path.GetFileName(sourcePath);
        File.Copy(sourcePath, Path.Combine(baselineDir, fileName), overwrite: true);
    }
}
