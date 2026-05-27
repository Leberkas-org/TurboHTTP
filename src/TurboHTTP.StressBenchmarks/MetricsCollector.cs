using System.Diagnostics;

namespace TurboHTTP.StressBenchmarks;

public sealed class MetricsCollector
{
    private readonly object _lock = new();
    private readonly long _startTimestamp;
    private readonly Dictionary<int, List<double>> _latencyBuckets = [];
    private readonly Dictionary<int, int> _errorBuckets = [];
    private readonly List<(int Second, long MemoryBytes, int GcGen0, int GcGen1, int GcGen2)> _memorySnapshots = [];
    private readonly Timer _memoryTimer;
    private int _gcGen0Baseline;
    private int _gcGen1Baseline;
    private int _gcGen2Baseline;

    public MetricsCollector()
    {
        _gcGen0Baseline = GC.CollectionCount(0);
        _gcGen1Baseline = GC.CollectionCount(1);
        _gcGen2Baseline = GC.CollectionCount(2);
        _startTimestamp = Stopwatch.GetTimestamp();
        _memoryTimer = new Timer(SampleMemory, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Record(RequestResult result)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        var second = (int)elapsed.TotalSeconds;

        lock (_lock)
        {
            if (!_latencyBuckets.TryGetValue(second, out var bucket))
            {
                bucket = new List<double>(512);
                _latencyBuckets[second] = bucket;
            }
            bucket.Add(result.ElapsedMs);

            if (result.Error is not null || result.StatusCode >= 400)
            {
                _errorBuckets.TryGetValue(second, out var count);
                _errorBuckets[second] = count + 1;
            }
        }
    }

    public StressResult Build(ServerType server, StressRunConfig config)
    {
        _memoryTimer.Dispose();

        List<TimeSlice> slices;
        lock (_lock)
        {
            var maxSecond = _latencyBuckets.Count > 0 ? _latencyBuckets.Keys.Max() : 0;
            slices = new List<TimeSlice>(maxSecond + 1);

            for (var s = 0; s <= maxSecond; s++)
            {
                var latencies = _latencyBuckets.TryGetValue(s, out var bucket) ? bucket : [];
                _errorBuckets.TryGetValue(s, out var errors);

                var snapshot = _memorySnapshots.FirstOrDefault(m => m.Second == s);

                double p50 = 0, p95 = 0, p99 = 0;
                if (latencies.Count > 0)
                {
                    latencies.Sort();
                    p50 = Percentile(latencies, 0.50);
                    p95 = Percentile(latencies, 0.95);
                    p99 = Percentile(latencies, 0.99);
                }

                slices.Add(new TimeSlice(
                    s,
                    latencies.Count,
                    errors,
                    p50,
                    p95,
                    p99,
                    snapshot.MemoryBytes,
                    snapshot.GcGen0,
                    snapshot.GcGen1,
                    snapshot.GcGen2));
            }
        }

        var totalRequests = slices.Sum(s => s.Requests);
        var totalErrors = slices.Sum(s => s.Errors);
        var durationSeconds = slices.Count > 0 ? slices.Count : 1;

        var allLatencies = new List<double>(totalRequests);
        lock (_lock)
        {
            foreach (var bucket in _latencyBuckets.Values)
            {
                allLatencies.AddRange(bucket);
            }
        }
        allLatencies.Sort();

        var summary = new StressSummary(
            totalRequests,
            totalErrors,
            totalRequests / (double)durationSeconds,
            allLatencies.Count > 0 ? Percentile(allLatencies, 0.50) : 0,
            allLatencies.Count > 0 ? Percentile(allLatencies, 0.95) : 0,
            allLatencies.Count > 0 ? Percentile(allLatencies, 0.99) : 0,
            _memorySnapshots.Count > 0 ? _memorySnapshots.Max(m => m.MemoryBytes) : 0,
            _memorySnapshots.Count > 0 ? _memorySnapshots[^1].MemoryBytes : 0,
            totalRequests > 0 ? totalErrors / (double)totalRequests : 0);

        return new StressResult(server, config, slices, summary);
    }

    private void SampleMemory(object? state)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        var second = (int)elapsed.TotalSeconds;
        var memory = GC.GetTotalMemory(false);
        var gcGen0 = GC.CollectionCount(0) - _gcGen0Baseline;
        var gcGen1 = GC.CollectionCount(1) - _gcGen1Baseline;
        var gcGen2 = GC.CollectionCount(2) - _gcGen2Baseline;

        lock (_lock)
        {
            _memorySnapshots.Add((second, memory, gcGen0, gcGen1, gcGen2));
        }
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        var index = (int)(sorted.Count * percentile);
        if (index >= sorted.Count)
        {
            index = sorted.Count - 1;
        }
        return sorted[index];
    }
}
