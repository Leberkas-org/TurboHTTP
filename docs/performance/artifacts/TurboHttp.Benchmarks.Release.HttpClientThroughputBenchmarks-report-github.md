``` ini

BenchmarkDotNet=v0.13.2, OS=Windows 11 (10.0.26100.7840)
AMD Ryzen 5 7600X, 1 CPU, 12 logical and 6 physical cores
.NET SDK=10.0.103
  [Host]     : .NET 10.0.3 (10.0.326.7603), X64 RyuJIT AVX2
  Job-FOLMGH : .NET 10.0.3 (10.0.326.7603), X64 RyuJIT AVX2
  Dry        : .NET 10.0.3 (10.0.326.7603), X64 RyuJIT AVX2


```
|                          Method |        Job | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount |      Mean |    Error |   StdDev | Ratio | RatioSD |   Req/sec |   Gen0 |   Gen1 | Allocated | Alloc Ratio |
|-------------------------------- |----------- |--------------- |------------ |------------ |------------- |------------ |----------:|---------:|---------:|------:|--------:|----------:|-------:|-------:|----------:|------------:|
| BmRelThr01a_Baseline_HttpClient | Job-FOLMGH |              5 |     Default |     Default |           16 |           3 |  16.52 μs | 3.236 μs | 0.501 μs |  1.00 |    0.00 | 60,515.62 | 0.1221 |      - |   2.31 KB |        1.00 |
|    BmRelThr01b_Custom_TurboHttp | Job-FOLMGH |              5 |     Default |     Default |           16 |           3 |  15.72 μs | 1.297 μs | 0.201 μs |  0.95 |    0.02 | 63,601.60 | 0.4578 | 0.0916 |   7.26 KB |        3.15 |
|                                 |            |                |             |             |              |             |           |          |          |       |         |           |        |        |           |             |
| BmRelThr01a_Baseline_HttpClient |        Dry |              1 |           1 |   ColdStart |            1 |           1 | 479.26 μs |       NA | 0.000 μs |  1.00 |    0.00 |  2,086.57 |      - |      - |   2.35 KB |        1.00 |
|    BmRelThr01b_Custom_TurboHttp |        Dry |              1 |           1 |   ColdStart |            1 |           1 |  84.64 μs |       NA | 0.000 μs |  0.18 |    0.00 | 11,815.09 |      - |      - |   7.43 KB |        3.17 |
