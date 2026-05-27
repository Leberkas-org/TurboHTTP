namespace TurboHTTP.StressBenchmarks;

public sealed record TimeSlice(
    int Second,
    int Requests,
    int Errors,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    long MemoryBytes,
    int GcGen0,
    int GcGen1,
    int GcGen2);
