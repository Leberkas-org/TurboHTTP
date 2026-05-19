# Server API

TurboHTTP Server is a standalone HTTP server built on Akka.Streams. Despite the `AddTurboKestrel` method name (kept for configuration familiarity), it uses its own transport layer via Servus.Akka.Transport.

## Registration

```csharp
public static class TurboServerServiceCollectionExtensions
{
    IServiceCollection AddTurboKestrel(
        this IServiceCollection services, 
        Action<TurboServerOptions>? configure = null);
    
    IServiceCollection AddTurboKestrel(
        this IServiceCollection services, 
        IConfiguration configuration, 
        Action<TurboServerOptions>? configure = null);
}
```

Register the server during application setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Simple registration
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

// Configuration-driven registration
builder.Services.AddTurboKestrel(
    builder.Configuration.GetSection("Turbo"),
    options => { /* optional overrides */ });

var app = builder.Build();
app.Run();
```

---

## Server Options

```csharp
public sealed class TurboServerOptions
{
    public int MaxConcurrentConnections { get; set; }
    public int MaxConcurrentUpgradedConnections { get; set; }
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int BodyBufferThreshold { get; set; } = 65536;
    public TimeSpan BodyConsumptionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int ResponseBodyChunkSize { get; set; } = 16384;

    public Http1ServerOptions Http1 { get; }
    public Http2ServerOptions Http2 { get; }
    public Http3ServerOptions Http3 { get; }

    void Listen(IPAddress address, ushort port);
    void Listen(IPAddress address, ushort port, Action<TurboListenOptions> configure);
    void ListenLocalhost(ushort port);
    void ListenLocalhost(ushort port, Action<TurboListenOptions> configure);
    void ListenAnyIP(ushort port);
    void ListenAnyIP(ushort port, Action<TurboListenOptions> configure);
}
```

### General Options

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConcurrentConnections` | System-dependent | Max TCP/QUIC connections at once |
| `MaxConcurrentUpgradedConnections` | System-dependent | Max upgraded connections (e.g., WebSocket) |
| `KeepAliveTimeout` | `120 s` | How long to keep idle connections alive |
| `RequestHeadersTimeout` | `30 s` | Max time to receive complete request headers |
| `GracefulShutdownTimeout` | `30 s` | Max time to drain in-flight requests on shutdown |
| `BodyBufferThreshold` | `65536` (64 KiB) | Buffer limit before switching to streaming |
| `BodyConsumptionTimeout` | `30 s` | Max time to consume request body |
| `ResponseBodyChunkSize` | `16384` (16 KiB) | Chunk size when writing response bodies |

### Listening Configuration

Use helper methods to configure endpoints:

```csharp
options.ListenLocalhost(5100);
options.ListenAnyIP(5100);
options.Listen(IPAddress.Parse("192.168.1.10"), 5100);

// With TLS configuration
options.ListenLocalhost(5443, listen =>
{
    listen.Certificates.Add(
        X509CertificateLoader.LoadPkcs12FromFile("cert.pfx", password));
});
```

---

## HTTP/1.x Options

```csharp
public sealed class Http1ServerOptions
{
    public int MaxRequestLineLength { get; set; } = 8192;
    public int MaxRequestTargetLength { get; set; } = 8192;
    public int MaxPipelinedRequests { get; set; } = 16;
    public int MaxChunkExtensionLength { get; set; } = 4096;
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxRequestLineLength` | `8192` | Max length of request line (method + URI + version) |
| `MaxRequestTargetLength` | `8192` | Max length of request target URI |
| `MaxPipelinedRequests` | `16` | Max concurrent pipelined requests per connection |
| `MaxChunkExtensionLength` | `4096` | Max chunk extension bytes in chunked transfer encoding |
| `BodyReadTimeout` | `30 s` | Max time to read request body |

---

## HTTP/2 Options

```csharp
public sealed class Http2ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialWindowSize { get; set; } = 65535;
    public int MaxFrameSize { get; set; } = 16384;
    public int MaxHeaderListSize { get; set; } = 8192;
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public long MaxResponseBufferSize { get; set; } = 1024 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `InitialWindowSize` | `65535` | Per-stream flow control window |
| `MaxFrameSize` | `16384` (16 KiB) | Max frame payload size |
| `MaxHeaderListSize` | `8192` | Max decompressed header block size |
| `MaxRequestBodySize` | `30 * 1024 * 1024` (30 MB) | Max request body size before rejection |
| `MaxResponseBufferSize` | `1024 * 1024` (1 MB) | Max buffered response data per stream |
| `KeepAliveTimeout` | `130 s` | Idle timeout before PING |
| `RequestHeadersTimeout` | `30 s` | Max time to receive complete headers |
| `MinRequestBodyDataRate` | `240` (bytes/sec) | Minimum acceptable upload rate |
| `MinRequestBodyDataRateGracePeriod` | `5 s` | Grace period before enforcing min rate |

---

## HTTP/3 Options

```csharp
public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int MaxHeaderListSize { get; set; } = 8192;
    public bool EnableWebTransport { get; set; }
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `MaxHeaderListSize` | `8192` | Max decompressed header block size |
| `EnableWebTransport` | `false` | Allow QUIC WebTransport protocol |
| `MaxRequestBodySize` | `30 * 1024 * 1024` (30 MB) | Max request body size before rejection |
| `KeepAliveTimeout` | `130 s` | Idle timeout before PING |
| `RequestHeadersTimeout` | `30 s` | Max time to receive complete headers |
| `MinRequestBodyDataRate` | `240` (bytes/sec) | Minimum acceptable upload rate |
| `MinRequestBodyDataRateGracePeriod` | `5 s` | Grace period before enforcing min rate |

---

## Routing Extensions

Extension methods on `WebApplication`:

```csharp
public static class TurboRoutingExtensions
{
    TurboRouteHandlerBuilder MapTurboGet(
        this WebApplication app, string pattern, Delegate handler);
    
    TurboRouteHandlerBuilder MapTurboPost(
        this WebApplication app, string pattern, Delegate handler);
    
    TurboRouteHandlerBuilder MapTurboPut(
        this WebApplication app, string pattern, Delegate handler);
    
    TurboRouteHandlerBuilder MapTurboDelete(
        this WebApplication app, string pattern, Delegate handler);
    
    TurboRouteHandlerBuilder MapTurboPatch(
        this WebApplication app, string pattern, Delegate handler);
    
    TurboRouteHandlerBuilder MapTurboMethods(
        this WebApplication app, string pattern, IEnumerable<HttpMethod> methods, Delegate handler);
    
    TurboRouteGroupBuilder MapTurboGroup(this WebApplication app, string prefix);
    
    TurboRouteHandlerBuilder MapTurboEntity(
        this WebApplication app, string pattern, Action<TurboEntityBuilder> configure);
    
    TurboRouteHandlerBuilder MapTurboEntity<TActorKey>(
        this WebApplication app, string pattern, Action<TurboEntityBuilder> configure);
}
```

Basic routing:

```csharp
app.MapTurboGet("/users/{id}", async (int id, TurboHttpContext context) =>
{
    return Results.Ok(new { Id = id });
});

app.MapTurboPost("/users", async (TurboHttpContext context) =>
{
    var body = await context.Request.BodyReader.ReadAsync();
    return Results.Created("/users/123", null);
});

// Custom methods
app.MapTurboMethods("/events", [HttpMethod.Patch, HttpMethod.Delete], handler);
```

---

## Route Handler Builder

```csharp
public sealed class TurboRouteHandlerBuilder
{
    TurboRouteHandlerBuilder WithName(string name);
    TurboRouteHandlerBuilder WithTags(params string[] tags);
    TurboRouteHandlerBuilder WithMetadata(params object[] metadata);
    TurboRouteHandlerBuilder RequireAuthorization();
    TurboRouteHandlerBuilder AllowAnonymous();
    TurboRouteHandlerBuilder Produces<T>(int statusCode = 200);
    TurboRouteHandlerBuilder ProducesProblem(int statusCode = 500);
}
```

Chain builder methods for metadata and OpenAPI documentation:

```csharp
app.MapTurboGet("/users/{id}", handler)
    .WithName("GetUser")
    .WithTags("users")
    .Produces<User>(200)
    .ProducesProblem(404)
    .RequireAuthorization();
```

---

## Route Group Builder

Groups routes under a common prefix. Methods inside the group **do not** include the `Turbo` prefix:

```csharp
public sealed class TurboRouteGroupBuilder
{
    TurboRouteHandlerBuilder MapGet(string pattern, Delegate handler);
    TurboRouteHandlerBuilder MapPost(string pattern, Delegate handler);
    TurboRouteHandlerBuilder MapPut(string pattern, Delegate handler);
    TurboRouteHandlerBuilder MapDelete(string pattern, Delegate handler);
    TurboRouteHandlerBuilder MapPatch(string pattern, Delegate handler);
    TurboRouteHandlerBuilder MapMethods(string pattern, IEnumerable<HttpMethod> methods, Delegate handler);
    TurboRouteGroupBuilder MapGroup(string prefix);
    TurboRouteHandlerBuilder MapEntity(string pattern, Action<TurboEntityBuilder> configure);
    TurboRouteHandlerBuilder MapEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure);
}
```

Example:

```csharp
var api = app.MapTurboGroup("/api");

api.MapGet("/users", listUsers);
api.MapPost("/users", createUser);
api.MapGet("/users/{id}", getUser);

// Nested groups
var v2 = api.MapGroup("/v2");
v2.MapGet("/status", status);
```

---

## Middleware

Middleware components implement `ITurboMiddleware`:

```csharp
public interface ITurboMiddleware
{
    Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next);
}

public delegate Task TurboRequestDelegate(TurboHttpContext context);
```

Register middleware via `UseTurbo`:

```csharp
// Built-in middleware
public static class TurboMiddlewareExtensions
{
    WebApplication UseTurbo<T>(this WebApplication app) where T : ITurboMiddleware;
    WebApplication UseTurbo(this WebApplication app, Func<TurboHttpContext, TurboRequestDelegate, Task> middleware);
}
```

Example middleware:

```csharp
public class RequestLoggingMiddleware : ITurboMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        _logger.LogInformation("{Method} {Path}", 
            context.Request.Method, context.Request.Path);
        
        await next(context);
        
        _logger.LogInformation("Response: {StatusCode}", context.Response.StatusCode);
    }
}

// Register it
app.UseTurbo<RequestLoggingMiddleware>();

// Or inline
app.UseTurbo(async (context, next) =>
{
    Console.WriteLine($"{context.Request.Method} {context.Request.Path}");
    await next(context);
});
```

---

## TurboHttpContext

`TurboHttpContext` extends ASP.NET Core's `HttpContext` and adds Akka.Streams-specific properties:

```csharp
public sealed class TurboHttpContext : HttpContext
{
    public TurboHttpRequest TurboRequest { get; }
    public TurboHttpResponse TurboResponse { get; }
    public IMaterializer Materializer { get; }
}
```

### TurboRequest

Provides stream-based request body access:

```csharp
// Access the request as a byte stream
var reader = context.TurboRequest.BodySource;
await foreach (var bytes in reader.ReadAllAsync(cancellationToken))
{
    // Process streaming body chunks
}
```

### TurboResponse

Provides stream-based response body writing:

```csharp
context.Response.StatusCode = 200;
context.Response.ContentType = "text/plain";

var writer = context.TurboResponse.BodyWriter;
await writer.WriteAsync(new Memory<byte>(utf8Bytes), cancellationToken);
await writer.CompleteAsync();
```

### Materializer

The Akka.Streams materializer running in the context of this request. Useful for running stream graphs:

```csharp
var source = Source.From(items);
var sink = Sink.Aggregate<T, List<T>>(new List<T>(), (list, item) =>
{
    list.Add(item);
    return list;
});

var result = await source.RunWith(sink, context.Materializer);
```
