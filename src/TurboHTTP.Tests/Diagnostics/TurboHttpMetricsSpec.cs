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
    public void CacheHit_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.hit"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void CacheMiss_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheMiss.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.miss"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void CacheHit_should_count_multiple()
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

    [Fact(Timeout = 5000)]
    public void CacheHitAndMiss_should_be_independent()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);
        TurboHttpMetrics.CacheMiss.Add(1);
        TurboHttpMetrics.CacheMiss.Add(1);

        _listener.RecordObservableInstruments();

        Assert.Single(GetLongMeasurements("http.client.cache.hit"));
        Assert.Equal(2, GetLongMeasurements("http.client.cache.miss").Count);
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
    public void OpenConnections_should_increment_active()
    {
        ClearMeasurements();

        TurboHttpMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.open_connections"));
        Assert.Equal(1, m.Value);
        Assert.Equal("active", GetTag(m.Tags, "http.connection.state"));
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_decrement_active()
    {
        ClearMeasurements();

        TurboHttpMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));
        TurboHttpMetrics.OpenConnections.Add(-1,
            new KeyValuePair<string, object?>("http.connection.state", "active"),
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.open_connections");
        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => m.Value == 1);
        Assert.Contains(measurements, m => m.Value == -1);
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_track_idle()
    {
        ClearMeasurements();

        TurboHttpMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "idle"),
            new KeyValuePair<string, object?>("server.address", "idle.example.com"),
            new KeyValuePair<string, object?>("server.port", 80));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.open_connections"));
        Assert.Equal(1, m.Value);
        Assert.Equal("idle", GetTag(m.Tags, "http.connection.state"));
        Assert.Equal("idle.example.com", GetTag(m.Tags, "server.address"));
    }

    [Fact(Timeout = 5000)]
    public void OpenConnections_should_distinguish_active_and_idle()
    {
        ClearMeasurements();

        TurboHttpMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "active"));
        TurboHttpMetrics.OpenConnections.Add(1,
            new KeyValuePair<string, object?>("http.connection.state", "idle"));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("http.client.open_connections");
        Assert.Equal(2, measurements.Count);
        Assert.Contains(measurements, m => GetTag(m.Tags, "http.connection.state")?.ToString() == "active");
        Assert.Contains(measurements, m => GetTag(m.Tags, "http.connection.state")?.ToString() == "idle");
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
    public void ConnectionDuration_should_record()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionDuration.Record(30.5,
            new KeyValuePair<string, object?>("server.address", "conn.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.connection.duration"));
        Assert.Equal(30.5, m.Value);
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
    public void RequestTimeInQueue_should_record()
    {
        ClearMeasurements();

        TurboHttpMetrics.RequestTimeInQueue.Record(0.050,
            new KeyValuePair<string, object?>("http.request.method", "GET"),
            new KeyValuePair<string, object?>("server.address", "example.com"),
            new KeyValuePair<string, object?>("server.port", 443),
            new KeyValuePair<string, object?>("url.scheme", "https"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.client.request.time_in_queue"));
        Assert.Equal(0.050, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void DnsLookupDuration_should_record()
    {
        ClearMeasurements();

        TurboHttpMetrics.DnsLookupDuration.Record(0.015,
            new KeyValuePair<string, object?>("dns.question.name", "example.com"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("dns.lookup.duration"));
        Assert.Equal(0.015, m.Value);
        Assert.Equal("example.com", GetTag(m.Tags, "dns.question.name"));
    }

    [Fact(Timeout = 5000)]
    public void PipelineStall_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.PipelineStall.Add(1,
            new KeyValuePair<string, object?>("stage", "Http20Connection"),
            new KeyValuePair<string, object?>("direction", "request"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("turbohttp.pipeline.stall"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_correct_units()
    {
        Assert.Equal("{request}", TurboHttpMetrics.RequestCount.Unit);
        Assert.Equal("s", TurboHttpMetrics.RequestDuration.Unit);
        Assert.Equal("{hit}", TurboHttpMetrics.CacheHit.Unit);
        Assert.Equal("{miss}", TurboHttpMetrics.CacheMiss.Unit);
        Assert.Equal("{retry}", TurboHttpMetrics.RetryCount.Unit);
        Assert.Equal("{redirect}", TurboHttpMetrics.RedirectCount.Unit);
        Assert.Equal("s", TurboHttpMetrics.ConnectionDuration.Unit);
        Assert.Equal("{connection}", TurboHttpMetrics.OpenConnections.Unit);
        Assert.Equal("{request}", TurboHttpMetrics.ActiveRequests.Unit);
        Assert.Equal("s", TurboHttpMetrics.RequestTimeInQueue.Unit);
        Assert.Equal("s", TurboHttpMetrics.DnsLookupDuration.Unit);
        Assert.Equal("{stall}", TurboHttpMetrics.PipelineStall.Unit);
    }

    [Fact(Timeout = 5000)]
    public void Instruments_should_have_descriptions()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.CacheHit.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.CacheMiss.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RetryCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RedirectCount.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ConnectionDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.OpenConnections.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.ActiveRequests.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.RequestTimeInQueue.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.DnsLookupDuration.Description));
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.PipelineStall.Description));
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
