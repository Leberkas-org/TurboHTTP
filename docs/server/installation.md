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

## Register the Server

TurboHTTP registers as an `IServer` implementation, replacing Kestrel:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5000);
});

var app = builder.Build();
app.MapGet("/", () => "Hello from TurboHTTP!");
await app.RunAsync();
```

`UseTurboHttp()` removes any existing `IServer` registration (including Kestrel) and registers `TurboServer`. After this call, your application uses standard ASP.NET Core — `app.MapGet`, `app.Use`, controllers, minimal APIs.

::: tip
`ListenLocalhost(5000)` binds to `127.0.0.1:5000`. Use `ListenAnyIP(5000)` to listen on all IPv4 addresses.
:::

## Side-by-Side with Kestrel

The only change from a standard Kestrel setup is replacing the server registration:

```csharp
// Before (Kestrel — default)
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Hello");
await app.RunAsync();

// After (TurboHTTP)
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTurboHttp(options =>        // <-- this is the only change
{
    options.ListenLocalhost(5000);
});
var app = builder.Build();
app.MapGet("/", () => "Hello");
await app.RunAsync();
```

Everything else — middleware, routing, DI, configuration — stays exactly the same.

## Endpoint Configuration

### Multiple Endpoints

```csharp
builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5000);
    options.ListenAnyIP(8080);
    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();
    });
});
```

### Explicit Address Binding

```csharp
using System.Net;

builder.Host.UseTurboHttp(options =>
{
    options.Listen(IPAddress.Loopback, 5000);
    options.Listen(IPAddress.Parse("192.168.1.100"), 8080);
});
```

### Protocol Selection

Endpoints default to HTTP/1.1 and HTTP/2. Override per endpoint:

```csharp
builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5000, listen =>
    {
        listen.Protocols = HttpProtocols.Http1;
    });

    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });

    options.ListenLocalhost(5002, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http3;
    });
});
```

| Protocol | Value | Transport | Notes |
|----------|-------|-----------|-------|
| `Http1` | HTTP/1.1 only | TCP | Maximum compatibility |
| `Http2` | HTTP/2 only | TCP | Multiplexing, HPACK compression |
| `Http1AndHttp2` | Both (default) | TCP | ALPN negotiation selects protocol |
| `Http3` | HTTP/3 | QUIC (UDP) | Requires HTTPS |

::: warning
HTTP/3 requires HTTPS. Configuring `Http3` without `UseHttps()` throws at startup.
:::

## HTTPS Configuration

### Dev Certificate

```csharp
options.ListenLocalhost(5001, listen =>
{
    listen.UseHttps();
});
```

### Certificate from File

```csharp
options.ListenLocalhost(443, listen =>
{
    listen.UseHttps("certs/server.pfx", "password123");
});
```

PEM certificates are also supported:

```csharp
options.ListenLocalhost(443, listen =>
{
    listen.UseHttps("certs/server.pem");
});
```

### Certificate Object

```csharp
var cert = X509CertificateLoader.LoadPkcs12FromFile("certs/server.pfx", "password123");

options.ListenLocalhost(443, listen =>
{
    listen.UseHttps(cert);
});
```

### HTTPS Defaults

Apply settings to all HTTPS endpoints:

```csharp
builder.Host.UseTurboHttp(options =>
{
    options.ConfigureHttpsDefaults(https =>
    {
        https.EnabledSslProtocols = SslProtocols.Tls13;
        https.HandshakeTimeout = TimeSpan.FromSeconds(15);
    });

    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();  // inherits defaults
    });
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServerCertificate` | `X509Certificate2?` | null | Certificate object |
| `CertificatePath` | `string?` | null | Path to .pfx or .pem file |
| `CertificatePassword` | `string?` | null | Certificate password |
| `EnabledSslProtocols` | `SslProtocols` | `None` (OS default) | Allowed TLS versions |
| `HandshakeTimeout` | `TimeSpan` | 10 seconds | TLS handshake timeout |
| `ClientCertificateMode` | `ClientCertificateMode` | `NoCertificate` | Client certificate requirement |
| `ClientCertificateValidationCallback` | `RemoteCertificateValidationCallback?` | null | Custom client cert validation |
| `ServerCertificateSelector` | `Func<string?, X509Certificate2?>?` | null | SNI-based certificate selection |

### Connection Logging

Enable per-connection logging for debugging:

```csharp
options.ListenLocalhost(5000, listen =>
{
    listen.UseConnectionLogging();
});
```

## Configuration from appsettings.json

TurboHTTP reads endpoint configuration from the `TurboHTTP` section:

```json
{
  "TurboHTTP": {
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

Endpoint names (`Http`, `Https`) are arbitrary — use meaningful names for your setup.

## Next Steps

- [Configuration](./configuration) — all server options in detail
- [Using with ASP.NET Core](./aspnet-core) — middleware, routing, DI patterns
- [Hosting & Lifecycle](./hosting) — actor hierarchy, graceful shutdown
