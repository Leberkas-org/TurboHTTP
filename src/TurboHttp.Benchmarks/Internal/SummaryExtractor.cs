using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace TurboHttp.Benchmarks.Internal;

/// <summary>
/// Extracts <see cref="BenchmarkResult"/> instances directly from a BenchmarkDotNet
/// <see cref="Summary"/>, bypassing the CSV artifact round-trip.
/// </summary>
public static class SummaryExtractor
{
    /// <summary>
    /// Converts all completed reports in <paramref name="summary"/> to
    /// <see cref="BenchmarkResult"/> instances. Reports with missing statistics
    /// (cancelled or errored runs) are silently skipped.
    /// </summary>
    public static IReadOnlyList<BenchmarkResult> Extract(Summary summary)
    {
        var results = new List<BenchmarkResult>(summary.Reports.Length);

        foreach (var report in summary.Reports)
        {
            var statistics = report.ResultStatistics;
            if (statistics is null)
            {
                continue;
            }

            var name = BuildName(report.BenchmarkCase);
            var allocBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0L;

            results.Add(new BenchmarkResult(
                name,
                statistics.Mean,
                statistics.Percentiles.P50,
                statistics.Percentiles.P95,
                statistics.Percentiles.Percentile(99),
                allocBytes));
        }

        return results;
    }

    /// <summary>
    /// Builds a scenario name matching the format used by the CSV-based pipeline:
    /// <c>{Method} / CL={ConcurrencyLevel} / {PayloadType} / HTTP {HttpVersion}</c>.
    /// </summary>
    private static string BuildName(BenchmarkCase benchmarkCase)
    {
        var method = benchmarkCase.Descriptor.WorkloadMethod.Name;
        var concurrency = GetParam(benchmarkCase, "ConcurrencyLevel") ?? "1";
        var payload = GetParam(benchmarkCase, "PayloadType") ?? "light";
        var version = GetParam(benchmarkCase, "HttpVersion") ?? "1.1";

        return $"{method} / CL={concurrency} / {payload} / HTTP {version}";
    }

    private static string? GetParam(BenchmarkCase benchmarkCase, string paramName)
        => benchmarkCase.Parameters.Items
            .FirstOrDefault(p => p.Name == paramName)
            ?.Value?.ToString();
}

/// <summary>
/// Extension helpers for <see cref="Summary"/>.
/// </summary>
public static class SummaryExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="summary"/> contains at least
    /// one benchmark case belonging to <typeparamref name="T"/>.
    /// </summary>
    public static bool HasBenchmarksOf<T>(this Summary summary)
        => summary.BenchmarksCases.Any(c => c.Descriptor.Type == typeof(T));
}
