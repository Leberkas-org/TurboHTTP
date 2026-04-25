using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[CollectionDefinition("OTEL", DisableParallelization = true)]
public sealed class OTelCollection;

[Collection("OTEL")]
public sealed class TurboHttpMetricsSpec : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurement<long>> _longMeasurements = [];
    private readonly ConcurrentBag<MetricMeasurement<double>> _doubleMeasurements = [];

    public TurboHttpMetricsSpec()
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
            (instrument, measurement, tags, _) =>
                _longMeasurements.Add(new MetricMeasurement<long>(instrument.Name, measurement, tags)));

        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
                _doubleMeasurements.Add(new MetricMeasurement<double>(instrument.Name, measurement, tags)));

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Meter_should_have_correct_name()
    {
        Assert.Equal("TurboHTTP", TurboHttpMetrics.Meter.Name);
    }

    [Fact(Timeout = 5000)]
    public void Meter_should_have_version()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.Meter.Version));
    }

    [Fact(Timeout = 5000)]
    public void RequestCount_should_increment_on_each_request()
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

    [Fact(Timeout = 5000)]
    public void RequestCount_should_carry_method_tag()
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

    [Fact(Timeout = 5000)]
    public void RequestCount_should_carry_status_code_and_server_tags()
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

    [Fact(Timeout = 5000)]
    public void CacheLookup_should_increment_with_hit_result()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "hit"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.lookup"));
        Assert.Equal(1, m.Value);
        Assert.Equal("hit", GetTag(m.Tags, "cache.result"));
    }

    [Fact(Timeout = 5000)]
    public void CacheLookup_should_increment_with_miss_result()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "miss"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.lookup"));
        Assert.Equal(1, m.Value);
        Assert.Equal("miss", GetTag(m.Tags, "cache.result"));
    }

    [Fact(Timeout = 5000)]
    public void CacheLookup_should_count_multiple()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "hit"));
        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "hit"));
        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "miss"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.cache.lookup");
        Assert.Equal(3, measurements.Count);
    }

    [Fact(Timeout = 5000)]
    public void CacheLookup_hit_and_miss_should_be_distinguished_by_tag()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "hit"));
        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "miss"));
        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", "miss"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.cache.lookup");
        Assert.Single(measurements, m => (string?)GetTag(m.Tags, "cache.result") == "hit");
        Assert.Equal(2, measurements.Count(m => (string?)GetTag(m.Tags, "cache.result") == "miss"));
    }

    [Fact(Timeout = 5000)]
    public void RetryCount_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.retry.count"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void RetryCount_should_carry_tags()
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

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public void RetryCount_should_record_per_method(string method)
    {
        ClearMeasurements();

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("server.address", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.retry.count"));
        Assert.Equal(method, GetTag(m.Tags, "http.request.method"));
    }

    [Fact(Timeout = 5000)]
    public void RedirectCount_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.RedirectCount.Add(1,
            new KeyValuePair<string, object?>("http.response.status_code", 301));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.redirect.count"));
        Assert.Equal(1, m.Value);
        Assert.Equal(301, GetTag(m.Tags, "http.response.status_code"));
    }

    [Fact(Timeout = 5000)]
    public void RequestDuration_should_record()
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

    [Fact(Timeout = 5000)]
    public void ActiveRequests_should_increment_and_decrement()
    {
        ClearMeasurements();

        TurboHttpMetrics.ActiveRequests.Add(1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("server.address", "example.com"),
            new KeyValuePair<string, object?>("server.port", 443),
            new KeyValuePair<string, object?>("url.scheme", "https"));

        TurboHttpMetrics.ActiveRequests.Add(-1,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("server.address", "example.com"),
            new KeyValuePair<string, object?>("server.port", 443),
            new KeyValuePair<string, object?>("url.scheme", "https"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.active_requests");
        Assert.Equal(2, measurements.Count);
        Assert.Equal(0, measurements.Sum(m => m.Value));
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_correct_units()
    {
        Assert.Equal("{request}", TurboHttpMetrics.RequestCount.Unit);
        Assert.Equal("s", TurboHttpMetrics.RequestDuration.Unit);
        Assert.Equal("{lookup}", TurboHttpMetrics.CacheLookup.Unit);
        Assert.Equal("{retry}", TurboHttpMetrics.RetryCount.Unit);
        Assert.Equal("{redirect}", TurboHttpMetrics.RedirectCount.Unit);
        Assert.Equal("{request}", TurboHttpMetrics.ActiveRequests.Unit);
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_descriptions()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.CacheLookup.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RetryCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RedirectCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ActiveRequests.Description));
    }

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
