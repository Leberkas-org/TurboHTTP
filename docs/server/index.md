# TurboHTTP Server

TurboHTTP Server is a high-performance `IServer` implementation for ASP.NET Core, built on Akka.Streams. It replaces Kestrel as the transport layer — handling TCP/QUIC connections, HTTP protocol negotiation, and wire-format encoding — while your application continues to use standard ASP.NET Core middleware, routing, and dependency injection.

::: tip Drop-In Replacement
TurboHTTP is not a framework. It's a transport. Register it with `UseTurboHttp()`, then write standard ASP.NET Core code.
:::

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

app.MapGet("/", () => "Hello from TurboHTTP!");

await app.RunAsync();
```

## What's Included

| Feature | Description |
|---------|-------------|
| **IServer implementation** | Drop-in replacement for Kestrel — registers as `IServer` in DI |
| **HTTP/1.0, 1.1, 2, 3** | Full protocol support with ALPN negotiation |
| **Actor-based connections** | Each connection runs in its own Akka.NET actor with supervision |
| **Akka.Streams backpressure** | Reactive streams pipeline protects against slow clients |
| **Graceful shutdown** | Coordinated drain with configurable timeout |
| **IFeatureCollection** | Standard ASP.NET Core feature interfaces for request/response |
| **HTTPS & TLS** | Certificate configuration, ALPN, TLS 1.2/1.3 |
| **TCP & QUIC transport** | Via Servus.Akka.Transport |

## What TurboHTTP Is NOT

TurboHTTP does not provide its own middleware, routing, or request handling. It handles the transport layer — everything from the TCP/QUIC socket up through HTTP protocol decoding. Your application code uses:

- **Standard ASP.NET Core middleware** — `app.Use()`, `app.UseExceptionHandler()`, etc.
- **Standard routing** — `app.MapGet()`, `app.MapPost()`, controllers, minimal APIs
- **Standard DI** — `builder.Services.AddScoped<T>()`, constructor injection
- **Standard configuration** — `appsettings.json`, environment variables, user secrets

## Supported Feature Interfaces

TurboHTTP implements these ASP.NET Core feature interfaces per request:

| Interface | Purpose |
|-----------|---------|
| `IHttpRequestFeature` | Request method, path, headers, body |
| `IHttpResponseFeature` | Response status code, headers |
| `IHttpResponseBodyFeature` | Response body writing |
| `IHttpRequestBodyDetectionFeature` | Whether the request has a body |
| `IHttpResponseTrailersFeature` | HTTP trailer headers |
| `IHttpConnectionFeature` | Connection ID, local/remote addresses |
| `ITlsHandshakeFeature` | TLS protocol, cipher suite |
| `IHttpRequestLifetimeFeature` | Request abort token |
| `IHttpRequestIdentifierFeature` | Unique request identifier |
| `IHttpMaxRequestBodySizeFeature` | Request body size limit |
| `IHttpBodyControlFeature` | Allow synchronous I/O control |

## Next Steps

- [Installation & Setup](./installation) — NuGet packages, endpoint configuration
- [Configuration](./configuration) — options, timeouts, protocols, HTTPS
- [Using with ASP.NET Core](./aspnet-core) — middleware, routing, DI patterns
- [Hosting & Lifecycle](./hosting) — actor hierarchy, graceful shutdown
- [Performance Tuning](./performance) — concurrency, buffers, timeouts
- [Troubleshooting](./troubleshooting) — common issues and solutions
