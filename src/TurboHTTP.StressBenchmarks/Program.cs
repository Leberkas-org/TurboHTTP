namespace TurboHTTP.StressBenchmarks;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("TurboHTTP Stress Benchmarks");
        Console.WriteLine("Usage: dotnet run -- --scenario <name> [--duration <seconds>] [--concurrency <n>] [--server <turbo|kestrel|both>]");
    }
}
