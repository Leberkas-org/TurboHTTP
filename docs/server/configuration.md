# Configuration

All server configuration flows through `TurboServerOptions`, passed to `UseTurboHttp()`.

```csharp
builder.Host.UseTurboHttp(options =>
{
    // configure here
});
```

## General Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `HandlerTimeout` | `TimeSpan` | 30s | Maximum time for a request handler to complete |
| `HandlerGracePeriod` | `TimeSpan` | 5s | Extra time after handler timeout before force-closing |
| `GracefulShutdownTimeout` | `TimeSpan` | 30s | Time to drain connections during shutdown |
| `BodyBufferThreshold` | `int` | 64 * 1024 | Request body buffer size before streaming |
| `BodyConsumptionTimeout` | `TimeSpan` | 30s | Time for the app to consume the request body |
| `ResponseBodyChunkSize` | `int` | 16 * 1024 | Chunk size for response body writes |

## Connection Limits

Access via `options.Limits`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentConnections` | `int` | 0 (unlimited) | Maximum concurrent connections |
| `MaxConcurrentUpgradedConnections` | `int` | 0 (unlimited) | Maximum upgraded connections (WebSocket) |
| `MaxRequestBodySize` | `long` | 30 * 1024 * 1024 | Global max request body size |
| `MaxRequestHeaderCount` | `int` | 100 | Maximum request headers |
| `MaxRequestHeadersTotalSize` | `int` | 32 * 1024 | Maximum total header bytes |
| `KeepAliveTimeout` | `TimeSpan` | 130s | Idle connection timeout |
| `RequestHeadersTimeout` | `TimeSpan` | 30s | Time to receive request headers |
| `MinRequestBodyDataRate` | `double` | 0 | Minimum body bytes/sec (0 = disabled) |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing body rate |
| `MinResponseDataRate` | `double` | 0 | Minimum response bytes/sec (0 = disabled) |
| `MinResponseDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing response rate |

## HTTP/1.x Options

Access via `options.Http1`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRequestLineLength` | `int` | 8192 | Maximum bytes for the request line |
| `MaxRequestTargetLength` | `int` | 8192 | Maximum bytes for the request target (URL) |
| `MaxPipelinedRequests` | `int` | 16 | Maximum queued pipelined requests |
| `MaxChunkExtensionLength` | `int` | 4096 | Maximum bytes for chunk extensions |
| `BodyReadTimeout` | `TimeSpan` | 30s | Timeout for reading request body |
| `MaxRequestBodySize` | `long` | 30_000_000 | HTTP/1.x-specific body size limit |
| `MaxHeaderListSize` | `int` | 32 * 1024 | Maximum total header bytes |
| `KeepAliveTimeout` | `TimeSpan?` | null (uses global) | Per-protocol keep-alive override |
| `RequestHeadersTimeout` | `TimeSpan?` | null (uses global) | Per-protocol headers timeout override |

## HTTP/2 Options

Access via `options.Http2`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentStreams` | `int` | 100 | Maximum concurrent streams per connection |
| `InitialConnectionWindowSize` | `int` | 1 * 1024 * 1024 | Connection-level flow control window |
| `InitialStreamWindowSize` | `int` | 768 * 1024 | Per-stream flow control window |
| `MaxFrameSize` | `int` | 16 * 1024 | Maximum HTTP/2 frame payload size |
| `MaxHeaderListSize` | `int` | 32 * 1024 | Maximum total header bytes |
| `HeaderTableSize` | `int` | 4 * 1024 | HPACK dynamic table size |
| `MaxRequestBodySize` | `long` | 30_000_000 | HTTP/2-specific body size limit |
| `MaxResponseBufferSize` | `long` | 64 * 1024 | Response buffering before backpressure |
| `KeepAliveTimeout` | `TimeSpan` | 130s | Connection idle timeout |
| `RequestHeadersTimeout` | `TimeSpan` | 30s | Time to receive request headers |
| `MinRequestBodyDataRate` | `int` | 240 | Minimum body bytes/sec |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing rate |

## HTTP/3 Options

Access via `options.Http3`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentStreams` | `int` | 100 | Maximum concurrent streams per connection |
| `MaxHeaderListSize` | `int` | 32 * 1024 | Maximum total header bytes |
| `QpackMaxTableCapacity` | `int` | 0 | QPACK dynamic table capacity (0 = static only) |
| `EnableWebTransport` | `bool` | false | Enable WebTransport support |
| `MaxRequestBodySize` | `long` | 30_000_000 | HTTP/3-specific body size limit |
| `KeepAliveTimeout` | `TimeSpan` | 130s | Connection idle timeout |
| `RequestHeadersTimeout` | `TimeSpan` | 30s | Time to receive request headers |
| `MinRequestBodyDataRate` | `int` | 240 | Minimum body bytes/sec |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing rate |

## Example: Full Configuration

```csharp
builder.Host.UseTurboHttp(options =>
{
    // Endpoints
    options.ListenLocalhost(5000);
    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Timeouts
    options.HandlerTimeout = TimeSpan.FromSeconds(60);
    options.HandlerGracePeriod = TimeSpan.FromSeconds(10);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);

    // Limits
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;

    // HTTP/2
    options.Http2.MaxConcurrentStreams = 200;
    options.Http2.InitialConnectionWindowSize = 2 * 1024 * 1024;

    // HTTP/3
    options.Http3.MaxConcurrentStreams = 200;
});
```

## Next Steps

- [Using with ASP.NET Core](./aspnet-core) — how TurboHTTP integrates with ASP.NET Core
- [Performance Tuning](./performance) — when and how to tune these options
- [Hosting & Lifecycle](./hosting) — shutdown behavior
