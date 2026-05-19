# Server Quick Start

Build a working TurboHTTP server in under 5 minutes.

::: tip Standalone Server
TurboHTTP Server is a fully standalone HTTP server built on Akka.Streams with its own TCP/QUIC transport (Servus.Akka.Transport). The `AddTurboKestrel` method name is a configuration convention — it does not use Kestrel.
:::

## 1. Install

```bash
dotnet add package TurboHTTP
```

## 2. Configure the Server

```csharp
using TurboHTTP.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
```

## 3. Add Routes

```csharp
app.MapTurboGet("/health", () => new { status = "healthy" });

app.MapTurboGet("/users/{id}", (int id) => new { id, name = "User " + id });

app.MapTurboPost("/users", (CreateUserRequest req) =>
    new { created = true, name = req.Name });

app.MapTurboDelete("/users/{id}", (int id) => new { deleted = true, id });

await app.RunAsync();

public sealed record CreateUserRequest(string Name, string Email);
```

## 4. Add Middleware

```csharp
app.UseTurbo(async (context, next) =>
{
    Console.WriteLine($"[{context.Request.Method}] {context.Request.Path}");
    await next(context);
});
```

## 5. Route Groups

```csharp
var api = app.MapTurboGroup("/api/v1");
api.MapGet("/users", () => new[] { "Alice", "Bob" });
api.MapPost("/users", (CreateUserRequest req) => new { created = true });
```

::: warning Route Group Methods
Inside a route group, use `MapGet`, `MapPost`, etc. (no "Turbo" prefix). The `MapTurbo*` prefix is only on `WebApplication` extension methods.
:::

## 6. Test It

```bash
curl http://localhost:5100/health
curl http://localhost:5100/users/42
curl -X POST http://localhost:5100/users -H "Content-Type: application/json" -d '{"name":"Alice","email":"alice@example.com"}'
```

## Server Architecture

TurboHTTP Server uses an actor hierarchy for connection management:

```
ServerSupervisorActor
├── ListenerActor (endpoint :5100)
│   ├── ConnectionActor (client A)
│   └── ConnectionActor (client B)
└── ListenerActor (endpoint :5101)
    └── ConnectionActor (client C)
```

Each connection gets its own actor, and protocol engines (HTTP/1.0, 1.1, 2, 3) are selected via ALPN negotiation.

## Next Steps

- [Installation & Setup](/server/installation) — endpoints, HTTPS, protocols
- [Middleware Pipeline](/server/middleware) — composition, error handling
- [Routing](/server/routing) — parameters, binding, groups
- [Entity Gateway](/server/entity-gateway) — actor-based stateful handling
- [Real-World Scenarios](/server/scenarios) — combined feature examples
