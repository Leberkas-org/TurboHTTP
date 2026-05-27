# Performance Tuning

TurboHTTP's defaults work well for most applications. This page explains when and how to tune server options for specific workloads.

## Concurrency

### Connection Limits

```csharp
builder.Host.UseTurboHttp(options =>
{
    options.Limits.MaxConcurrentConnections = 1000;
});
```

Default is 0 (unlimited). Set a limit to protect against connection exhaustion. Each connection creates an actor, so memory scales linearly with connection count.

### HTTP/2 and HTTP/3 Stream Limits

```csharp
options.Http2.MaxConcurrentStreams = 200;
options.Http3.MaxConcurrentStreams = 200;
```

Default is 100 streams per connection. HTTP/2 and HTTP/3 multiplex many requests over one connection — this controls how many can be active simultaneously.

Higher values improve throughput for clients sending many parallel requests. Lower values reduce per-connection memory and prevent a single connection from dominating server resources.

## Buffers

### Request Body Buffer

```csharp
options.BodyBufferThreshold = 128 * 1024;  // 128 KB
```

Default is 64 KB. Request bodies smaller than this threshold are buffered in memory. Larger bodies stream directly to the application.

- **Increase** for APIs that commonly receive medium-sized payloads (64-256 KB)
- **Decrease** for memory-constrained environments or very large upload workloads

### Response Chunk Size

```csharp
options.ResponseBodyChunkSize = 32 * 1024;  // 32 KB
```

Default is 16 KB. Controls the size of chunks when writing response bodies to the network.

- **Increase** for large response bodies (file downloads, large JSON)
- **Decrease** for low-latency streaming where you want data sent sooner

### HTTP/2 Flow Control Windows

```csharp
options.Http2.InitialConnectionWindowSize = 2 * 1024 * 1024;  // 2 MB
options.Http2.InitialStreamWindowSize = 1 * 1024 * 1024;      // 1 MB
```

Larger windows allow more data in flight before the sender must pause. Increase for high-bandwidth, high-latency connections (CDN, cross-region).

### HTTP/2 Response Buffer

```csharp
options.Http2.MaxResponseBufferSize = 128 * 1024;  // 128 KB
```

Default is 64 KB. Responses are buffered up to this size before backpressure kicks in. Increase for handlers that write large responses in bursts.

## Timeouts

### Handler Timeout

```csharp
options.HandlerTimeout = TimeSpan.FromSeconds(60);
options.HandlerGracePeriod = TimeSpan.FromSeconds(10);
```

The handler timeout starts when `IHttpApplication.ProcessRequestAsync()` begins. If the handler doesn't complete within `HandlerTimeout`, the request's `CancellationToken` is cancelled. After an additional `HandlerGracePeriod`, TurboHTTP returns a 503 response.

- **Short (5-10s)**: API endpoints with fast handlers
- **Medium (30s, default)**: General web applications
- **Long (60-120s)**: File uploads, long-polling, report generation

### Keep-Alive Timeout

```csharp
options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
```

Default is 130s. How long idle connections stay open. Lower values free connection actors sooner. Higher values reduce reconnection overhead for chatty clients.

### Graceful Shutdown

```csharp
options.GracefulShutdownTimeout = TimeSpan.FromSeconds(60);
```

Default is 30s. Time to drain active connections during shutdown. Set this longer than your longest expected handler execution.

## Backpressure

TurboHTTP uses Akka.Streams reactive backpressure throughout the pipeline. When a slow client can't consume response data fast enough, backpressure propagates:

1. Response body writer blocks (async wait, not thread block)
2. `ApplicationBridgeStage` stops pulling new requests from the protocol engine
3. Protocol engine stops reading from the transport
4. TCP/QUIC flow control signals the client to slow down

This prevents memory buildup from buffering responses for slow clients. No configuration needed — it's built into the Akka.Streams pipeline.

## Benchmarking

Run the included benchmarks:

```bash
dotnet run --configuration Release --project src/TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj
```
