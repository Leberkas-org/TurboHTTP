# Extending the Server Pipeline

The TurboHTTP server is designed to be extended at multiple points: custom middleware, path-based branching, custom routing, and actor-based entity handling.

::: warning Prerequisites
This page assumes familiarity with [Akka.NET](https://getakka.net/), async/await, and ASP.NET Core middleware concepts. For a quick introduction to the server architecture, start with [Server Request Pipeline](/architecture/server-pipeline).
:::

---

## Custom Middleware

Middleware is the primary extension point. Each middleware receives the `TurboHttpContext`, can inspect or modify the request, and can short-circuit the chain or transform the response.

### Implementing `ITurboMiddleware`

```csharp
public sealed class LoggingMiddleware : ITurboMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger) => _logger = logger;

    public async Task InvokeAsync(TurboHttpContext context, TurboMiddlewareDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("→ {Method} {Path}", context.Request.Method, context.Request.Path);

        try
        {
            await next(context);
            stopwatch.Stop();
            _logger.LogInformation("← {StatusCode} ({ElapsedMs}ms)",
                context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "✗ {Method} {Path} ({ElapsedMs}ms)",
                context.Request.Method, context.Request.Path, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
```

### Registering Middleware

Middleware must be registered in `IServiceCollection` and added to the TurboHTTP app:

```csharp
// In Program.cs
services.AddScoped<LoggingMiddleware>();
services.AddScoped<AuthenticationMiddleware>();

var app = builder.Build();

app.UseTurbo<LoggingMiddleware>()
   .UseTurbo<AuthenticationMiddleware>()
   .MapTurboGet("/health", ctx => ctx.Response.StatusCode = 200);
```

Middleware executes in the order you call `UseTurbo<T>()` — outermost first for requests, innermost first for responses.

### Short-Circuiting

Return without calling `next(context)` to short-circuit the chain:

```csharp
public async Task InvokeAsync(TurboHttpContext context, TurboMiddlewareDelegate next)
{
    if (!AuthorizeRequest(context))
    {
        context.Response.StatusCode = 401;
        context.Response.ReasonPhrase = "Unauthorized";
        return;
    }

    await next(context);
}
```

---

## Path-Based Branching

Use `MapTurbo()` to register a sub-application at a path prefix, and `MapTurboWhen()` for predicate-based branching.

### Branch by Path Prefix

```csharp
app.MapTurbo("/api", api =>
{
    api.UseTurbo<ApiAuthMiddleware>()
       .MapTurboGet("/users/{id}", GetUser)
       .MapTurboPost("/users", CreateUser)
       .MapTurboDelete("/users/{id}", DeleteUser);
});

app.MapTurbo("/admin", admin =>
{
    admin.UseTurbo<AdminOnlyMiddleware>()
         .MapTurboGet("/stats", GetStats)
         .MapTurboPost("/config", UpdateConfig);
});

// Root-level routes
app.MapTurboGet("/health", ctx => ctx.Response.StatusCode = 200);
```

Each branch has its own middleware pipeline — middleware registered in `/api` does not affect `/admin`.

### Branch by Predicate

```csharp
// Internal requests (X-Internal header)
app.MapTurboWhen(
    ctx => ctx.Request.Headers.ContainsKey("X-Internal"),
    internal_ =>
    {
        internal_.UseTurbo<InternalMiddleware>()
                 .MapTurboGet("/debug/state", GetDebugState);
    });

// External requests (default)
app.MapTurboWhen(
    ctx => !ctx.Request.Headers.ContainsKey("X-Internal"),
    external =>
    {
        external.UseTurbo<RateLimitMiddleware>()
                .MapTurboGet("/public", GetPublic);
    });
```

---

## Route Groups

For cleaner organization when registering many routes, use `MapTurboGroup()`:

```csharp
app.MapTurboGroup("/api/v1", group =>
{
    group.MapGet("/users", ListUsers);           // GET /api/v1/users
    group.MapPost("/users", CreateUser);         // POST /api/v1/users
    group.MapGet("/users/{id}", GetUser);        // GET /api/v1/users/{id}
    group.MapPut("/users/{id}", UpdateUser);     // PUT /api/v1/users/{id}
    group.MapDelete("/users/{id}", DeleteUser);  // DELETE /api/v1/users/{id}
});

app.MapTurboGroup("/api/v2", group =>
{
    group.MapGet("/products", ListProducts);     // GET /api/v2/products
    // ...
});
```

::: tip
Inside `MapTurboGroup()`, use `MapGet`, `MapPost`, etc. (no "Turbo" prefix). The `MapTurbo*` prefix is only for `WebApplication` extension methods.
:::

---

## Entity Gateway Extension

For actor-based entity handling, implement `IEntityActorResolver`:

```csharp
public sealed class UserActorResolver : IEntityActorResolver
{
    public Task<IActorRef> ResolveAsync(string entityType, string entityId, IActorRefFactory factory)
    {
        if (entityType != "user")
            throw new InvalidOperationException($"Unknown entity type: {entityType}");

        var actorName = $"user-{entityId}";
        return Task.FromResult(factory.ActorOf(Props.Create(() => new UserActor(entityId)), actorName));
    }
}
```

Register it in DI:

```csharp
services.AddScoped<IEntityActorResolver, UserActorResolver>();
```

Then dispatch to actors via `MapTurboEntity()`:

```csharp
app.MapTurboEntity<int>("/users/{id}", entity =>
{
    entity.UseResolver<UserActorResolver>();
    entity.OnGet((int id) => new GetUser(id));
    entity.OnPost((int id, CreateUserRequest req) => new CreateUser(id, req.Name));
    entity.OnDelete((int id) => new DeleteUser(id));
});
```

The dispatcher automatically:
1. Resolves the actor using the configured resolver
2. Sends the mapped message to the actor
3. Waits for the response
4. Sends the response back to the client

---

## Request/Response Transformation

Middleware can transform requests and responses:

### Request Transformation

```csharp
public sealed class TenantMiddleware : ITurboMiddleware
{
    public async Task InvokeAsync(TurboHttpContext context, TurboMiddlewareDelegate next)
    {
        var tenantId = context.Request.Headers["X-Tenant-ID"]?.ToString() ?? "default";
        context.Request.Headers["X-Tenant-ID"] = tenantId;
        context.RouteValues["tenantId"] = tenantId;

        await next(context);
    }
}
```

### Response Transformation

```csharp
public sealed class SecurityHeadersMiddleware : ITurboMiddleware
{
    public async Task InvokeAsync(TurboHttpContext context, TurboMiddlewareDelegate next)
    {
        await next(context);

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    }
}
```

---

## Error Handling

Middleware can catch exceptions and generate error responses:

```csharp
public sealed class ExceptionMiddleware : ITurboMiddleware
{
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(ILogger<ExceptionMiddleware> logger) => _logger = logger;

    public async Task InvokeAsync(TurboHttpContext context, TurboMiddlewareDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found");
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        }
    }
}
```

Register it as the outermost middleware to catch all exceptions:

```csharp
app.UseTurbo<ExceptionMiddleware>()
   .UseTurbo<LoggingMiddleware>()
   .UseTurbo<AuthenticationMiddleware>();
```

---

## Related Guides

- [Middleware Pipeline](/server/middleware) — full middleware reference
- [Routing](/server/routing) — route registration and parameter binding
- [Entity Gateway](/server/entity-gateway) — actor-based entity patterns
- [Server Request Pipeline](/architecture/server-pipeline) — how requests flow through stages
