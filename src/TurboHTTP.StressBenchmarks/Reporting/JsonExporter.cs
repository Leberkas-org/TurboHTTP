using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHTTP.StressBenchmarks.Reporting;

public static class JsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task ExportAsync(string scenarioName, StressRunConfig config, StressResult? turbo, StressResult? kestrel)
    {
        var outputDir = Path.Combine("results", "stress");
        Directory.CreateDirectory(outputDir);

        var report = new JsonReport
        {
            Scenario = scenarioName,
            Config = new JsonConfig
            {
                Concurrency = config.Concurrency,
                DurationSeconds = (int)config.Duration.TotalSeconds,
            },
            Turbo = turbo is not null ? MapResult(turbo) : null,
            Kestrel = kestrel is not null ? MapResult(kestrel) : null
        };

        var path = Path.Combine(outputDir, string.Concat(scenarioName, ".json"));
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions);

        Console.WriteLine(string.Concat("JSON exported to: ", path));
    }

    private static JsonServerResult MapResult(StressResult result)
    {
        return new JsonServerResult
        {
            TimeSeries = result.TimeSeries.Select(s => new JsonTimeSlice
            {
                T = s.Second,
                Rps = s.Requests,
                P50 = Math.Round(s.P50Ms, 1),
                P95 = Math.Round(s.P95Ms, 1),
                P99 = Math.Round(s.P99Ms, 1),
                MemoryMb = Math.Round(s.MemoryBytes / (1024.0 * 1024.0), 1),
                Errors = s.Errors,
                GcGen0 = s.GcGen0,
                GcGen1 = s.GcGen1,
                GcGen2 = s.GcGen2
            }).ToList(),
            Summary = new JsonSummary
            {
                TotalRequests = result.Summary.TotalRequests,
                TotalErrors = result.Summary.TotalErrors,
                AvgRps = Math.Round(result.Summary.AvgRps, 1),
                P50Ms = Math.Round(result.Summary.P50Ms, 1),
                P95Ms = Math.Round(result.Summary.P95Ms, 1),
                P99Ms = Math.Round(result.Summary.P99Ms, 1),
                PeakMemoryMb = Math.Round(result.Summary.PeakMemoryBytes / (1024.0 * 1024.0), 1),
                FinalMemoryMb = Math.Round(result.Summary.FinalMemoryBytes / (1024.0 * 1024.0), 1),
                ErrorRate = Math.Round(result.Summary.ErrorRate, 4)
            }
        };
    }

    private sealed class JsonReport
    {
        public string Scenario { get; set; } = "";
        public JsonConfig Config { get; set; } = new();
        public JsonServerResult? Turbo { get; set; }
        public JsonServerResult? Kestrel { get; set; }
    }

    private sealed class JsonConfig
    {
        public int Concurrency { get; set; }
        public int DurationSeconds { get; set; }
    }

    private sealed class JsonServerResult
    {
        public List<JsonTimeSlice> TimeSeries { get; set; } = [];
        public JsonSummary Summary { get; set; } = new();
    }

    private sealed class JsonTimeSlice
    {
        public int T { get; set; }
        public int Rps { get; set; }
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
        public double MemoryMb { get; set; }
        public int Errors { get; set; }
        public int GcGen0 { get; set; }
        public int GcGen1 { get; set; }
        public int GcGen2 { get; set; }
    }

    private sealed class JsonSummary
    {
        public int TotalRequests { get; set; }
        public int TotalErrors { get; set; }
        public double AvgRps { get; set; }
        public double P50Ms { get; set; }
        public double P95Ms { get; set; }
        public double P99Ms { get; set; }
        public double PeakMemoryMb { get; set; }
        public double FinalMemoryMb { get; set; }
        public double ErrorRate { get; set; }
    }
}
