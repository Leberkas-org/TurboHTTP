using BenchmarkDotNet.Running;
using TurboHTTP.Benchmarks.Binkraken;
using TurboHTTP.Benchmarks.Internal;
using TurboHTTP.Benchmarks.Kestrel;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var enumerable = summaries.ToList();

var binkHttp = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenHttpClientConcurrentBenchmarks>());
var binkTurboSend = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboSendAsyncConcurrentBenchmarks>());
var binkTurboStream = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<BinkrakenTurboStreamingConcurrentBenchmarks>());

if (binkHttp is not null
    && binkTurboSend is not null
    && binkTurboStream is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateReport(
        SummaryExtractor.Extract(binkHttp),
        SummaryExtractor.Extract(binkTurboSend),
        SummaryExtractor.Extract(binkTurboStream));

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Binkraken report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown);
    Console.WriteLine($"Binkraken comparison report: {path}");
}
else
{
    Console.WriteLine("Binkraken comparison report skipped — not all 3 benchmark suites ran.");
    Console.WriteLine("Required Binkraken suites:");
    Console.WriteLine($"  BinkrakenHttpClientConcurrentBenchmarks      : {(binkHttp is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboSendAsyncConcurrentBenchmarks  : {(binkTurboSend is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  BinkrakenTurboStreamingConcurrentBenchmarks  : {(binkTurboStream is not null ? "OK" : "MISSING")}");
}

var kestrelHttp = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelHttpClientConcurrentBenchmarks>());
var kestrelTurboSend = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboSendAsyncConcurrentBenchmarks>());
var kestrelTurboStream = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<KestrelTurboStreamingConcurrentBenchmarks>());

if (kestrelHttp is not null
    && kestrelTurboSend is not null
    && kestrelTurboStream is not null)
{
    var markdown = BenchmarkComparisonReport.GenerateKestrelReport(
        SummaryExtractor.Extract(kestrelHttp),
        SummaryExtractor.Extract(kestrelTurboSend),
        SummaryExtractor.Extract(kestrelTurboStream));

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Kestrel report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown);
    Console.WriteLine($"Kestrel comparison report: {path}");
}
else
{
    Console.WriteLine("Kestrel comparison report skipped — not all 3 benchmark suites ran.");
    Console.WriteLine("Required Kestrel suites:");
    Console.WriteLine($"  KestrelHttpClientConcurrentBenchmarks        : {(kestrelHttp is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboSendAsyncConcurrentBenchmarks    : {(kestrelTurboSend is not null ? "OK" : "MISSING")}");
    Console.WriteLine($"  KestrelTurboStreamingConcurrentBenchmarks    : {(kestrelTurboStream is not null ? "OK" : "MISSING")}");
}
