namespace TurboHTTP.StressBenchmarks;

public sealed record RequestResult(int StatusCode, double ElapsedMs, Exception? Error);
