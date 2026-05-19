# Installation & Setup

## Requirements

- **.NET 10.0** or later
- **Akka.NET** is pulled in as a transitive dependency — no manual installation needed

## Install the Package

```bash
dotnet add package TurboHTTP
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="TurboHTTP" Version="1.*" />
```

## Minimal Setup

The fastest way to get started is to register TurboHTTP with dependency injection and map a single route:

```csharp
using TurboHTTP;
using TurboHTTP.Hosting;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5000);
});

var app = builder.Build();

app.MapTurboGet("/hello", () => TypedResults.Ok("Hello from TurboHTTP"));

await app.RunAsync();
```

This creates a server listening on `http://localhost:5000` with HTTP/1.1 and HTTP/2 enabled by default.

::: tip
`ListenLocalhost(5000)` is equivalent to listening on `127.0.0.1:5000`. Use `ListenAnyIP(5000)` to listen on all IPv4 addresses.
:::

## Endpoint Configuration

### Multiple Endpoints

Listen on multiple addresses and ports:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5000);        // HTTP on localhost:5000
    options.ListenAnyIP(8080);            // HTTP on all interfaces:8080
    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();                // HTTPS on localhost:5001
    });
});
```

### Explicit Address Binding

Use `Listen()` to bind to a specific IP address:

```csharp
using System.Net;

builder.Services.AddTurboKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 5000);
    options.Listen(IPAddress.Parse("192.168.1.100"), 8080);
});
```

### Protocol Selection

By default, endpoints support both HTTP/1.1 and HTTP/2. To use a specific protocol:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5000, listen =>
    {
        listen.Protocols = HttpProtocols.Http1;    // HTTP/1.1 only
    });

    options.ListenLocalhost(5001, listen =>
    {
        listen.Protocols = HttpProtocols.Http2;    // HTTP/2 only
    });

    options.ListenLocalhost(5002, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;  // Default
    });
});
```

Supported protocols:

| Protocol | Value | Use Case |
|----------|-------|----------|
| `Http1` | HTTP/1.1 only | Legacy clients, maximum compatibility |
| `Http2` | HTTP/2 (ALPN h2) | Modern clients, multiplexing, server push |
| `Http1AndHttp2` | HTTP/1.1 and HTTP/2 (default) | Protocol negotiation via ALPN |
| `Http3` | HTTP/3 (QUIC) | Ultra-low latency, UDP-based |

::: warning
HTTP/3 support is **not yet available** in this release. Use `Http1AndHttp2` or `Http1` on HTTPS endpoints.
:::

## HTTPS Configuration

### Self-Signed or Generated Certificate

Use TurboHTTP's built-in certificate handling:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();  // Auto-generates a certificate
    });
});
```

### Certificate from File

Load a certificate from disk:

```csharp
options.ListenLocalhost(443, listen =>
{
    listen.UseHttps("certs/server.pfx", "password123");
});
```

### Certificate from X509Certificate2

Provide a certificate object directly:

```csharp
using System.Security.Cryptography.X509Certificates;

var cert = new X509Certificate2("certs/server.pfx", "password123");

options.ListenLocalhost(443, listen =>
{
    listen.UseHttps(cert);
});
```

### HTTPS Defaults

Configure SSL protocol versions and handshake timeout globally:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ConfigureHttpsDefaults(https =>
    {
        https.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13;
        https.HandshakeTimeout = TimeSpan.FromSeconds(10);
    });

    options.ListenLocalhost(443, listen =>
    {
        listen.UseHttps();  // Inherits global HTTPS defaults
    });
});
```

**TurboHttpsOptions** properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServerCertificate` | `X509Certificate2?` | null | Certificate object |
| `CertificatePath` | `string?` | null | Path to .pfx file |
| `CertificatePassword` | `string?` | null | Certificate password |
| `EnabledSslProtocols` | `SslProtocols` | `None` | TLS versions (e.g., Tls12, Tls13) |
| `ClientCertificateValidationCallback` | `RemoteCertificateValidationCallback?` | null | Client cert validation |
| `HandshakeTimeout` | `TimeSpan` | 10 seconds | TLS handshake timeout |

## Configuration from appsettings.json

Use `IConfiguration` to externalize endpoint and HTTPS settings:

```csharp
builder.Services.AddTurboKestrel(
    builder.Configuration,
    options =>
    {
        // Optional: override or add endpoints programmatically
        options.ListenLocalhost(9000);
    }
);
```

In `appsettings.json`:

```json
{
  "TurboKestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      },
      "Https": {
        "Url": "https://localhost:5001",
        "Protocols": "Http1AndHttp2",
        "Certificate": {
          "Path": "certs/server.pfx",
          "Password": "changeit"
        },
        "SslProtocols": "Tls12, Tls13"
      }
    },
    "HttpsDefaults": {
      "SslProtocols": "Tls13",
      "HandshakeTimeout": "00:00:30"
    }
  }
}
```

::: tip
Endpoint configuration keys (e.g., `Endpoints:Http`, `Endpoints:Https`) are arbitrary — use meaningful names for your use case. Multiple endpoints are supported by adding more keys under `Endpoints`.
:::

### Configuration Structure

**Endpoints:**
- `Url` (required) — full URL (http/https, host, port)
- `Protocols` (optional) — `Http1`, `Http2`, `Http1AndHttp2`, `Http3`
- `Certificate` (optional for HTTPS) — sub-object with `Path` and `Password`
- `SslProtocols` (optional) — comma-separated TLS versions

**HttpsDefaults:**
- `SslProtocols` (optional) — applies to all HTTPS endpoints without explicit override
- `HandshakeTimeout` (optional) — TimeSpan format (e.g., `00:00:30`)

## Server Options Reference

Beyond endpoint configuration, TurboServerOptions exposes performance and protocol-level tuning:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Connection limits
    options.MaxConcurrentConnections = 1000;
    options.MaxConcurrentUpgradedConnections = 500;

    // Timeouts
    options.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    options.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
    options.BodyConsumptionTimeout = TimeSpan.FromSeconds(30);

    // Buffering
    options.BodyBufferThreshold = 65536;        // Request body threshold
    options.ResponseBodyChunkSize = 16384;      // Response chunk size

    // Protocol-specific settings available via options.Http1, options.Http2, options.Http3
    options.Http2.MaxConcurrentStreams = 100;
    options.Http2.MaxFrameSize = 16384;
    options.Http2.MaxHeaderListSize = 8192;

    options.Http3.MaxConcurrentStreams = 100;
    options.Http3.MaxHeaderListSize = 8192;
    options.Http3.EnableWebTransport = false;

    // Endpoints
    options.ListenLocalhost(5000);
});
```

## Next Steps

- [Getting Started](./index) — routing, middleware, and basic patterns
- [Configuration](./configuration) — all TurboServerOptions explained
- [API Reference](/api/) — full public API surface
