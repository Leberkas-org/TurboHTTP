using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[CollectionDefinition("OTEL", DisableParallelization = true)]
public sealed class OTelCollection;

[Collection("OTEL")]
public sealed class TurboHttpMetricsTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurement<long>> _longMeasurements = new();
    private readonly ConcurrentBag<MetricMeasurement<double>> _doubleMeasurements = new();

    public TurboHttpMetricsTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TurboHttpMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
                _longMeasurements.Add(new MetricMeasurement<long>(instrument.Name, measurement, tags)));

        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
                _doubleMeasurements.Add(new MetricMeasurement<double>(instrument.Name, measurement, tags)));

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    // ── Meter metadata ─────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-001: Meter has name TurboHttp")]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("TurboHttp", TurboHttpMetrics.Meter.Name);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-002: Meter has non-empty version")]
    public void Meter_HasVersion()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.Meter.Version));
    }

    // ── RequestCount ───────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-003: RequestCount increments on each request")]
    public void RequestCount_Increments_OnEachRequest()
    {
        ClearMeasurements();

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("http.response.status_code", 200),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "POST"),
            new KeyValuePair<string, object?>("http.response.status_code", 201),
            new KeyValuePair<string, object?>("server.address", "api.example.com"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.request.count");
        Assert.Equal(2, measurements.Count);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-004: RequestCount carries method tag")]
    public void RequestCount_Carries_MethodTag()
    {
        ClearMeasurements();

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "PUT"),
            new KeyValuePair<string, object?>("http.response.status_code", 200),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.request.count"));
        Assert.Equal("PUT", GetTag(m.Tags, "http.request.method"));
    }

    [Fact(DisplayName = "Diagnostics-Metrics-005: RequestCount carries status_code and server.address tags")]
    public void RequestCount_Carries_StatusCodeAndServerTags()
    {
        ClearMeasurements();

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("http.response.status_code", 404),
            new KeyValuePair<string, object?>("server.address", "api.test.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.request.count"));
        Assert.Equal(404, GetTag(m.Tags, "http.response.status_code"));
        Assert.Equal("api.test.com", GetTag(m.Tags, "server.address"));
    }

    // ── CacheHit / CacheMiss ───────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-006: CacheHit counter increments on hit")]
    public void CacheHit_Increments()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.hit"));
        Assert.Equal(1, m.Value);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-007: CacheMiss counter increments on miss")]
    public void CacheMiss_Increments()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheMiss.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.miss"));
        Assert.Equal(1, m.Value);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-008: Multiple cache hits are counted separately")]
    public void CacheHit_MultipleCounted()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);
        TurboHttpMetrics.CacheHit.Add(1);
        TurboHttpMetrics.CacheHit.Add(1);

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.cache.hit");
        Assert.Equal(3, measurements.Count);
        Assert.All(measurements, m => Assert.Equal(1, m.Value));
    }

    [Fact(DisplayName = "Diagnostics-Metrics-009: Cache hit and miss are independent counters")]
    public void CacheHitAndMiss_AreIndependent()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);
        TurboHttpMetrics.CacheMiss.Add(1);
        TurboHttpMetrics.CacheMiss.Add(1);

        _listener.RecordObservableInstruments();

        Assert.Single(GetLongMeasurements("http.client.cache.hit"));
        Assert.Equal(2, GetLongMeasurements("http.client.cache.miss").Count);
    }

    // ── RetryCount ─────────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-010: RetryCount increments on retry attempt")]
    public void RetryCount_Increments()
    {
        ClearMeasurements();

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.retry.count"));
        Assert.Equal(1, m.Value);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-011: RetryCount carries method and server.address tags")]
    public void RetryCount_Carries_Tags()
    {
        ClearMeasurements();

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "POST"),
            new KeyValuePair<string, object?>("server.address", "retry.example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.retry.count"));
        Assert.Equal("POST", GetTag(m.Tags, "http.request.method"));
        Assert.Equal("retry.example.com", GetTag(m.Tags, "server.address"));
    }

    [Theory(DisplayName = "Diagnostics-Metrics-012: RetryCount records correct attempt per method")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public void RetryCount_RecordsPerMethod(string method)
    {
        ClearMeasurements();

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.retry.count"));
        Assert.Equal(method, GetTag(m.Tags, "http.request.method"));
    }

    // ── RedirectCount ──────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-013: RedirectCount increments on redirect hop")]
    public void RedirectCount_Increments()
    {
        ClearMeasurements();

        TurboHttpMetrics.RedirectCount.Add(1,
            new KeyValuePair<string, object?>("http.response.status_code", 301));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.redirect.count"));
        Assert.Equal(1, m.Value);
        Assert.Equal(301, GetTag(m.Tags, "http.response.status_code"));
    }

    // ── ConnectionActive ───────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-014: ConnectionActive increments when connection added")]
    public void ConnectionActive_Increments()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionActive.Add(1,
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.connection.active"));
        Assert.Equal(1, m.Value);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-015: ConnectionActive decrements when connection removed")]
    public void ConnectionActive_Decrements()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionActive.Add(1,
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));
        TurboHttpMetrics.ConnectionActive.Add(-1,
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.connection.active");
        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => m.Value == 1);
        Assert.Contains(measurements, m => m.Value == -1);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-016: ConnectionActive increment and decrement net to zero")]
    public void ConnectionActive_NetZero()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionActive.Add(1);
        TurboHttpMetrics.ConnectionActive.Add(1);
        TurboHttpMetrics.ConnectionActive.Add(-1);

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.connection.active");
        Assert.Equal(3, measurements.Count);
        Assert.Equal(1, measurements.Sum(m => m.Value));
    }

    // ── ConnectionIdle ─────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-017: ConnectionIdle tracks idle connections")]
    public void ConnectionIdle_Tracks()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionIdle.Add(1,
            new KeyValuePair<string, object?>("server.address", "idle.example.com"),
            new KeyValuePair<string, object?>("server.port", 80));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.connection.idle"));
        Assert.Equal(1, m.Value);
        Assert.Equal("idle.example.com", GetTag(m.Tags, "server.address"));
    }

    // ── RequestDuration ────────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-018: RequestDuration records duration in seconds")]
    public void RequestDuration_Records()
    {
        ClearMeasurements();

        TurboHttpMetrics.RequestDuration.Record(0.125,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("http.response.status_code", 200));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.request.duration"));
        Assert.Equal(0.125, m.Value);
        Assert.Equal("GET", GetTag(m.Tags, "http.request.method"));
        Assert.Equal(200, GetTag(m.Tags, "http.response.status_code"));
    }

    // ── ConnectionDuration ─────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-019: ConnectionDuration records lifetime in seconds")]
    public void ConnectionDuration_Records()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionDuration.Record(30.5,
            new KeyValuePair<string, object?>("server.address", "conn.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.connection.duration"));
        Assert.Equal(30.5, m.Value);
    }

    // ── Instrument metadata ────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Metrics-020: All instruments have correct units")]
    public void Instruments_HaveCorrectUnits()
    {
        Assert.Equal("{request}", TurboHttpMetrics.RequestCount.Unit);
        Assert.Equal("s", TurboHttpMetrics.RequestDuration.Unit);
        Assert.Equal("{hit}", TurboHttpMetrics.CacheHit.Unit);
        Assert.Equal("{miss}", TurboHttpMetrics.CacheMiss.Unit);
        Assert.Equal("{retry}", TurboHttpMetrics.RetryCount.Unit);
        Assert.Equal("{redirect}", TurboHttpMetrics.RedirectCount.Unit);
        Assert.Equal("s", TurboHttpMetrics.ConnectionDuration.Unit);
        Assert.Equal("{connection}", TurboHttpMetrics.ConnectionActive.Unit);
        Assert.Equal("{connection}", TurboHttpMetrics.ConnectionIdle.Unit);
    }

    [Fact(DisplayName = "Diagnostics-Metrics-021: All instruments have non-empty descriptions")]
    public void Instruments_HaveDescriptions()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.CacheHit.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.CacheMiss.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RetryCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RedirectCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ConnectionDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ConnectionActive.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ConnectionIdle.Description));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void ClearMeasurements()
    {
        _longMeasurements.Clear();
        _doubleMeasurements.Clear();
    }

    private List<MetricMeasurement<long>> GetLongMeasurements(string instrumentName)
    {
        return _longMeasurements
            .Where(m => m.InstrumentName == instrumentName)
            .ToList();
    }

    private List<MetricMeasurement<double>> GetDoubleMeasurements(string instrumentName)
    {
        return _doubleMeasurements
            .Where(m => m.InstrumentName == instrumentName)
            .ToList();
    }

    private static object? GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value;
            }
        }

        return null;
    }

    private readonly record struct MetricMeasurement<T> where T : struct
    {
        public string InstrumentName { get; }
        public T Value { get; }
        public KeyValuePair<string, object?>[] Tags { get; }

        public MetricMeasurement(string instrumentName, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            InstrumentName = instrumentName;
            Value = value;
            Tags = tags.ToArray();
        }
    }
}
