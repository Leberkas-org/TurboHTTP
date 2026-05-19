# Middleware Pipeline

TurboHTTP Server implements an ASP.NET Core-style middleware pipeline that allows you to compose request handlers with cross-cutting concerns. Middleware components run in order and can inspect, modify, or short-circuit the request/response flow.

## How Middleware Works

The middleware pipeline is built as a delegate chain. Each middleware receives two parameters:
- **context**: The `TurboHttpContext` containing request, response, and connection details
- **next**: A `TurboRequestDelegate` that invokes the next middleware in the pipeline

Middleware follows a **before/after pattern**: code before `await next(context)` runs on the way in, code after runs on the way out. If you don't call `next()`, the pipeline terminates and no further middleware executes.

```csharp
public delegate Task TurboRequestDelegate(TurboHttpContext context);

public interface ITurboMiddleware
{
    Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next);
}
```

## Inline Middleware

For simple, single-use middleware, use `app.UseTurbo()` with an inline delegate:

```csharp
// Logging middleware
app.UseTurbo(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
    try
    {
        await next(context);
    }
    finally
    {
        stopwatch.Stop();
        Console.WriteLine($"{context.Request.Method} {context.Request.Path} " +
            $"completed in {stopwatch.ElapsedMilliseconds}ms with status {context.Response.StatusCode}");
    }
});
```

```csharp
// CORS headers middleware
app.UseTurbo(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE";
    
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }
    
    await next(context);
});
```

```csharp
// Authorization check
app.UseTurbo(async (context, next) =>
{
    var token = context.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(token))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }
    
    await next(context);
});
```

## Class-Based Middleware

For reusable, complex middleware, implement `ITurboMiddleware`:

```csharp
public class TimingMiddleware : ITurboMiddleware
{
    private readonly ILogger<TimingMiddleware> _logger;
    
    public TimingMiddleware(ILogger<TimingMiddleware> logger)
    {
        _logger = logger;
    }
    
    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Request {Method} {Path} completed in {ElapsedMilliseconds}ms with status {StatusCode}",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds,
                context.Response.StatusCode);
        }
    }
}
```

Register class-based middleware with generic registration:

```csharp
app.UseTurbo<TimingMiddleware>();
```

::: tip
Class-based middleware supports dependency injection. Constructor parameters are resolved from the request service provider.
:::

## Terminal Middleware

Terminal middleware handles all remaining requests and does not call `next()`. Use `app.RunTurbo()` to register a terminal handler:

```csharp
app.RunTurbo(async context =>
{
    if (context.Request.Path == "/health")
    {
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("OK");
    }
    else if (context.Request.Path == "/status")
    {
        context.Response.StatusCode = 200;
        context.Response.Headers["Content-Type"] = "application/json";
        await context.Response.WriteAsync("{\"status\":\"running\"}");
    }
    else
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Not Found");
    }
});
```

::: warning
Only one terminal middleware can be registered. It should be the last `Use` or `Run` call in your pipeline builder, as no middleware registered after it will ever execute.
:::

## Path Branching

Use `app.MapTurbo()` to branch the pipeline based on a path prefix:

```csharp
app.MapTurbo("/api", builder =>
{
    builder.Use(async (context, next) =>
    {
        context.Response.Headers["X-API-Version"] = "1";
        await next(context);
    });
    
    builder.Run(async context =>
    {
        context.Response.StatusCode = 200;
        context.Response.Headers["Content-Type"] = "application/json";
        await context.Response.WriteAsync("{\"message\":\"API response\"}");
    });
});

app.RunTurbo(async context =>
{
    context.Response.StatusCode = 404;
    await context.Response.WriteAsync("Not Found");
});
```

Requests to `/api/users`, `/api/status`, etc. are routed to the `/api` branch. All other requests bypass that branch and continue through the main pipeline.

## Conditional Branching

Use `app.MapTurboWhen()` to branch based on request properties:

```csharp
app.MapTurboWhen(
    predicate: context => context.Request.Headers["User-Agent"].Contains("Mobile"),
    configure: builder =>
    {
        builder.Use(async (context, next) =>
        {
            context.Response.Headers["X-Device-Type"] = "mobile";
            await next(context);
        });
        
        builder.Run(async context =>
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("{\"type\":\"mobile\"}");
        });
    }
);

app.RunTurbo(async context =>
{
    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("{\"type\":\"desktop\"}");
});
```

The predicate is evaluated for each request. If it returns `true`, the branch pipeline executes. Otherwise, execution continues with subsequent middleware.

## Execution Order

Middleware executes in the order it is registered:

```csharp
app.UseTurbo(async (context, next) =>
{
    // Runs first (incoming)
    await next(context);
    // Runs last (outgoing)
});

app.UseTurbo<AuthenticationMiddleware>();
// Runs second (incoming), second-to-last (outgoing)

app.MapTurbo("/admin", builder =>
{
    builder.Run(async context =>
    {
        // Runs third (only for /admin/* requests)
    });
});

app.RunTurbo(async context =>
{
    // Runs last (incoming), first (outgoing)
});
```

The pipeline is built once at startup. Adding middleware after calling `RunTurbo()` has no effect.

## ASP.NET Core Comparison

| Feature | TurboHTTP | ASP.NET Core |
|---------|-----------|--------------|
| Middleware interface | `ITurboMiddleware` | `IMiddleware` |
| Inline delegate | `app.UseTurbo(async (ctx, next) => ...)` | `app.Use(async (ctx, next) => ...)` |
| Class-based registration | `app.UseTurbo<T>()` | `app.UseMiddleware<T>()` |
| Terminal handler | `app.RunTurbo(handler)` | `app.Run(handler)` |
| Path branching | `app.MapTurbo(prefix, builder => ...)` | `app.Map(prefix, app => ...)` |
| Conditional branching | `app.MapTurboWhen(predicate, builder => ...)` | `app.MapWhen(predicate, app => ...)` |
| Context type | `TurboHttpContext` | `HttpContext` |

TurboHTTP middleware follows the same compositional patterns as ASP.NET Core but operates with the TurboHTTP request/response model and is integrated with Akka.Streams backpressure.

## TurboHttpContext

`TurboHttpContext` extends `HttpContext` and provides access to:

- **Request** — HTTP request details (method, path, headers, query string)
- **Response** — HTTP response object for writing status, headers, and body
- **Connection** — Connection metadata (local/remote addresses)
- **RequestAborted** — `CancellationToken` signaling request cancellation
- **TraceIdentifier** — Unique identifier for request tracing
- **User** — Principal from authentication middleware
- **Items** — Request-scoped dictionary for passing data between middleware
- **RequestServices** — Service provider for dependency injection
- **TurboRequest** — TurboHTTP-specific request properties and methods
- **TurboResponse** — TurboHTTP-specific response properties and methods
- **Materializer** — Akka.Streams materialization context

Use `context.Items` to pass data between middleware:

```csharp
app.UseTurbo(async (context, next) =>
{
    context.Items["StartTime"] = DateTime.UtcNow;
    await next(context);
});

app.UseTurbo<TimingMiddleware>(); // Can access context.Items["StartTime"]
```
