namespace TurboHTTP.StressBenchmarks;

public sealed record StressSummary(
    int TotalRequests,
    int TotalErrors,
    double AvgRps,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    long PeakMemoryBytes,
    long FinalMemoryBytes,
    double ErrorRate);
