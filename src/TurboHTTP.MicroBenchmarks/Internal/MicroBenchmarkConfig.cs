using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;

namespace TurboHTTP.MicroBenchmarks.Internal;

public sealed class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddJob(Job.Default.WithGcServer(true));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(JsonExporter.Full);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
    }
}
