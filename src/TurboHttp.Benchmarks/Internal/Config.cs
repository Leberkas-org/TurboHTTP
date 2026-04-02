using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace TurboHttp.Benchmarks.Internal;

public class RequestsPerSecondColumn : IColumn
{
    public string Id => nameof(RequestsPerSecondColumn);
    public string ColumnName => "Req/sec";

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public bool IsAvailable(Summary summary) => true;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => -1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Requests per Second";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        if (!summary.HasReport(benchmarkCase))
        {
            return "<not found>";
        }

        var report = summary[benchmarkCase];
        var statistics = report?.ResultStatistics;
        if (statistics is null)
        {
            return "<not found>";
        }

        // For concurrent benchmarks, each invocation fires ConcurrencyLevel requests
        // simultaneously. The Mean measures one invocation, so multiply to get actual req/s.
        var isConcurrent = benchmarkCase.Descriptor.WorkloadMethod.Name.Contains("Concurrent",
            StringComparison.Ordinal);
        var concurrencyLevel = isConcurrent
            && benchmarkCase.Parameters.Items.FirstOrDefault(p => p.Name == "ConcurrencyLevel")?.Value is int cl
            ? cl : 1;

        var invocationsPerSecond = 1.0 / (statistics.Mean / 1e9);
        var requestsPerSecond = invocationsPerSecond * concurrencyLevel;

        return requestsPerSecond.ToString("N2");
    }
}

public class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddColumn(new RequestsPerSecondColumn());
    }
}

/// <summary>
/// Benchmark configuration for engine-level throughput and latency measurements.
/// Includes p50/p95/p100 latency percentile columns, memory diagnostics, and a
/// requests-per-second column for throughput visibility.
/// </summary>
public class EngineBenchmarkConfig : ManualConfig
{
    public EngineBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddColumn(StatisticColumn.P50);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.P100);
        AddColumn(new RequestsPerSecondColumn());
    }
}