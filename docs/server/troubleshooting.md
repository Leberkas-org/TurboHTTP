# Troubleshooting

Common issues and solutions when running TurboHTTP Server.

## Server Doesn't Start

### Port Already in Use

```
System.Net.Sockets.SocketException: Address already in use
```

Another process is using the port. Find it with:

```bash
# Windows
netstat -ano | findstr :5000

# Linux/macOS
lsof -i :5000
```

Change the port or stop the conflicting process.

### Missing HTTPS Certificate

```
InvalidOperationException: No server certificate configured for HTTPS endpoint
```

You called `UseHttps()` but didn't provide a certificate. Options:

```csharp
// Use a dev certificate
listen.UseHttps();  // requires dotnet dev-certs https --trust

// Use a file
listen.UseHttps("certs/server.pfx", "password");

// Use a certificate object
listen.UseHttps(myCert);
```

### HTTP/3 Without HTTPS

```
InvalidOperationException: HTTP/3 requires HTTPS
```

QUIC requires TLS. Add `UseHttps()` to the endpoint:

```csharp
options.ListenLocalhost(5000, listen =>
{
    listen.UseHttps();
    listen.Protocols = HttpProtocols.Http3;
});
```

## Connection Issues

### Requests Time Out with 503

TurboHTTP enforces a handler timeout (default 30s). If your handler takes longer:

```csharp
builder.Host.UseTurboHttp(options =>
{
    options.HandlerTimeout = TimeSpan.FromSeconds(120);
    options.HandlerGracePeriod = TimeSpan.FromSeconds(15);
});
```

### Connections Drop Under Load

Check connection limits:

```csharp
options.Limits.MaxConcurrentConnections = 0;  // 0 = unlimited (default)
```

Check HTTP/2 stream limits if clients use multiplexing:

```csharp
options.Http2.MaxConcurrentStreams = 200;  // default 100
```

### Keep-Alive Connections Close Too Soon

Increase the keep-alive timeout:

```csharp
options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(300);
```

## Protocol Negotiation

### HTTP/2 Not Negotiating

HTTP/2 requires ALPN negotiation over TLS. Ensure:

1. The endpoint has `UseHttps()` configured
2. `Protocols` includes `Http2` (default is `Http1AndHttp2`)
3. The client supports HTTP/2 and ALPN

For plaintext HTTP/2 (h2c), the client must send the HTTP/2 connection preface directly.

### HTTP/3 Not Negotiating

HTTP/3 uses QUIC (UDP). Ensure:

1. The endpoint has `UseHttps()` and `Protocols = HttpProtocols.Http3`
2. The OS supports QUIC (Windows 11+, Linux with libmsquic)
3. The client supports HTTP/3
4. No firewall blocks UDP on the configured port

## ActorSystem Configuration

### Using Your Own ActorSystem

If you use Akka.Hosting, TurboHTTP reuses your `ActorSystem`:

```csharp
builder.Services.AddAkka("my-system", configurationBuilder =>
{
    // your config
});

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5000);
});
```

If no `ActorSystem` is registered, TurboHTTP creates one named `turbo-server`.

### Logging

TurboHTTP routes Akka.NET logs through `ILoggerFactory`. To see connection-level logs:

```csharp
options.ListenLocalhost(5000, listen =>
{
    listen.UseConnectionLogging();
});
```

Set the log level in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "TurboHTTP.Server.ConnectionLogging": "Debug"
    }
  }
}
```

## Graceful Shutdown

### Shutdown Hangs

If `StopAsync` doesn't return, a handler may be blocked indefinitely. Reduce the timeout:

```csharp
options.GracefulShutdownTimeout = TimeSpan.FromSeconds(10);
```

After the timeout, connections are forcefully closed.

### In-Flight Requests Get 503

During shutdown, new requests are rejected with 503. This is expected. To minimize impact:

1. Use health checks so load balancers detect the drain
2. Set `GracefulShutdownTimeout` long enough for in-flight requests to complete
