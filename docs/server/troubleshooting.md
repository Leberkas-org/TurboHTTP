# Troubleshooting

This guide covers common issues when running TurboHTTP Server and practical debugging techniques to resolve them.

## Server Won't Start

### Port Already in Use

If you see an error that the port is already in use, either change the port your server listens on or stop the conflicting process.

Check what's using a port:
```powershell
# Windows
netstat -ano | findstr :8080

# Linux/macOS
lsof -i :8080
```

Change your server configuration:
```csharp
app.ListenTcp(
    port: 8081,  // Change from 8080 to 8081
    host: "127.0.0.1"
);
```

### Missing AddTurboKestrel

The TurboHTTP Server integration must be explicitly registered before building the app:

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

// This is required
builder.Services.AddTurboKestrel();

var app = builder.Build();
```

Without calling `AddTurboKestrel()`, the Listen/ListenLocalhost/ListenAnyIP methods won't be available and the app will fail to build.

### No Endpoints Configured

At least one endpoint must be configured when starting the server:

```csharp
var app = builder.Build();

// At least one of these is required:
app.ListenTcp(8080, "127.0.0.1");
app.ListenTcp(8443, "127.0.0.1", "cert.pfx", "password");

app.Run();
```

Without any endpoints, the server has no address to bind to and cannot start.

## HTTPS Errors

### Certificate Not Found

Verify the certificate file path is correct and the file exists:

```csharp
var certPath = Path.Combine(Directory.GetCurrentDirectory(), "certs", "cert.pfx");
if (!File.Exists(certPath))
{
    throw new FileNotFoundException($"Certificate not found: {certPath}");
}

app.ListenTcp(8443, "127.0.0.1", certPath, "password");
```

Use absolute paths to avoid ambiguity about the working directory.

### Wrong Certificate Password

Ensure the password matches the certificate:

```csharp
// Verify password by attempting to load the certificate
var cert = new X509Certificate2(certPath, "password");
```

If you receive a password error, regenerate the certificate or verify the password used when creating it.

### Certificate Expired

Check the certificate expiration date:

```csharp
var cert = new X509Certificate2(certPath, "password");
Console.WriteLine($"Valid from: {cert.NotBefore}");
Console.WriteLine($"Valid until: {cert.NotAfter}");
Console.WriteLine($"Is expired: {DateTime.UtcNow > cert.NotAfter}");
```

Renew the certificate and update the path in your configuration.

## Routes Not Matching

### Pattern Syntax

Route patterns use the format `{param:type}` for parameters:

```csharp
// Correct
app.MapGet("/users/{id:int}", async (int id, TurboHttpContext ctx) =>
{
    await ctx.Response.WriteAsync($"User {id}");
});

// Incorrect - parameter name only won't work
app.MapGet("/users/{id}", async (int id, TurboHttpContext ctx) =>
{
    // This won't match because type is missing
});
```

Supported types: `int`, `long`, `float`, `double`, `bool`, `guid`, `string` (default).

### Group Prefix

Routes within a route group use the full path combining group prefix and route pattern:

```csharp
app.MapGroup("/api")
   .MapGet("/users", ...);  // Full path: /api/users
   .MapPost("/users", ...); // Full path: /api/users
```

Verify the complete path when testing endpoints.

### Trailing Slash Sensitivity

Routes are matched exactly, including trailing slashes:

```csharp
app.MapGet("/users", ...);  // Matches /users only

// This will NOT match:
// /users/ (extra slash)
// /Users (different case)
```

When linking to endpoints or testing, ensure the path exactly matches the route definition.

## Middleware Not Executing

### Registration Order Matters

Middleware runs in the order it is registered. Register middleware before the route handlers that depend on it:

```csharp
var app = builder.Build();

// Correct: authentication before route handlers
app.UseAuthentication();
app.UseRouting();
app.MapGet("/protected", ...) // Can check User from context

// Incorrect: authentication after routes won't protect them
app.MapGet("/protected", ...);
app.UseAuthentication();
```

::: tip
Use the middleware order to implement cross-cutting concerns like logging, authentication, and error handling consistently.
:::

### Missing await next(context)

Middleware must call the next delegate to continue the pipeline:

```csharp
app.Use(async (context, next) =>
{
    // Do something before
    await next(context);  // REQUIRED - calls next middleware
    // Do something after
});
```

If you don't call `await next(context)`, the pipeline stops and subsequent middleware won't run.

### Terminal Middleware

The `RunTurbo()` method is terminal and stops the pipeline:

```csharp
app.MapGet("/hello", ...);
app.RunTurbo();  // After this, no more middleware can run
app.MapGet("/world", ...);  // This never executes
```

Place terminal middleware last in the configuration.

## Entity Gateway Timeouts

### Actor Not Responding

If the entity gateway times out trying to reach an actor, the actor may not be alive or configured correctly.

Enable Akka logging to see actor lifecycle events:

```xml
<configuration>
  <akka>
    <logger>
      <logLevel>DEBUG</logLevel>
    </logger>
  </akka>
</configuration>
```

Check that:
- The actor system is running
- The actor reference is correct
- Message types match what the actor expects

### Wrong Resolver

Ensure the resolver matches your actor setup. Two common patterns:

```csharp
// Child-per-entity: creates one actor per entity ID
services.AddEntityGateway(options =>
{
    options.EntityResolver = new ChildPerEntityResolver("handlers");
});

// Registry: requires registering entities in advance
services.AddEntityGateway(options =>
{
    options.EntityResolver = new RegistryResolver(actorSystem, registry);
});
```

Mixing resolvers or misconfiguring the entity paths causes timeout errors.

::: warning
The resolver must match how you set up your actors. Verify the actor creation strategy and entity naming.
:::

### Timeout Too Short

The default timeout may be too short for slow operations. Increase if needed:

```csharp
var defaultTimeout = TimeSpan.FromSeconds(30);
gateway.SendAsync(entityId, message, defaultTimeout);
```

But consider fixing the underlying slow operation rather than just increasing the timeout.

## Connection Limits

### MaxConcurrentConnections

Limit the number of concurrent connections to protect server resources:

```csharp
app.ListenTcp(8080, "127.0.0.1", options =>
{
    options.MaxConcurrentConnections = 1000;  // Limit connections
    // Set to 0 for unlimited (not recommended for production)
});
```

If you consistently hit the connection limit, increase it or investigate whether clients are properly closing connections.

### HTTP/2 Stream Limits

Each HTTP/2 connection can have a configurable maximum number of concurrent streams:

```csharp
var options = new Http2ProtocolOptions
{
    MaxConcurrentStreams = 100
};
```

If clients are opening many streams on a single connection, increase this value. Too low a limit causes stream reset errors.

## Shutdown Issues

### Long-Running Requests

Graceful shutdown gives in-flight requests a timeout to complete. If requests take longer than the timeout, they're forcefully closed.

Increase the shutdown timeout:

```csharp
var host = app.Build();

await host.StopAsync(timeout: TimeSpan.FromSeconds(60));  // 60 seconds
```

::: tip
Monitor request duration in production. If you frequently exceed the shutdown timeout, consider implementing request queuing or prioritization.
:::

### Body Not Consumed

If response bodies are not fully consumed by clients, the server may hold connections open longer than necessary.

Enable body consumption timeout:

```csharp
app.ListenTcp(8080, "127.0.0.1", options =>
{
    options.BodyConsumptionTimeout = TimeSpan.FromSeconds(10);
});
```

This forces connection closure if the client doesn't consume the body within the timeout.

## Debugging Tips

### Enable Akka Logging

Set the Akka log level to DEBUG or INFO to see detailed actor activity:

```json
{
  "akka": {
    "loggers": ["Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog"],
    "loglevel": "DEBUG",
    "actor": {
      "debug": {
        "receive": true,
        "lifecycle": true
      }
    }
  }
}
```

This helps diagnose actor creation, message delivery, and lifecycle issues.

### Use Middleware for Logging

Add a logging middleware to inspect requests and responses:

```csharp
app.Use(async (context, next) =>
{
    var startTime = DateTime.UtcNow;
    
    await next(context);
    
    var elapsed = DateTime.UtcNow - startTime;
    Console.WriteLine($"{context.Request.Method} {context.Request.Path} - {context.Response.StatusCode} ({elapsed.TotalMilliseconds}ms)");
});
```

This provides visibility into the request/response lifecycle without code changes.

### Check ConnectionCompletionReason

When a connection closes abnormally, the completion reason provides details:

```csharp
// In your actor or middleware
if (connection.CompletionReason is not null)
{
    Console.WriteLine($"Connection closed: {connection.CompletionReason}");
}
```

Possible reasons include:
- Client closed normally
- Read/write timeout
- Protocol error
- Graceful shutdown
- Resource limits exceeded

Use this to diagnose whether issues are client-side, server-side, or environmental.
