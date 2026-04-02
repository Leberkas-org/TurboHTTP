using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[CollectionDefinition("OTEL", DisableParallelization = true)]
public sealed class OTelCollection;

[Collection("OTEL")]
public sealed class TurboHttpMetricsSpec : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurement<long>> _longMeasurements = new();
    private readonly ConcurrentBag<MetricMeasurement<double>> _doubleMeasurements = new();

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


    [Fact]
    public void Meter_should_have_correct_name()
    {
        Assert.Equal("TurboHttp", TurboHttpMetrics.Meter.Name);
    }

    [Fact]
    public void Meter_should_have_version()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpMetrics.Meter.Version));
    }


    [Fact]
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

    [Fact]
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

    [Fact]
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


    [Fact]
    public void CacheHit_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheHit.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.hit"));
        Assert.Equal(1, m.Value);
    }

    [Fact]
    public void CacheMiss_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.CacheMiss.Add(1);

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.cache.miss"));
        Assert.Equal(1, m.Value);
    }

    [Fact]
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

    [Fact]
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


    [Fact]
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

    [Fact]
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


    [Fact]
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


    [Fact]
    public void ConnectionActive_should_increment()
    {
        ClearMeasurements();

        TurboHttpMetrics.ConnectionActive.Add(1,
            new KeyValuePair<string, object?>("server.address", "pool.example.com"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.client.connection.active"));
        Assert.Equal(1, m.Value);
    }

    [Fact]
    public void ConnectionActive_should_decrement()
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

    [Fact]
    public void ConnectionActive_should_net_to_zero()
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


    [Fact]
    public void ConnectionIdle_should_track()
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


    [Fact]
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


    [Fact]
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


    [Fact]
    public void Instruments_should_have_correct_units()
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

    [Fact]
    public void Instruments_should_have_descriptions()
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
