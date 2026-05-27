using System.Diagnostics.Metrics;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

internal static class TurboServerMetricsExtensions
{
    private static UpDownCounter<long>? _activeConnections;
    private static Histogram<double>? _connectionDuration;
    private static Counter<long>? _rejectedConnections;
    private static Histogram<double>? _tlsHandshakeDuration;
    private static UpDownCounter<long>? _activeTlsHandshakes;
    private static UpDownCounter<long>? _serverActiveRequests;
    private static Histogram<double>? _serverRequestDuration;
    private static UpDownCounter<long>? _pipelineInFlight;
    private static UpDownCounter<long>? _pipelinePending;
    private static Counter<long>? _handlerTimeouts;
    private static UpDownCounter<long>? _drainActive;
    private static Histogram<double>? _protocolNegotiationDuration;

    public static UpDownCounter<long> ActiveConnections(this ServusMetrics metrics)
    {
        return _activeConnections ??= metrics.Meter.CreateUpDownCounter<long>(
            "kestrel.active_connections",
            unit: "{connection}",
            description: "Number of connections that are currently active on the server.");
    }

    public static Histogram<double> ConnectionDuration(this ServusMetrics metrics)
    {
        return _connectionDuration ??= metrics.Meter.CreateHistogram<double>(
            "kestrel.connection.duration",
            unit: "s",
            description: "The duration of connections on the server.");
    }

    public static Counter<long> RejectedConnections(this ServusMetrics metrics)
    {
        return _rejectedConnections ??= metrics.Meter.CreateCounter<long>(
            "kestrel.rejected_connections",
            unit: "{connection}",
            description: "Number of connections rejected by the server.");
    }

    public static Histogram<double> TlsHandshakeDuration(this ServusMetrics metrics)
    {
        return _tlsHandshakeDuration ??= metrics.Meter.CreateHistogram<double>(
            "kestrel.tls_handshake.duration",
            unit: "s",
            description: "The duration of TLS handshakes on the server.");
    }

    public static UpDownCounter<long> ActiveTlsHandshakes(this ServusMetrics metrics)
    {
        return _activeTlsHandshakes ??= metrics.Meter.CreateUpDownCounter<long>(
            "kestrel.active_tls_handshakes",
            unit: "{handshake}",
            description: "Number of TLS handshakes that are currently in progress on the server.");
    }

    public static UpDownCounter<long> ServerActiveRequests(this ServusMetrics metrics)
    {
        return _serverActiveRequests ??= metrics.Meter.CreateUpDownCounter<long>(
            "http.server.active_requests",
            unit: "{request}",
            description: "Number of active HTTP server requests.");
    }

    public static Histogram<double> ServerRequestDuration(this ServusMetrics metrics)
    {
        return _serverRequestDuration ??= metrics.Meter.CreateHistogram<double>(
            "http.server.request.duration",
            unit: "s",
            description: "Duration of HTTP server requests.");
    }

    public static UpDownCounter<long> PipelineInFlight(this ServusMetrics metrics)
    {
        return _pipelineInFlight ??= metrics.Meter.CreateUpDownCounter<long>(
            "turbo.server.pipeline.inflight",
            unit: "{request}",
            description: "Number of requests currently being processed by the application handler.");
    }

    public static UpDownCounter<long> PipelinePending(this ServusMetrics metrics)
    {
        return _pipelinePending ??= metrics.Meter.CreateUpDownCounter<long>(
            "turbo.server.pipeline.pending",
            unit: "{request}",
            description: "Number of completed responses waiting in the reorder buffer.");
    }

    public static Counter<long> HandlerTimeouts(this ServusMetrics metrics)
    {
        return _handlerTimeouts ??= metrics.Meter.CreateCounter<long>(
            "turbo.server.handler.timeouts",
            unit: "{timeout}",
            description: "Number of application handler timeouts.");
    }

    public static UpDownCounter<long> DrainActive(this ServusMetrics metrics)
    {
        return _drainActive ??= metrics.Meter.CreateUpDownCounter<long>(
            "turbo.server.drain.active",
            unit: "{connection}",
            description: "Number of connections currently draining during graceful shutdown.");
    }

    public static Histogram<double> ProtocolNegotiationDuration(this ServusMetrics metrics)
    {
        return _protocolNegotiationDuration ??= metrics.Meter.CreateHistogram<double>(
            "turbo.server.protocol_negotiation.duration",
            unit: "s",
            description: "The duration of protocol negotiation.");
    }
}
