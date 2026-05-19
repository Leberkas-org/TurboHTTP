# Configuration

TurboHTTP Server exposes all configuration through `TurboServerOptions` — connection limits, timeouts, buffer thresholds, and protocol-specific settings. Configuration is code-first and applies when you call `AddTurboKestrel()`.

## General Options

`TurboServerOptions` controls server-wide behavior across all connections and protocols.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| MaxConcurrentConnections | int | 0 (unlimited) | Maximum number of connections allowed. 0 = no limit. |
| MaxConcurrentUpgradedConnections | int | 0 (unlimited) | Maximum number of upgraded connections (WebSocket, etc.). 0 = no limit. |
| KeepAliveTimeout | TimeSpan | 120s | How long to keep idle connections alive. |
| RequestHeadersTimeout | TimeSpan | 30s | Maximum time to receive request headers before timeout. |
| GracefulShutdownTimeout | TimeSpan | 30s | Time to gracefully shut down active connections. |
| BodyBufferThreshold | int | 65536 (64 KiB) | Buffer size for request bodies before streaming to application. |
| BodyConsumptionTimeout | TimeSpan | 30s | Maximum time the application has to consume the request body. |
| ResponseBodyChunkSize | int | 16384 (16 KiB) | Size of chunks when sending response bodies over the network. |
| Http1 | Http1ServerOptions | (see below) | HTTP/1.x-specific options. |
| Http2 | Http2ServerOptions | (see below) | HTTP/2-specific options. |
| Http3 | Http3ServerOptions | (see below) | HTTP/3-specific options. |

## HTTP/1.x Options

Controls HTTP/1.0 and HTTP/1.1 behavior. Access via `options.Http1`.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| MaxRequestLineLength | int | 8192 | Maximum length of request line (method + target + version). |
| MaxRequestTargetLength | int | 8192 | Maximum length of request target (URI). Limits attack surface for malformed targets. |
| MaxPipelinedRequests | int | 16 | Maximum number of requests allowed in a pipeline (HTTP/1.1 pipelining). |
| MaxChunkExtensionLength | int | 4096 | Maximum length of chunk extensions in chunked transfer encoding. |
| BodyReadTimeout | TimeSpan | 30s | Time limit for reading request body data. |

**Example: Increase request line limits for APIs with very long URLs**

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.Http1.MaxRequestLineLength = 16384;  // 16 KiB instead of 8 KiB
    options.Http1.MaxRequestTargetLength = 16384;
});
```

## HTTP/2 Options

Controls HTTP/2 (RFC 9113) behavior. Access via `options.Http2`.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| MaxConcurrentStreams | int | 100 | Maximum number of concurrent streams per connection. |
| InitialWindowSize | int | 65535 | Initial flow-control window size (bytes) for each stream. |
| MaxFrameSize | int | 16384 | Maximum payload size for HTTP/2 frames. |
| MaxHeaderListSize | int | 8192 | Maximum size of the decompressed header block. |
| MaxRequestBodySize | long | 30 * 1024 * 1024 (30 MiB) | Maximum size of a single request body. |
| MaxResponseBufferSize | long | 1024 * 1024 (1 MiB) | Maximum size of buffered response data before backpressure. |
| KeepAliveTimeout | TimeSpan | 130s | How long to wait on idle HTTP/2 connections (before sending PING). |
| RequestHeadersTimeout | TimeSpan | 30s | Time to receive request headers. |
| MinRequestBodyDataRate | int | 240 | Minimum bytes-per-second data rate for request body (slowloris protection). |
| MinRequestBodyDataRateGracePeriod | TimeSpan | 5s | Grace period before enforcing minimum data rate. |

**Example: Lower stream limits for more conservative memory usage**

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.Http2.MaxConcurrentStreams = 50;      // Reduce from 100
    options.Http2.MaxResponseBufferSize = 512 * 1024;  // 512 KiB instead of 1 MiB
});
```

## HTTP/3 Options

Controls HTTP/3 (RFC 9114, QUIC) behavior. Access via `options.Http3`.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| MaxConcurrentStreams | int | 100 | Maximum number of concurrent streams per connection. |
| MaxHeaderListSize | int | 8192 | Maximum size of the decompressed header block. |
| EnableWebTransport | bool | false | Enable experimental WebTransport support (unidirectional streams). |
| MaxRequestBodySize | long | 30 * 1024 * 1024 (30 MiB) | Maximum size of a single request body. |
| KeepAliveTimeout | TimeSpan | 130s | How long to keep idle QUIC connections alive. |
| RequestHeadersTimeout | TimeSpan | 30s | Time to receive request headers. |
| MinRequestBodyDataRate | int | 240 | Minimum bytes-per-second data rate for request body (slowloris protection). |
| MinRequestBodyDataRateGracePeriod | TimeSpan | 5s | Grace period before enforcing minimum data rate. |

**Example: Enable WebTransport for bidirectional communication**

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.Http3.EnableWebTransport = true;
});
```

## Endpoint Configuration

Configure IP addresses, ports, and HTTPS settings with `TurboListenOptions`.

### Listen Methods

Use one of these on `TurboServerOptions`:

```csharp
// Listen on specific address and port
options.Listen(IPAddress.Loopback, 5100);

// Listen on localhost (shorthand)
options.ListenLocalhost(5100);

// Listen on any IPv4 address (shorthand)
options.ListenAnyIP(5100);

// Listen on specific address with configuration
options.Listen(IPAddress.Any, 5100, listen =>
{
    listen.Protocols = HttpProtocols.Http1AndHttp2;
    listen.UseHttps("/path/to/cert.pfx", "password");
});

// Listen with shorthand + configuration
options.ListenLocalhost(5101, listen =>
{
    listen.Protocols = HttpProtocols.Http2;
    listen.UseHttps();  // Auto-discover certificate
});
```

### TurboListenOptions Properties

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| Address | IPAddress | (constructor param) | IP address to listen on (e.g. `IPAddress.Any`, `IPAddress.Loopback`). |
| Port | ushort | (constructor param) | TCP/UDP port number (e.g. 80, 443, 5100). |
| Protocols | HttpProtocols | Http1AndHttp2 | Which protocols to support on this endpoint. |

### HTTPS Configuration

Enable HTTPS with one of the `UseHttps()` overloads:

```csharp
// Auto-discover certificate from system store
listen.UseHttps();

// Use X509Certificate2 directly
var cert = new X509Certificate2("/path/to/cert.pfx", "password");
listen.UseHttps(cert);

// Load certificate from file
listen.UseHttps("/path/to/cert.pfx", "password");

// Load certificate with additional options
listen.UseHttps(cert, opts =>
{
    opts.EnabledSslProtocols = SslProtocols.Tls13;
    opts.HandshakeTimeout = TimeSpan.FromSeconds(15);
});
```

Set HTTPS defaults for all endpoints via `ConfigureHttpsDefaults()`:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Defaults apply to all endpoints unless overridden
    options.ConfigureHttpsDefaults(https =>
    {
        https.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        https.HandshakeTimeout = TimeSpan.FromSeconds(10);
    });
    
    // This endpoint uses the defaults above
    options.ListenLocalhost(5101, listen => listen.UseHttps());
});
```

## HTTPS Options

Control SSL/TLS behavior with `TurboHttpsOptions`.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| ServerCertificate | X509Certificate2? | null | The certificate to use (if set, overrides CertificatePath). |
| CertificatePath | string? | null | Path to certificate file (.pfx, .pem, etc.). |
| CertificatePassword | string? | null | Password for encrypted certificate files. |
| EnabledSslProtocols | SslProtocols | None (system default) | Which TLS versions to allow (e.g. Tls12, Tls13). |
| ClientCertificateValidationCallback | RemoteCertificateValidationCallback? | null | Custom validation for client certificates (mTLS). |
| HandshakeTimeout | TimeSpan | 10s | Time limit for TLS handshake to complete. |

**Example: Require TLS 1.3 with strict client certificate validation**

```csharp
options.ListenLocalhost(5443, listen =>
{
    listen.UseHttps(cert, https =>
    {
        https.EnabledSslProtocols = SslProtocols.Tls13;
        https.HandshakeTimeout = TimeSpan.FromSeconds(5);
        https.ClientCertificateValidationCallback = (chain, cert, policy, errors) =>
        {
            // Custom validation logic
            return errors == System.Net.Security.SslPolicyErrors.None;
        };
    });
});
```

## Protocol Selection

Use `HttpProtocols` flag enum to specify which protocols each endpoint supports.

| Flag | Value | Purpose |
|------|-------|---------|
| Http1 | 1 | HTTP/1.0 and HTTP/1.1 only. |
| Http2 | 2 | HTTP/2 only. |
| Http1AndHttp2 | 3 | HTTP/1.1 and HTTP/2 (both over TLS, negotiated via ALPN). |
| Http3 | 4 | HTTP/3 over QUIC only. |
| None | 0 | No protocols (not useful — use for clearing flags). |

Protocols are negotiated at connection time via ALPN (Application Layer Protocol Negotiation).

**Example: Mixed protocol endpoints**

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // HTTP/1 only (unencrypted)
    options.ListenAnyIP(80, listen =>
    {
        listen.Protocols = HttpProtocols.Http1;
    });
    
    // HTTP/1 + HTTP/2 (TLS, ALPN selects at handshake)
    options.ListenLocalhost(443, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps(cert);
    });
    
    // HTTP/3 only (QUIC)
    options.ListenLocalhost(443, listen =>
    {
        listen.Protocols = HttpProtocols.Http3;
        listen.UseHttps(cert);
    });
});
```

## Complete Configuration Example

Here's a full configuration combining multiple options:

```csharp
using TurboHTTP.Hosting;
using System.Net;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    // General limits
    options.MaxConcurrentConnections = 1000;
    options.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    options.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
    
    // Buffer strategy
    options.BodyBufferThreshold = 64 * 1024;  // 64 KiB
    options.ResponseBodyChunkSize = 16 * 1024;  // 16 KiB
    
    // HTTP/1.x tuning
    options.Http1.MaxRequestLineLength = 8192;
    options.Http1.MaxPipelinedRequests = 16;
    
    // HTTP/2 tuning
    options.Http2.MaxConcurrentStreams = 100;
    options.Http2.MaxRequestBodySize = 30 * 1024 * 1024;  // 30 MiB
    options.Http2.MinRequestBodyDataRate = 240;  // bytes/sec (slowloris protection)
    
    // HTTP/3 tuning
    options.Http3.MaxConcurrentStreams = 100;
    options.Http3.MaxRequestBodySize = 30 * 1024 * 1024;  // 30 MiB
    
    // HTTPS defaults
    options.ConfigureHttpsDefaults(https =>
    {
        https.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        https.HandshakeTimeout = TimeSpan.FromSeconds(10);
    });
    
    // HTTP endpoint (localhost)
    options.ListenLocalhost(5100);
    
    // HTTPS endpoint (HTTP/1 + HTTP/2)
    options.ListenLocalhost(5101, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps("/path/to/cert.pfx", "password");
    });
    
    // HTTP/3 endpoint (QUIC)
    options.ListenLocalhost(5102, listen =>
    {
        listen.Protocols = HttpProtocols.Http3;
        listen.UseHttps("/path/to/cert.pfx", "password");
    });
    
    // Any IP + HTTP/2
    options.ListenAnyIP(8080, listen =>
    {
        listen.Protocols = HttpProtocols.Http2;
        listen.UseHttps();
    });
});

var app = builder.Build();
await app.RunAsync();
```

## Configuration via appsettings.json

You can also configure endpoints through `appsettings.json` and bind them to `TurboServerOptions`:

```json
{
  "Kestrel": {
    "Limits": {
      "MaxConcurrentConnections": 1000,
      "KeepAliveTimeout": "00:02:00",
      "RequestHeadersTimeout": "00:00:30"
    },
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5100",
        "Protocols": "Http1"
      },
      "Https": {
        "Url": "https://localhost:5101",
        "Protocols": "Http1AndHttp2",
        "Certificate": {
          "Path": "/path/to/cert.pfx",
          "Password": "secret"
        }
      }
    }
  }
}
```

Then load in `Program.cs`:

```csharp
builder.Services.AddTurboKestrel(builder.Configuration, options =>
{
    // Optional: override with code
    options.Http2.MaxConcurrentStreams = 50;
});
```

## Performance Tuning

### Connection Limits

Start conservative and increase based on load testing:

- **MaxConcurrentConnections**: Set to 2-4× your expected peak connection count (accounts for slow clients, connection drains).
- **MaxConcurrentUpgradedConnections**: For WebSocket or HTTP/2 servers, typically 10-50% of total connections (they're heavier).

### Body Buffers

Tune based on typical request sizes:

- **BodyBufferThreshold**: Increase for APIs that expect large JSON payloads; decrease for mostly small requests.
- **ResponseBodyChunkSize**: Larger chunks (32 KiB+) for high-bandwidth scenarios; smaller (8 KiB) for many concurrent slow clients.

### Timeouts

Balance resource cleanup against slow clients:

- **KeepAliveTimeout**: Shorter (30-60s) for APIs with many clients; longer (2-5m) for long-lived connections.
- **RequestHeadersTimeout**: Short (5-10s) for untrusted clients; longer (30s+) for slow networks.
- **BodyConsumptionTimeout**: Match your application's processing speed.

### Slowloris Protection

HTTP/2 and HTTP/3 include **MinRequestBodyDataRate** (default 240 bytes/sec) to prevent slowloris attacks:

```csharp
options.Http2.MinRequestBodyDataRate = 240;  // bytes/sec
options.Http2.MinRequestBodyDataRateGracePeriod = TimeSpan.FromSeconds(5);
```

This ensures slow-sending clients are eventually disconnected, freeing resources.

## See Also

- [Installation & Setup](./installation) — NuGet packages and Kestrel integration
- [Hosting & Deployment](./hosting) — health checks, graceful shutdown, containerization
- [Architecture Overview](/architecture/) — protocol engines and data flow
