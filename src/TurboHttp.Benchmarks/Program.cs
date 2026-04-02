using BenchmarkDotNet.Running;
using TurboHttp.Benchmarks;
using TurboHttp.Benchmarks.Internal;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

var enumerable = summaries.ToList();
var httpSingleSummary = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<HttpClientSingleRequestBenchmarks>());
var turboSingleSummary = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<TurboHttpSingleRequestBenchmarks>());
var httpConcurrentSummary = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<HttpClientConcurrentBenchmarks>());
var turboConcurrentSummary = enumerable.FirstOrDefault(s => s.HasBenchmarksOf<TurboHttpConcurrentBenchmarks>());

if (httpSingleSummary is not null
    && turboSingleSummary is not null
    && httpConcurrentSummary is not null
    && turboConcurrentSummary is not null)
{
    var httpResults = SummaryExtractor.Extract(httpSingleSummary);
    var turboResults = SummaryExtractor.Extract(turboSingleSummary);
    var httpConcurrentResults = SummaryExtractor.Extract(httpConcurrentSummary);
    var turboConcurrentResults = SummaryExtractor.Extract(turboConcurrentSummary);

    var markdown = BenchmarkComparisonReport.GenerateReport(
        httpResults,
        turboResults,
        httpConcurrentResults,
        turboConcurrentResults);

    if (markdown.Contains("NaN") || markdown.Contains("Infinity") || markdown.Contains("Inf%"))
    {
        Console.Error.WriteLine("WARNING: Report contains NaN or Inf values — check input data.");
    }

    var path = BenchmarkComparisonReport.WriteReportToFile(markdown);
    Console.WriteLine($"Comparison report: {path}");
}
else
{
    Console.WriteLine("Comparison report skipped — not all 4 benchmark suites ran " +
        "(HttpClientSingleRequest, TurboHttpSingleRequest, HttpClientConcurrent, TurboHttpConcurrent).");
}