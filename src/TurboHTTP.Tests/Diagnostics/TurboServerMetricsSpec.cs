using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TurboHTTP.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboServerMetricsSpec : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurement<long>> _longMeasurements = [];
    private readonly ConcurrentBag<MetricMeasurement<double>> _doubleMeasurements = [];

    public TurboServerMetricsSpec()
    {
        var meterName = Metrics.Meter.Name;
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            _longMeasurements.Add(new MetricMeasurement<long>(instrument.Name, measurement, tags)));

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            _doubleMeasurements.Add(new MetricMeasurement<double>(instrument.Name, measurement, tags)));

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ActiveConnections_should_increment_and_decrement()
    {
        ClearMeasurements();

        Metrics.ActiveConnections().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        Metrics.ActiveConnections().Add(-1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("kestrel.active_connections");
        Assert.Equal(2, measurements.Count);
        Assert.Equal(0, measurements.Sum(m => m.Value));
    }

    [Fact(Timeout = 5000)]
    public void ConnectionDuration_should_record_seconds()
    {
        ClearMeasurements();

        Metrics.ConnectionDuration().Record(1.5,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080),
            new KeyValuePair<string, object?>("network.protocol.version", "2"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("kestrel.connection.duration"));
        Assert.Equal(1.5, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void RejectedConnections_should_increment()
    {
        ClearMeasurements();

        Metrics.RejectedConnections().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("kestrel.rejected_connections"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeDuration_should_record()
    {
        ClearMeasurements();

        Metrics.TlsHandshakeDuration().Record(0.05,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("tls.protocol.version", "1.3"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("kestrel.tls_handshake.duration"));
        Assert.Equal(0.05, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void ActiveTlsHandshakes_should_increment_and_decrement()
    {
        ClearMeasurements();

        Metrics.ActiveTlsHandshakes().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 443));

        Metrics.ActiveTlsHandshakes().Add(-1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 443));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("kestrel.active_tls_handshakes");
        Assert.Equal(2, measurements.Count);
        Assert.Equal(0, measurements.Sum(m => m.Value));
    }

    [Fact(Timeout = 5000)]
    public void ServerActiveRequests_should_carry_method_and_scheme()
    {
        ClearMeasurements();

        Metrics.ServerActiveRequests().Add(1,
            new KeyValuePair<string, object?>("url.scheme", "https"),
            new KeyValuePair<string, object?>("http.request.method", "GET"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("http.server.active_requests"));
        Assert.Equal("https", GetTag(m.Tags, "url.scheme"));
        Assert.Equal("GET", GetTag(m.Tags, "http.request.method"));
    }

    [Fact(Timeout = 5000)]
    public void ServerRequestDuration_should_record_with_status()
    {
        ClearMeasurements();

        Metrics.ServerRequestDuration().Record(0.250,
            new KeyValuePair<string, object?>("http.request.method", "POST"),
            new KeyValuePair<string, object?>("http.response.status_code", 201),
            new KeyValuePair<string, object?>("url.scheme", "https"),
            new KeyValuePair<string, object?>("network.protocol.version", "2"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("http.server.request.duration"));
        Assert.Equal(0.250, m.Value);
        Assert.Equal("POST", GetTag(m.Tags, "http.request.method"));
        Assert.Equal(201, GetTag(m.Tags, "http.response.status_code"));
    }

    [Fact(Timeout = 5000)]
    public void OTelStandard_instruments_should_have_correct_units()
    {
        Assert.Equal("{connection}", Metrics.ActiveConnections().Unit);
        Assert.Equal("s", Metrics.ConnectionDuration().Unit);
        Assert.Equal("{connection}", Metrics.RejectedConnections().Unit);
        Assert.Equal("s", Metrics.TlsHandshakeDuration().Unit);
        Assert.Equal("{handshake}", Metrics.ActiveTlsHandshakes().Unit);
        Assert.Equal("{request}", Metrics.ServerActiveRequests().Unit);
        Assert.Equal("s", Metrics.ServerRequestDuration().Unit);
    }

    [Fact(Timeout = 5000)]
    public void OTelStandard_instruments_should_have_descriptions()
    {
        Assert.False(string.IsNullOrEmpty(Metrics.ActiveConnections().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.ConnectionDuration().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.RejectedConnections().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.TlsHandshakeDuration().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.ActiveTlsHandshakes().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.ServerActiveRequests().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.ServerRequestDuration().Description));
    }

    [Fact(Timeout = 5000)]
    public void PipelineInFlight_should_increment_and_decrement()
    {
        ClearMeasurements();

        Metrics.PipelineInFlight().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        Metrics.PipelineInFlight().Add(-1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("turbo.server.pipeline.inflight");
        Assert.Equal(2, measurements.Count);
        Assert.Equal(0, measurements.Sum(m => m.Value));
    }

    [Fact(Timeout = 5000)]
    public void PipelinePending_should_track_reorder_buffer()
    {
        ClearMeasurements();

        Metrics.PipelinePending().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"),
            new KeyValuePair<string, object?>("server.port", 8080));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("turbo.server.pipeline.pending"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void HandlerTimeouts_should_increment()
    {
        ClearMeasurements();

        Metrics.HandlerTimeouts().Add(1,
            new KeyValuePair<string, object?>("server.address", "127.0.0.1"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetLongMeasurements("turbo.server.handler.timeouts"));
        Assert.Equal(1, m.Value);
    }

    [Fact(Timeout = 5000)]
    public void DrainActive_should_track_draining_connections()
    {
        ClearMeasurements();

        Metrics.DrainActive().Add(1);
        Metrics.DrainActive().Add(-1);

        _listener.RecordObservableInstruments();

        var measurements = GetLongMeasurements("turbo.server.drain.active");
        Assert.Equal(2, measurements.Count);
        Assert.Equal(0, measurements.Sum(m => m.Value));
    }

    [Fact(Timeout = 5000)]
    public void ProtocolNegotiationDuration_should_record()
    {
        ClearMeasurements();

        Metrics.ProtocolNegotiationDuration().Record(0.002,
            new KeyValuePair<string, object?>("network.protocol.version", "2"));

        _listener.RecordObservableInstruments();

        var m = Assert.Single(GetDoubleMeasurements("turbo.server.protocol_negotiation.duration"));
        Assert.Equal(0.002, m.Value);
        Assert.Equal("2", GetTag(m.Tags, "network.protocol.version"));
    }

    [Fact(Timeout = 5000)]
    public void Differenzierung_instruments_should_have_correct_units()
    {
        Assert.Equal("{request}", Metrics.PipelineInFlight().Unit);
        Assert.Equal("{request}", Metrics.PipelinePending().Unit);
        Assert.Equal("{timeout}", Metrics.HandlerTimeouts().Unit);
        Assert.Equal("{connection}", Metrics.DrainActive().Unit);
        Assert.Equal("s", Metrics.ProtocolNegotiationDuration().Unit);
    }

    [Fact(Timeout = 5000)]
    public void Differenzierung_instruments_should_have_descriptions()
    {
        Assert.False(string.IsNullOrEmpty(Metrics.PipelineInFlight().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.PipelinePending().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.HandlerTimeouts().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.DrainActive().Description));
        Assert.False(string.IsNullOrEmpty(Metrics.ProtocolNegotiationDuration().Description));
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
