# TurboClientOptions

```csharp
public sealed class TurboClientOptions
{
    // Base address
    public Uri? BaseAddress { get; set; }

    // Version-specific options (nested)
    public Http1Options Http1 { get; init; } = new();    // HTTP/1.x settings
    public Http2Options Http2 { get; init; } = new();    // HTTP/2 settings
    public Http3Options Http3 { get; init; } = new();    // HTTP/3 settings

    // Body buffering
    public long MaxBufferedBodySize { get; set; } = 4 * 1024 * 1024;  // 4 MiB
    public long? MaxStreamedBodySize { get; set; }                      // unlimited

    // Connection pool
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan PooledConnectionLifetime { get; set; } = Timeout.InfiniteTimeSpan;
    public uint MaxEndpointSubstreams { get; set; } = 256;

    // TLS
    public bool DangerousAcceptAnyServerCertificate { get; set; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }
    public X509CertificateCollection? ClientCertificates { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    // Socket options
    public int? SocketSendBufferSize { get; set; }
    public int? SocketReceiveBufferSize { get; set; }

    // Proxy
    public bool UseProxy { get; set; } = true;
    public IWebProxy? Proxy { get; set; }
    public ICredentials? DefaultProxyCredentials { get; set; }

    // Authentication
    public ICredentials? Credentials { get; set; }
    public bool PreAuthenticate { get; set; }
}
```

## Connection Options

| Property | Default | Description |
|----------|---------|-------------|
| `BaseAddress` | `null` | Base URI for relative requests |
| `ConnectTimeout` | `15 s` | TCP/QUIC connection timeout |
| `PooledConnectionIdleTimeout` | `90 s` | How long idle connections are kept in the pool |
| `PooledConnectionLifetime` | `infinite` | Maximum lifetime of a pooled connection |
| `MaxEndpointSubstreams` | `256` | Max concurrently active endpoint substreams |

Per-version connection limits are configured on the nested options objects:

| Property | Default | Description |
|----------|---------|-------------|
| `Http1.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/1.x connections per host |
| `Http1.MaxPipelineDepth` | `16` | Max pipelined requests per HTTP/1.1 connection |
| `Http2.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/2 connections per host |
| `Http2.MaxConcurrentStreams` | `100` | Max concurrent streams per HTTP/2 connection |
| `Http3.MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |

See [Connection Pooling guide](/client/connection-pooling) for pool lifecycle details.

## HTTP/1.x Options

```csharp
public sealed class Http1Options
{
    public int MaxConnectionsPerServer { get; set; } = 6;
    public int MaxPipelineDepth { get; set; } = 16;
    public int MaxResponseHeadersLength { get; set; } = 64;  // KB
    public bool AutoHost { get; set; } = true;
    public bool AutoAcceptEncoding { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 3;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `MaxPipelineDepth` | `16` | Max pipelined requests per connection |
| `MaxResponseHeadersLength` | `64` (KB) | Max response header size |
| `AutoHost` | `true` | Automatically inject `Host` header |
| `AutoAcceptEncoding` | `true` | Automatically inject `Accept-Encoding` header |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |

## HTTP/2 Options

```csharp
public sealed class Http2Options
{
    public int MaxConnectionsPerServer { get; set; } = 6;
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialConnectionWindowSize { get; set; } = 64 * 1024 * 1024;  // 64 MB
    public int InitialStreamWindowSize { get; set; } = 2 * 1024 * 1024;        // 2 MB
    public int MaxFrameSize { get; set; } = 64 * 1024;                        // 64 KB
    public int HeaderTableSize { get; set; } = 64 * 1024;                     // 64 KB
    public int MaxReconnectAttempts { get; set; } = 3;
    public TimeSpan KeepAlivePingDelay { get; set; } = Timeout.InfiniteTimeSpan;
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public HttpKeepAlivePingPolicy KeepAlivePingPolicy { get; set; } = HttpKeepAlivePingPolicy.Always;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `InitialConnectionWindowSize` | `64 * 1024 * 1024` (64 MB) | Connection-level flow control window |
| `InitialStreamWindowSize` | `2 * 1024 * 1024` (2 MB) | Per-stream flow control window |
| `MaxFrameSize` | `64 * 1024` (64 KB) | Max frame payload size |
| `HeaderTableSize` | `64 * 1024` (64 KB) | HPACK dynamic table size |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `KeepAlivePingDelay` | `infinite` | Delay before sending keep-alive PING |
| `KeepAlivePingTimeout` | `20 s` | Timeout for PING acknowledgment |
| `KeepAlivePingPolicy` | `Always` | When to send keep-alive PINGs |

### Adjusting Frame Size

```csharp
// Increase frame size for large binary payloads (default: 64 KiB, max: 16 MiB)
options.Http2.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB
```

See [HTTP/2 & Multiplexing guide](/client/http2) for multiplexing configuration.

## HTTP/3 Options

```csharp
public sealed class Http3Options
{
    public int MaxConnectionsPerServer { get; set; } = 4;
    public int MaxConcurrentStreams { get; set; } = 100;
    public int QpackMaxTableCapacity { get; set; } = 16 * 1024;  // 16 KB
    public int QpackBlockedStreams { get; set; } = 100;
    public int MaxFieldSectionSize { get; set; } = 64 * 1024;    // 64 KB
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxReconnectAttempts { get; set; } = 3;
    public bool AllowConnectionMigration { get; set; } = true;
    public bool EnableAltSvcDiscovery { get; set; }
    public int MaxReconnectBufferSize { get; set; } = 64;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `QpackMaxTableCapacity` | `16 * 1024` (16 KB) | QPACK dynamic table size |
| `QpackBlockedStreams` | `100` | Max streams blocked waiting for QPACK |
| `MaxFieldSectionSize` | `64 * 1024` (64 KB) | Max header block size |
| `IdleTimeout` | `30 s` | QUIC idle timeout |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `AllowConnectionMigration` | `true` | Allow QUIC connection migration |
| `EnableAltSvcDiscovery` | `false` | Auto-discover HTTP/3 via Alt-Svc headers |
| `MaxReconnectBufferSize` | `64` | Max datagram buffers during reconnection |

See [HTTP/3 & QUIC guide](/client/http3) for QUIC-specific settings.

## TLS Options

| Property | Default | Description |
|----------|---------|-------------|
| `DangerousAcceptAnyServerCertificate` | `false` | Skip all certificate validation — dev/test only |
| `ServerCertificateValidationCallback` | Accept valid certs | Custom TLS certificate validation |
| `ClientCertificates` | `null` | Client certificates for mutual TLS |
| `EnabledSslProtocols` | `SslProtocols.None` (OS default) | TLS protocol versions to permit |

```csharp
// Mutual TLS with a client certificate
options.ClientCertificates = new X509CertificateCollection
{
    X509CertificateLoader.LoadPkcs12FromFile("client.pfx", password)
};
```

## Socket Options

| Property | Default | Description |
|----------|---------|-------------|
| `SocketSendBufferSize` | `null` (system default) | OS socket send buffer size in bytes |
| `SocketReceiveBufferSize` | `null` (system default) | OS socket receive buffer size in bytes |

## Proxy Options

| Property | Default | Description |
|----------|---------|-------------|
| `UseProxy` | `true` | Use system proxy settings |
| `Proxy` | `null` | Custom proxy URI |
| `DefaultProxyCredentials` | `null` | Credentials for proxy authentication |

## Authentication Options

| Property | Default | Description |
|----------|---------|-------------|
| `Credentials` | `null` | Credentials for HTTP authentication (Basic, Digest, etc.) |
| `PreAuthenticate` | `false` | Send credentials proactively before receiving a challenge |

## Body Buffering Options

| Property | Default | Description |
|----------|---------|-------------|
| `MaxBufferedBodySize` | `4 * 1024 * 1024` (4 MiB) | Max response body size before buffering fails |
| `MaxStreamedBodySize` | `null` (unlimited) | Max body size for streamed (unbuffered) consumption |

::: tip
For large file downloads or uploads, use `MaxStreamedBodySize` to handle bodies larger than `MaxBufferedBodySize` without buffering the entire response in memory.
:::
