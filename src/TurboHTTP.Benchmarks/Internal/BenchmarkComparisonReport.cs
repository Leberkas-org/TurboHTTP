using System.Text;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Represents the measured result of a single benchmark scenario.
/// </summary>
/// <param name="BenchmarkName">Scenario identifier, e.g. "ConcurrentRequests_Light / CL=1 / HTTP 1.1".</param>
/// <param name="MeanNanoseconds">Mean elapsed time per operation in nanoseconds.</param>
/// <param name="P50Nanoseconds">Median (50th-percentile) latency in nanoseconds.</param>
/// <param name="P95Nanoseconds">95th-percentile latency in nanoseconds.</param>
/// <param name="P99Nanoseconds">99th-percentile latency in nanoseconds.</param>
/// <param name="AllocatedBytes">Managed memory allocated per single operation in bytes.</param>
public sealed record BenchmarkResult(
    string BenchmarkName,
    double MeanNanoseconds,
    double P50Nanoseconds,
    double P95Nanoseconds,
    double P99Nanoseconds,
    long AllocatedBytes);

/// <summary>
/// Generates human-readable markdown reports comparing TurboHttp (SendAsync and Streaming)
/// against standard <see cref="System.Net.Http.HttpClient"/> benchmark results.
/// Results are split into single-request (CL=1) and concurrent (CL&gt;1) sections automatically.
/// </summary>
public static class BenchmarkComparisonReport
{
    /// <summary>
    /// Generates a three-way markdown report comparing HttpClient, TurboHttp SendAsync, and
    /// TurboHttp Streaming. Results with CL=1 are shown as single-request benchmarks;
    /// results with CL&gt;1 are shown as concurrent benchmarks.
    /// </summary>
    public static string GenerateReport(
        IReadOnlyList<BenchmarkResult> httpClientResults,
        IReadOnlyList<BenchmarkResult> turboSendAsyncResults,
        IReadOnlyList<BenchmarkResult> turboStreamingResults)
    {
        var sb = new StringBuilder();
        AppendBinkrakenHeader(sb, DateTime.UtcNow);
        AppendVersionSections(sb, httpClientResults, turboSendAsyncResults, turboStreamingResults);
        AppendBinkrakenNotes(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Generates a three-way markdown report for the localhost Kestrel test server.
    /// </summary>
    public static string GenerateKestrelReport(
        IReadOnlyList<BenchmarkResult> httpClientResults,
        IReadOnlyList<BenchmarkResult> turboSendAsyncResults,
        IReadOnlyList<BenchmarkResult> turboStreamingResults)
    {
        var sb = new StringBuilder();
        AppendKestrelHeader(sb, DateTime.UtcNow);
        AppendVersionSections(sb, httpClientResults, turboSendAsyncResults, turboStreamingResults);
        AppendKestrelNotes(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Writes a markdown report to <c>benchmarks/comparison_report_{timestamp}.md</c>
    /// relative to the current working directory, creating the directory if needed.
    /// </summary>
    public static string WriteReportToFile(string markdown)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "benchmarks");
        Directory.CreateDirectory(outputDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(outputDir, $"comparison_report_{timestamp}.md");

        File.WriteAllText(filePath, markdown, Encoding.UTF8);
        return filePath;
    }

    private static void AppendVersionSections(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendResults,
        IReadOnlyList<BenchmarkResult> streamResults)
    {
        foreach (var version in new[] { "1.1", "2.0", "3.0" })
        {
            var httpAll = FilterByVersion(httpResults, version);
            var sendAll = FilterByVersion(sendResults, version);
            var streamAll = FilterByVersion(streamResults, version);

            if (httpAll.Count == 0 && sendAll.Count == 0 && streamAll.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"# HTTP/{version}");
            sb.AppendLine();

            var httpSingle = FilterByConcurrency(httpAll, cl: 1);
            var sendSingle = FilterByConcurrency(sendAll, cl: 1);
            var streamSingle = FilterByConcurrency(streamAll, cl: 1);

            if (httpSingle.Count > 0 || sendSingle.Count > 0 || streamSingle.Count > 0)
            {
                AppendThroughputTable(sb, httpSingle, sendSingle, streamSingle);
                AppendLatencyTable(sb, httpSingle, sendSingle, streamSingle);
                AppendMemoryTable(sb, httpSingle, sendSingle, streamSingle);
            }

            var httpConc = FilterByConcurrencyMin(httpAll, 2);
            var sendConc = FilterByConcurrencyMin(sendAll, 2);
            var streamConc = FilterByConcurrencyMin(streamAll, 2);

            if (httpConc.Count > 0 || sendConc.Count > 0 || streamConc.Count > 0)
            {
                AppendConcurrentSections(sb, httpConc, sendConc, streamConc);
            }
        }
    }

    private static void AppendBinkrakenHeader(StringBuilder sb, DateTime reportDate)
    {
        sb.AppendLine("# TurboHttp vs HttpClient — Binkraken.com (Remote HTTPS)");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| **Report date** | {reportDate:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine("| **Server** | binkraken.com (GitHub Pages CDN) |");
        sb.AppendLine("| **Protocol** | HTTPS — HTTP/1.1, HTTP/2 (ALPN), HTTP/3 (QUIC) |");
        sb.AppendLine("| **Light endpoint** | `GET /` (~3 KB HTML) |");
        sb.AppendLine("| **Heavy endpoint** | `GET /assets/…plugin-vue_export-helper….js` (~159 KB) |");
        sb.AppendLine();
        sb.AppendLine("> **Legend:**");
        sb.AppendLine("> - ✓  faster than HttpClient by >5%");
        sb.AppendLine("> - –  within ±5% of HttpClient");
        sb.AppendLine("> - ✗  slower than HttpClient by >5%");
        sb.AppendLine("> - **Δ%** is relative to the HttpClient baseline (positive = faster/cheaper)");
        sb.AppendLine();
    }

    private static void AppendBinkrakenNotes(StringBuilder sb)
    {
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- All requests target binkraken.com over real internet (HTTPS/TLS).");
        sb.AppendLine("- Results include DNS resolution, TLS handshake (first request), and network latency.");
        sb.AppendLine("- Light: `GET /` returns the SPA index (~3 KB). Heavy: `GET /assets/…` returns a JS bundle (~159 KB).");
        sb.AppendLine("- HTTP/2 is negotiated via ALPN over TLS — no cleartext h2c. HTTP/3 uses QUIC when server supports Alt-Svc.");
        sb.AppendLine("- Variance may be higher than loopback benchmarks due to network jitter and CDN caching.");
        sb.AppendLine("- Memory figures reflect managed allocations only; native/pooled buffers are not included.");
        sb.AppendLine("- **Streaming** uses the channel API (`Requests` writer / `Responses` reader).");
        sb.AppendLine("- **SendAsync** uses `Task.WhenAll` fan-out; each concurrent slot gets its own `Task<HttpResponseMessage>`.");
        sb.AppendLine();
    }

    private static void AppendKestrelHeader(StringBuilder sb, DateTime reportDate)
    {
        sb.AppendLine("# TurboHttp vs HttpClient — Localhost Kestrel (Loopback)");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| **Report date** | {reportDate:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine("| **Server** | localhost Kestrel (127.0.0.1, dynamic port) |");
        sb.AppendLine("| **Protocol** | HTTP/1.1 cleartext, HTTP/2 (h2c prior knowledge), HTTP/3 (QUIC+TLS) |");
        sb.AppendLine("| **Light endpoint** | `GET /benchmark/simple` (~3 B text/plain) |");
        sb.AppendLine("| **Heavy endpoint** | `POST /benchmark/payload` (10 KB request body) |");
        sb.AppendLine();
        sb.AppendLine("> **Legend:**");
        sb.AppendLine("> - ✓  faster than HttpClient by >5%");
        sb.AppendLine("> - –  within ±5% of HttpClient");
        sb.AppendLine("> - ✗  slower than HttpClient by >5%");
        sb.AppendLine("> - **Δ%** is relative to the HttpClient baseline (positive = faster/cheaper)");
        sb.AppendLine();
    }

    private static void AppendKestrelNotes(StringBuilder sb)
    {
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- All requests target a localhost Kestrel server over loopback (127.0.0.1).");
        sb.AppendLine("- HTTP/1.1 and HTTP/2 use cleartext (no TLS overhead). HTTP/3 requires TLS (QUIC mandates TLS 1.3).");
        sb.AppendLine("- Light: `GET /benchmark/simple` returns `OK\\n` (~3 B). Heavy: `POST /benchmark/payload` with 10 KB body.");
        sb.AppendLine("- HTTP/2 uses h2c prior knowledge on a dedicated listener port. HTTP/3 uses QUIC+TLS with a self-signed certificate.");
        sb.AppendLine("- Loopback eliminates network jitter — results reflect pure client+server overhead.");
        sb.AppendLine("- Memory figures reflect managed allocations only; native/pooled buffers are not included.");
        sb.AppendLine("- **Streaming** uses the channel API (`Requests` writer / `Responses` reader).");
        sb.AppendLine("- **SendAsync** uses `Task.WhenAll` fan-out; each concurrent slot gets its own `Task<HttpResponseMessage>`.");
        sb.AppendLine();
    }

    private static void AppendThroughputTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("## Single Request — Throughput (Req/sec — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var row in MatchRows3Way(httpResults, sendAsyncResults, streamingResults))
        {
            var httpRps = NsToRps(row.Http.MeanNanoseconds);
            var sendRps = NsToRps(row.SendAsync.MeanNanoseconds);
            var streamRps = NsToRps(row.Streaming.MeanNanoseconds);

            var sendDelta = ComputeDelta(httpRps, sendRps);
            var streamDelta = ComputeDelta(httpRps, streamRps);

            sb.AppendLine(
                $"| {row.Name} | {httpRps:N0} | {sendRps:N0} | {sendDelta:+0.0;-0.0;0.0}% | {streamRps:N0} | {streamDelta:+0.0;-0.0;0.0}% |");
        }

        sb.AppendLine();
    }

    private static void AppendLatencyTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("## Single Request — Latency (ns — lower is better)");
        sb.AppendLine();

        sb.AppendLine("### p50 (Median)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P50Nanoseconds, "ns");
        sb.AppendLine();

        sb.AppendLine("### p95");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P95Nanoseconds, "ns");
        sb.AppendLine();

        sb.AppendLine("### p99");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P99Nanoseconds, "ns");
        sb.AppendLine();
    }

    private static void AppendLatencyRows3Way(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults,
        Func<BenchmarkResult, double> selector,
        string unit)
    {
        foreach (var row in MatchRows3Way(httpResults, sendAsyncResults, streamingResults))
        {
            var httpVal = selector(row.Http);
            var sendVal = selector(row.SendAsync);
            var streamVal = selector(row.Streaming);

            var sendDelta = ComputeLatencyDelta(httpVal, sendVal);
            var streamDelta = ComputeLatencyDelta(httpVal, streamVal);

            sb.AppendLine(
                $"| {row.Name} | {httpVal:N0} {unit} | {sendVal:N0} {unit} | {sendDelta:+0.0;-0.0;0.0}% | {streamVal:N0} {unit} | {streamDelta:+0.0;-0.0;0.0}% |");
        }
    }

    private static void AppendMemoryTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("## Single Request — Memory (Allocated bytes/op — lower is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var row in MatchRows3Way(httpResults, sendAsyncResults, streamingResults))
        {
            double httpBytes = row.Http.AllocatedBytes;
            double sendBytes = row.SendAsync.AllocatedBytes;
            double streamBytes = row.Streaming.AllocatedBytes;

            var sendDelta = ComputeLatencyDelta(httpBytes, sendBytes);
            var streamDelta = ComputeLatencyDelta(httpBytes, streamBytes);

            sb.AppendLine(
                $"| {row.Name} | {row.Http.AllocatedBytes:N0} B | {row.SendAsync.AllocatedBytes:N0} B | {sendDelta:+0.0;-0.0;0.0}% | {row.Streaming.AllocatedBytes:N0} B | {streamDelta:+0.0;-0.0;0.0}% |");
        }

        sb.AppendLine();
    }

    private static void AppendConcurrentSections(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Concurrent Benchmarks");
        sb.AppendLine();
        sb.AppendLine("> N requests are fired simultaneously (SendAsync: `Task.WhenAll`; Streaming: channel write-all, drain-all).");
        sb.AppendLine("> **Throughput** = N / Mean (aggregate req/sec across all parallel slots).");
        sb.AppendLine("> **Latency** = elapsed wall-time until all N complete (lower is better).");
        sb.AppendLine();

        AppendConcurrentThroughputTable(sb, httpResults, sendAsyncResults, streamingResults);
        AppendConcurrentLatencyTable(sb, httpResults, sendAsyncResults, streamingResults);
        AppendConcurrentMemoryTable(sb, httpResults, sendAsyncResults, streamingResults);
    }

    private static void AppendConcurrentThroughputTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("### Concurrent Throughput (Req/sec — higher is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var row in MatchRows3Way(httpResults, sendAsyncResults, streamingResults))
        {
            var cl = ParseConcurrencyLevel(row.Name);
            var httpRps = ConcurrentNsToRps(row.Http.MeanNanoseconds, cl);
            var sendRps = ConcurrentNsToRps(row.SendAsync.MeanNanoseconds, cl);
            var streamRps = ConcurrentNsToRps(row.Streaming.MeanNanoseconds, cl);

            var sendDelta = ComputeDelta(httpRps, sendRps);
            var streamDelta = ComputeDelta(httpRps, streamRps);

            sb.AppendLine(
                $"| {row.Name} | {httpRps:N0} | {sendRps:N0} | {sendDelta:+0.0;-0.0;0.0}% | {streamRps:N0} | {streamDelta:+0.0;-0.0;0.0}% |");
        }

        sb.AppendLine();
    }

    private static void AppendConcurrentLatencyTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("### Concurrent Latency (ns — lower is better)");
        sb.AppendLine();

        sb.AppendLine("#### p50 (Median)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P50Nanoseconds, "ns");
        sb.AppendLine();

        sb.AppendLine("#### p95");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P95Nanoseconds, "ns");
        sb.AppendLine();

        sb.AppendLine("#### p99");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendLatencyRows3Way(sb, httpResults, sendAsyncResults, streamingResults, r => r.P99Nanoseconds, "ns");
        sb.AppendLine();
    }

    private static void AppendConcurrentMemoryTable(
        StringBuilder sb,
        IReadOnlyList<BenchmarkResult> httpResults,
        IReadOnlyList<BenchmarkResult> sendAsyncResults,
        IReadOnlyList<BenchmarkResult> streamingResults)
    {
        sb.AppendLine("### Concurrent Memory (Allocated bytes/op — lower is better)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | HttpClient | SendAsync | Δ% | Streaming | Δ% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var row in MatchRows3Way(httpResults, sendAsyncResults, streamingResults))
        {
            double httpBytes = row.Http.AllocatedBytes;
            double sendBytes = row.SendAsync.AllocatedBytes;
            double streamBytes = row.Streaming.AllocatedBytes;

            var sendDelta = ComputeLatencyDelta(httpBytes, sendBytes);
            var streamDelta = ComputeLatencyDelta(httpBytes, streamBytes);

            sb.AppendLine(
                $"| {row.Name} | {row.Http.AllocatedBytes:N0} B | {row.SendAsync.AllocatedBytes:N0} B | {sendDelta:+0.0;-0.0;0.0}% | {row.Streaming.AllocatedBytes:N0} B | {streamDelta:+0.0;-0.0;0.0}% |");
        }

        sb.AppendLine();
    }

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
    /// Positive result = turbo faster.
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
    /// Positive result = turbo cheaper/faster.
    /// </summary>
    public static double ComputeLatencyDelta(double baselineValue, double turboValue)
    {
        if (baselineValue == 0)
        {
            return 0;
        }

        return (baselineValue - turboValue) / baselineValue * 100.0;
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

    /// <summary>
    /// Returns only results whose <see cref="BenchmarkResult.BenchmarkName"/> ends with
    /// <c>/ HTTP {version}</c> (e.g. <c>"1.1"</c> or <c>"2.0"</c>).
    /// </summary>
    private static IReadOnlyList<BenchmarkResult> FilterByVersion(
        IReadOnlyList<BenchmarkResult> results, string version)
    {
        var suffix = $"/ HTTP {version}";
        return results
            .Where(r => r.BenchmarkName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Returns only results with the exact specified concurrency level.
    /// </summary>
    private static IReadOnlyList<BenchmarkResult> FilterByConcurrency(
        IReadOnlyList<BenchmarkResult> results, int cl)
    {
        return results
            .Where(r => ParseConcurrencyLevel(r.BenchmarkName) == cl)
            .ToList();
    }

    /// <summary>
    /// Returns only results with concurrency level &gt;= <paramref name="minCl"/>.
    /// </summary>
    private static IReadOnlyList<BenchmarkResult> FilterByConcurrencyMin(
        IReadOnlyList<BenchmarkResult> results, int minCl)
    {
        return results
            .Where(r => ParseConcurrencyLevel(r.BenchmarkName) >= minCl)
            .ToList();
    }

    /// <summary>
    /// Strips the trailing <c> / HTTP x.x</c> segment from a scenario name so that
    /// the version is not repeated when results are already grouped by version.
    /// </summary>
    private static string StripVersionSuffix(string name)
    {
        var idx = name.LastIndexOf(" / HTTP ", StringComparison.Ordinal);
        return idx >= 0 ? name[..idx] : name;
    }

    /// <summary>
    /// Matches rows from three result sets by <see cref="BenchmarkResult.BenchmarkName"/>.
    /// Missing rows on the SendAsync or Streaming side are filled with zeroes.
    /// </summary>
    private static IReadOnlyList<(string Name, BenchmarkResult Http, BenchmarkResult SendAsync, BenchmarkResult Streaming)>
        MatchRows3Way(
            IReadOnlyList<BenchmarkResult> httpResults,
            IReadOnlyList<BenchmarkResult> sendAsyncResults,
            IReadOnlyList<BenchmarkResult> streamingResults)
    {
        var httpMap = httpResults.ToDictionary(r => r.BenchmarkName, StringComparer.OrdinalIgnoreCase);
        var streamMap = streamingResults.ToDictionary(r => r.BenchmarkName, StringComparer.OrdinalIgnoreCase);

        var result = new List<(string, BenchmarkResult, BenchmarkResult, BenchmarkResult)>();
        var matchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var send in sendAsyncResults)
        {
            var name = send.BenchmarkName;
            var http = httpMap.TryGetValue(name, out var h) ? h : Zero(name);
            var stream = streamMap.TryGetValue(name, out var s) ? s : Zero(name);
            result.Add((StripVersionSuffix(name), http, send, stream));
            matchedNames.Add(name);
        }

        foreach (var http in httpResults)
        {
            if (!matchedNames.Contains(http.BenchmarkName))
            {
                result.Add((StripVersionSuffix(http.BenchmarkName), http, Zero(http.BenchmarkName), Zero(http.BenchmarkName)));
            }
        }

        return result;
    }

    private static BenchmarkResult Zero(string name)
        => new(name, 0, 0, 0, 0, 0);
}
