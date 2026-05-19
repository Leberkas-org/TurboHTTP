# API Reference

TurboHTTP's public API is organized into client, server, and feature configuration.

## Client API

| Type | Description | Reference |
|------|-------------|-----------|
| `ITurboHttpClientFactory` | Creates named client instances | [Client API](./client) |
| `ITurboHttpClient` | The HTTP client — `SendAsync` and channel-based API | [Client API](./client) |
| `TurboClientOptions` | Connection, TLS, proxy, and protocol settings | [Client Options](./client-options) |
| `Http1Options` / `Http2Options` / `Http3Options` | Per-protocol tuning | [Client Options](./client-options) |
| `RetryOptions` / `CacheOptions` / `RedirectOptions` | Feature configuration | [Feature Options](./feature-options) |
| Builder extensions (`.WithRetry()`, `.WithCache()`, etc.) | Fluent feature composition | [Feature Options](./feature-options) |

## Server API

| Type | Description | Reference |
|------|-------------|-----------|
| `AddTurboKestrel()` | Server registration (standalone, not Kestrel) | [Server API](./server) |
| `TurboServerOptions` | Endpoints, protocols, timeouts | [Server API](./server) |
| `Http1ServerOptions` / `Http2ServerOptions` / `Http3ServerOptions` | Per-protocol tuning | [Server API](./server) |
| `MapTurboGet/Post/Put/Delete/Patch()` | Route registration | [Server API](./server) |
| `ITurboMiddleware` | Middleware pipeline | [Server API](./server) |
| `TurboHttpContext` | Request/response context with Akka.Streams access | [Server API](./server) |
| `TurboEntityBuilder` | Actor-based entity routing | [Entity Gateway API](./entity-gateway) |

## DI Registration

### Client

```csharp
// Named client
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache();

// Default (unnamed) client
builder.Services.AddTurboHttpClient(options => { ... });

// Typed client
builder.Services.AddTurboHttpClient<IMyApiClient>(options => { ... });
```

### Server

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
app.Run();
```

## Quick Links

- [Client Configuration Guide](/client/configuration)
- [Server Configuration Guide](/server/configuration)
- [HTTP/2 & Multiplexing](/client/http2)
- [HTTP/3 & QUIC](/client/http3)
- [Automatic Retries](/client/retries)
- [HTTP Caching](/client/caching)
- [Cookies](/client/cookies)
- [Redirects](/client/redirects)
