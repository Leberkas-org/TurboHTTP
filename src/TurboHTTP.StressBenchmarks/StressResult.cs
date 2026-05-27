namespace TurboHTTP.StressBenchmarks;

public sealed record StressResult(
    ServerType Server,
    StressRunConfig Config,
    IReadOnlyList<TimeSlice> TimeSeries,
    StressSummary Summary);
