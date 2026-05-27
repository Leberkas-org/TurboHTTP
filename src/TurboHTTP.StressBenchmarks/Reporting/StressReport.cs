using System.Text;

namespace TurboHTTP.StressBenchmarks.Reporting;

public static class StressReport
{
    public static void PrintScenario(StressResult turbo, StressResult kestrel)
    {
        var config = turbo.Config;
        Console.WriteLine();
        Console.WriteLine(string.Concat("## ", config.ScenarioName, " (", config.Concurrency.ToString(), " concurrent, ", ((int)config.Duration.TotalSeconds).ToString(), "s)"));
        Console.WriteLine();
        Console.WriteLine("| Metric                | TurboServer | Kestrel   | Delta   |");
        Console.WriteLine("|-----------------------|-------------|-----------|---------|");

        PrintRow("Throughput (req/s)", turbo.Summary.AvgRps, kestrel.Summary.AvgRps, "F0", higherIsBetter: true);
        PrintRow("Latency p50 (ms)", turbo.Summary.P50Ms, kestrel.Summary.P50Ms, "F1", higherIsBetter: false);
        PrintRow("Latency p95 (ms)", turbo.Summary.P95Ms, kestrel.Summary.P95Ms, "F1", higherIsBetter: false);
        PrintRow("Latency p99 (ms)", turbo.Summary.P99Ms, kestrel.Summary.P99Ms, "F1", higherIsBetter: false);
        PrintRow("Peak Memory (MB)", turbo.Summary.PeakMemoryBytes / (1024.0 * 1024.0), kestrel.Summary.PeakMemoryBytes / (1024.0 * 1024.0), "F0", higherIsBetter: false);
        PrintCountRow("Errors", turbo.Summary.TotalErrors, kestrel.Summary.TotalErrors);
        PrintRow("Error Rate", turbo.Summary.ErrorRate * 100, kestrel.Summary.ErrorRate * 100, "F1", higherIsBetter: false, suffix: "%");

        Console.WriteLine();
    }

    public static void PrintSummary(IReadOnlyList<(StressResult Turbo, StressResult Kestrel)> results)
    {
        Console.WriteLine("## Summary");
        Console.WriteLine();
        Console.WriteLine("| Scenario         | Winner      | Key Advantage                    |");
        Console.WriteLine("|------------------|-------------|----------------------------------|");

        foreach (var (turbo, kestrel) in results)
        {
            var name = turbo.Config.ScenarioName;
            var turboScore = 0;
            var kestrelScore = 0;

            if (turbo.Summary.P99Ms < kestrel.Summary.P99Ms) turboScore++; else kestrelScore++;
            if (turbo.Summary.PeakMemoryBytes < kestrel.Summary.PeakMemoryBytes) turboScore++; else kestrelScore++;
            if (turbo.Summary.TotalErrors < kestrel.Summary.TotalErrors) turboScore++; else kestrelScore++;

            var winner = turboScore >= kestrelScore ? "TurboServer" : "Kestrel";
            var advantage = BuildAdvantage(turbo, kestrel);

            Console.WriteLine(string.Concat("| ", name.PadRight(16), " | ", winner.PadRight(11), " | ", advantage.PadRight(32), " |"));
        }

        Console.WriteLine();
    }

    private static void PrintRow(string metric, double turbo, double kestrel, string format, bool higherIsBetter, string suffix = "")
    {
        var turboStr = string.Concat(turbo.ToString(format), suffix);
        var kestrelStr = string.Concat(kestrel.ToString(format), suffix);
        var delta = kestrel != 0 ? ((turbo - kestrel) / kestrel) * 100 : 0;
        var deltaStr = string.Concat(delta >= 0 ? "+" : "", delta.ToString("F1"), "%");

        Console.WriteLine(string.Concat("| ", metric.PadRight(21), " | ", turboStr.PadRight(11), " | ", kestrelStr.PadRight(9), " | ", deltaStr.PadRight(7), " |"));
    }

    private static void PrintCountRow(string metric, int turbo, int kestrel)
    {
        Console.WriteLine(string.Concat("| ", metric.PadRight(21), " | ", turbo.ToString().PadRight(11), " | ", kestrel.ToString().PadRight(9), " | —       |"));
    }

    private static string BuildAdvantage(StressResult turbo, StressResult kestrel)
    {
        var parts = new List<string>(3);

        if (kestrel.Summary.P99Ms > 0 && turbo.Summary.P99Ms < kestrel.Summary.P99Ms)
        {
            var pct = ((kestrel.Summary.P99Ms - turbo.Summary.P99Ms) / kestrel.Summary.P99Ms * 100).ToString("F0");
            parts.Add(string.Concat("p99 ", pct, "% lower"));
        }

        if (kestrel.Summary.PeakMemoryBytes > 0 && turbo.Summary.PeakMemoryBytes < kestrel.Summary.PeakMemoryBytes)
        {
            var pct = ((kestrel.Summary.PeakMemoryBytes - turbo.Summary.PeakMemoryBytes) / (double)kestrel.Summary.PeakMemoryBytes * 100).ToString("F0");
            parts.Add(string.Concat(pct, "% less memory"));
        }

        if (turbo.Summary.TotalErrors == 0 && kestrel.Summary.TotalErrors > 0)
        {
            parts.Add("zero errors");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "comparable";
    }
}
