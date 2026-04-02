using System.Text;

namespace TurboHttp.Benchmarks.Internal;

/// <summary>
/// Represents the measured result of a single benchmark scenario.
/// </summary>
/// <param name="BenchmarkName">Scenario identifier, e.g. "SingleRequest_Light / HTTP 1.1".</param>
/// <param name="MeanNanoseconds">Mean elapsed time per operation in nanoseconds.</param>
/// <param name="P50Nanoseconds">Median (50th-percentile) latency in nanoseconds.</param>
/// <param name="P95Nanoseconds">95th-percentile latency in nanoseconds.</param>
/// <param name="P99Nanoseconds">99th-percentile latency in nanoseconds.</param>
/// <param name="AllocatedBytes">Managed memory allocated per operation in bytes.</param>
public sealed record BenchmarkResult(
    string BenchmarkName,
    double MeanNanoseconds,
    double P50Nanoseconds,
    double P95Nanoseconds,
    double P99Nanoseconds,
    long AllocatedBytes);

/// <summary>
/// Generates human-readable markdown reports comparing TurboHttp against standard
/// <see cref="System.Net.Http.HttpClient"/> benchmark results.
/// </summary>
public static class BenchmarkComparisonReport
{
    private const double DeltaThresholdPercent = 5.0;

    /// <summary>
    /// Generates a markdown report string comparing <paramref name="httpClientResults"/>
    /// against <paramref name="turboHttpResults"/>, with an optional concurrent-benchmark section.
    /// </summary>
    /// <param name="httpClientResults">Baseline HttpClient single-request benchmark results.</param>
    /// <param name="turboHttpResults">TurboHttp single-request benchmark results.</param>
    /// <param name="httpClientConcurrentResults">Baseline HttpClient concurrent benchmark results.</param>
    /// <param name="turboHttpConcurrentResults">TurboHttp concurrent benchmark results.</param>
    /// <returns>A markdown-formatted comparison report.</returns>
    public static string GenerateReport(
        IReadOnlyList<BenchmarkResult> httpClientResults,
        IReadOnlyList<BenchmarkResult> turboHttpResults,
        IReadOnlyList<BenchmarkResult> httpClientConcurrentResults,
        IReadOnlyList<BenchmarkResult> turboHttpConcurrentResults)
    {
        var sb = new StringBuilder();
        var now = DateTime.UtcNow;

        AppendHeader(sb, now);
        AppendThroughputTable(sb, httpClientResults, turboHttpResults);
        AppendLatencyTable(sb, httpClientResults, turboHttpResults);
        AppendMemoryTable(sb, httpClientResults, turboHttpResults);
        AppendConcurrentSections(sb, httpClientConcurrentResults, turboHttpConcurrentResults);
        AppendNotes(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Writes a markdown report to <c>benchmarks/comparison_report_{timestamp}.md</c>
    /// relative to the current working directory, creating the directory if needed.
    /// </summary>
    /// <param name="markdown">The markdown content returned by <see cref="GenerateReport"/>.</param>
    /// <returns>The path of the written file.</returns>
    public static string WriteReportToFile(string markdown)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "benchmarks");
        Directory.CreateDirectory(outputDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(outputDir, $"comparison_report_{timestamp}.md");

        File.WriteAllText(filePath, markdown, Encoding.UTF8);
        return filePath;
    }

    // Private helpers

    private static void AppendHeader(StringBuilder sb, DateTime reportDate)
    {
        sb.AppendLine("# TurboHttp vs HttpClient — Performance Comparison");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| **Report date** | {reportDate:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine("| **Server** | Kestrel on 127.0.0.1 (loopback) |");
        sb.AppendLine("| **Protocol** | HTTP/1.1 and HTTP/2 (h2c) |");
        sb.AppendLine("| **Warmup** | 3 iterations · 5 target · 32 invocations |");
        sb.AppendLine();
        sb.AppendLine("> **Legend:** ✓ TurboHttp faster by >5%   –  within ±5%   ✗ TurboHttp slower by >5%");
        sb.AppendLine();
    }

    private static void AppendThroughputTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("## Throughput (Req/sec — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");

        foreach (var row in MatchRows(httpResults, turboResults))
        {
            var httpRps = NsToRps(row.Http.MeanNanoseconds);
            var turboRps = NsToRps(row.Turbo.MeanNanoseconds);

            // Higher req/sec is better for TurboHttp
            var delta = ComputeDelta(httpRps, turboRps);
            var indicator = ThroughputIndicator(delta);

            sb.AppendLine(
                $"| {row.Name} | {httpRps:N0} | {turboRps:N0} | {delta:+0.0;-0.0;0.0}% | {indicator} |");
        }

        sb.AppendLine();
    }

    private static void AppendLatencyTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("## Latency (ns — lower is better)");
        sb.AppendLine();

        // p50
        sb.AppendLine("### p50 (Median)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P50Nanoseconds);
        sb.AppendLine();

        // p95
        sb.AppendLine("### p95");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P95Nanoseconds);
        sb.AppendLine();

        // p99
        sb.AppendLine("### p99");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P99Nanoseconds);
        sb.AppendLine();
    }

    private static void AppendLatencyRows(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults,
        Func<BenchmarkResult, double> selector)
    {
        foreach (var row in MatchRows(httpResults, turboResults))
        {
            var httpNs = selector(row.Http);
            var turboNs = selector(row.Turbo);

            // Lower latency is better for TurboHttp → positive delta means TurboHttp is better
            var delta = ComputeLatencyDelta(httpNs, turboNs);
            var indicator = ThroughputIndicator(delta);

            sb.AppendLine(
                $"| {row.Name} | {httpNs:N0} ns | {turboNs:N0} ns | {delta:+0.0;-0.0;0.0}% | {indicator} |");
        }
    }

    private static void AppendMemoryTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("## Memory (Allocated bytes/op — lower is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");

        foreach (var row in MatchRows(httpResults, turboResults))
        {
            double httpBytes = row.Http.AllocatedBytes;
            double turboBytes = row.Turbo.AllocatedBytes;

            // Lower allocation is better for TurboHttp → positive delta means TurboHttp is better
            var delta = ComputeLatencyDelta(httpBytes, turboBytes);
            var indicator = ThroughputIndicator(delta);

            sb.AppendLine(
                $"| {row.Name} | {row.Http.AllocatedBytes:N0} B | {row.Turbo.AllocatedBytes:N0} B | {delta:+0.0;-0.0;0.0}% | {indicator} |");
        }

        sb.AppendLine();
    }

    private static void AppendConcurrentSections(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Concurrent Benchmarks");
        sb.AppendLine();
        sb.AppendLine("> N requests are fired simultaneously via `Task.WhenAll`.");
        sb.AppendLine("> **Throughput** = N / Mean (total req/sec across all parallel slots).");
        sb.AppendLine("> **Latency** = elapsed wall-time until all N complete (lower is better).");
        sb.AppendLine();

        AppendConcurrentThroughputTable(sb, httpResults, turboResults);
        AppendConcurrentLatencyTable(sb, httpResults, turboResults);
        AppendConcurrentMemoryTable(sb, httpResults, turboResults);
    }

    private static void AppendConcurrentThroughputTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("### Concurrent Throughput (Req/sec — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");

        foreach (var row in MatchRows(httpResults, turboResults))
        {
            var cl = ParseConcurrencyLevel(row.Name);
            var httpRps = ConcurrentNsToRps(row.Http.MeanNanoseconds, cl);
            var turboRps = ConcurrentNsToRps(row.Turbo.MeanNanoseconds, cl);

            var delta = ComputeDelta(httpRps, turboRps);
            var indicator = ThroughputIndicator(delta);

            sb.AppendLine(
                $"| {row.Name} | {httpRps:N0} | {turboRps:N0} | {delta:+0.0;-0.0;0.0}% | {indicator} |");
        }

        sb.AppendLine();
    }

    private static void AppendConcurrentLatencyTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("### Concurrent Latency (ns — lower is better)");
        sb.AppendLine();

        sb.AppendLine("#### p50 (Median)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P50Nanoseconds);
        sb.AppendLine();

        sb.AppendLine("#### p95");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P95Nanoseconds);
        sb.AppendLine();

        sb.AppendLine("#### p99");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");
        AppendLatencyRows(sb, httpResults, turboResults, r => r.P99Nanoseconds);
        sb.AppendLine();
    }

    private static void AppendConcurrentMemoryTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        sb.AppendLine("### Concurrent Memory (Allocated bytes/op — lower is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | TurboHttp | Delta% | |");
        sb.AppendLine("|---|---:|---:|---:|:---:|");

        foreach (var row in MatchRows(httpResults, turboResults))
        {
            double httpBytes = row.Http.AllocatedBytes;
            double turboBytes = row.Turbo.AllocatedBytes;

            var delta = ComputeLatencyDelta(httpBytes, turboBytes);
            var indicator = ThroughputIndicator(delta);

            sb.AppendLine(
                $"| {row.Name} | {row.Http.AllocatedBytes:N0} B | {row.Turbo.AllocatedBytes:N0} B | {delta:+0.0;-0.0;0.0}% | {indicator} |");
        }

        sb.AppendLine();
    }

    private static void AppendNotes(StringBuilder sb)
    {
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- All benchmarks run on loopback (127.0.0.1); real-network latency will differ.");
        sb.AppendLine("- HTTP/2 cleartext (h2c) requires `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport=true`.");
        sb.AppendLine("- Memory figures reflect managed allocations only; native/pooled buffers are not included.");
        sb.AppendLine("- Variance anomalies (high p99 vs p50 spread) may indicate GC pauses — re-run to confirm.");
        sb.AppendLine();
    }

    // Maths

    /// <summary>Converts nanoseconds-per-operation to requests per second.</summary>
    public static double NsToRps(double meanNanoseconds)
    {
        if (meanNanoseconds <= 0)
        {
            return 0;
        }

        return 1_000_000_000.0 / meanNanoseconds;
    }

    /// <summary>
    /// Computes the signed percentage improvement when <paramref name="turboValue"/> is higher
    /// than <paramref name="baselineValue"/> (throughput: higher is better).
    /// </summary>
    public static double ComputeDelta(double baselineValue, double turboValue)
    {
        if (baselineValue == 0)
        {
            return 0;
        }

        return (turboValue - baselineValue) / baselineValue * 100.0;
    }

    /// <summary>
    /// Computes the signed percentage improvement when <paramref name="turboValue"/> is lower
    /// than <paramref name="baselineValue"/> (latency/memory: lower is better).
    /// </summary>
    public static double ComputeLatencyDelta(double baselineValue, double turboValue)
    {
        if (baselineValue == 0)
        {
            return 0;
        }

        return (baselineValue - turboValue) / baselineValue * 100.0;
    }

    /// <summary>Returns the visual indicator for a performance delta.</summary>
    public static string ThroughputIndicator(double deltaPercent)
    {
        return deltaPercent switch
        {
            > DeltaThresholdPercent => "✓",
            < -DeltaThresholdPercent => "✗",
            _ => "–"
        };
    }

    /// <summary>
    /// Converts nanoseconds-per-batch to requests per second, scaling by
    /// <paramref name="concurrencyLevel"/> because each batch completes N requests.
    /// </summary>
    public static double ConcurrentNsToRps(double meanNanoseconds, int concurrencyLevel)
    {
        if (meanNanoseconds <= 0)
        {
            return 0;
        }

        return concurrencyLevel * 1_000_000_000.0 / meanNanoseconds;
    }

    /// <summary>
    /// Parses the concurrency level from a scenario name built by
    /// <see cref="SummaryExtractor"/> (e.g. <c>"ConcurrentRequests_Light / CL=16 / …"</c>).
    /// Returns 1 when no <c>CL=</c> token is found.
    /// </summary>
    public static int ParseConcurrencyLevel(string name)
    {
        var clIdx = name.IndexOf("CL=", StringComparison.Ordinal);
        if (clIdx < 0)
        {
            return 1;
        }

        var start = clIdx + 3;
        var spaceIdx = name.IndexOf(' ', start);
        var slice = spaceIdx < 0 ? name.AsSpan(start) : name.AsSpan(start, spaceIdx - start);
        return int.TryParse(slice, out var cl) ? cl : 1;
    }

    // Row matching

    private static IReadOnlyList<(string Name, BenchmarkResult Http, BenchmarkResult Turbo)> MatchRows(
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> turboResults)
    {
        var httpMap = httpResults.ToDictionary(r => r.BenchmarkName, StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, BenchmarkResult, BenchmarkResult)>();

        foreach (var turbo in turboResults)
        {
            if (httpMap.TryGetValue(turbo.BenchmarkName, out var http))
            {
                result.Add((turbo.BenchmarkName, http, turbo));
            }
        }

        // Include http-only rows (no matching turbo) with zero turbo values
        var matchedNames = new HashSet<string>(result.Select(r => r.Item1), StringComparer.OrdinalIgnoreCase);
        foreach (var http in httpResults)
        {
            if (!matchedNames.Contains(http.BenchmarkName))
            {
                var zero = new BenchmarkResult(http.BenchmarkName, 0, 0, 0, 0, 0);
                result.Add((http.BenchmarkName, http, zero));
            }
        }

        return result;
    }
}
