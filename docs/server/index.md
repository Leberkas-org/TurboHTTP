# Getting Started with TurboHTTP Server

TurboHTTP Server is a high-performance, standalone HTTP server for .NET built on Akka.Streams. It provides middleware, routing, entity gateway, parameter binding, and actor-based connection lifecycle management — all with zero buffer copies and minimal allocations.

::: tip New to TurboHTTP Server?
See [Installation & Setup](./installation) for NuGet packages and endpoint configuration.
:::

## Quick Start

Add the NuGet package to your ASP.NET Core project:

```bash
dotnet add package TurboHTTP
```

Configure the server in `Program.cs`:

```csharp
using TurboHTTP.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register TurboHTTP Server
builder.Services.AddTurboKestrel(options =>
{
    // Configure HTTP endpoint
    options.ListenLocalhost(5100);
    
    // Configure HTTPS endpoint
    options.ListenLocalhost(5101, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

var app = builder.Build();

// Add middleware — TurboHTTP-style pipeline
app.UseTurbo(async (context, next) =>
{
    context.Response.Headers.Add("X-Powered-By", "TurboHTTP");
    await next(context);
});

// Health check
app.MapTurboGet("/health", () => new { status = "healthy" });

// Simple route
app.MapTurboGet("/", () => "Hello, TurboHTTP!");

// Route group with sub-routes
var api = app.MapTurboGroup("/api/v1");
api.MapGet("/users", GetUsers);
api.MapPost("/users", CreateUser);
api.MapGet("/users/{id}", GetUser);

await app.RunAsync();

// Handlers
static object GetUsers() => new { users = new[] { "Alice", "Bob" } };

static object GetUser(int id) => new { id, name = "User " + id };

static object CreateUser() => new { created = true };
```

Run the server:

```bash
dotnet run
```

Then test with curl:

```bash
# HTTP
curl http://localhost:5100/
curl http://localhost:5100/health

# HTTPS
curl --insecure https://localhost:5101/api/v1/users
curl --insecure https://localhost:5101/api/v1/users/42
```

## Middleware Pipeline

TurboHTTP middleware follows the ASP.NET Core pattern — compose request processing from reusable components:

```csharp
// Inline middleware
app.UseTurbo(async (context, next) =>
{
    // Run before
    context.Response.Headers.Add("X-Request-ID", Guid.NewGuid().ToString());
    await next(context);
    // Run after
    context.Response.Headers.Add("X-Processing-Time", "42ms");
});

// Typed middleware (implements ITurboMiddleware)
app.UseTurbo<LoggingMiddleware>();

// Terminal middleware (does not call next)
app.RunTurbo(context =>
{
    context.Response.StatusCode = 200;
    return context.Response.WriteAsync("Terminal handler\n");
});

// Map to prefix
app.MapTurbo("/debug", builder =>
{
    builder.UseTurbo(async (context, next) =>
    {
        context.Response.Headers.Add("X-Debug", "true");
        await next(context);
    });
    builder.RunTurbo(context => context.Response.WriteAsync("Debug info\n"));
});

// Conditional routing
app.MapTurboWhen(
    context => context.Request.Path.StartsWithSegments("/admin"),
    builder =>
    {
        builder.UseTurbo<AuthenticationMiddleware>();
        builder.MapTurboGet("/dashboard", () => "Admin dashboard");
    });
```

## Routing

Map HTTP methods to handler functions. Handlers can return POCOs (automatically JSON-serialized), strings, or handle the response directly:

```csharp
// GET with no parameters
app.MapTurboGet("/items", () => new[] { "item1", "item2" });

// GET with route parameter
app.MapTurboGet("/items/{id}", (int id) => new { Id = id, Name = $"Item {id}" });

// GET with query parameter
app.MapTurboGet("/search", (string query) => new { Query = query, Results = new object[] { } });

// POST with body
app.MapTurboPost("/items", (ItemRequest req) => new { Created = true, Item = req });

// PUT
app.MapTurboPut("/items/{id}", (int id, ItemRequest req) => new { Updated = true, Id = id });

// DELETE
app.MapTurboDelete("/items/{id}", (int id) => new { Deleted = true, Id = id });

// PATCH
app.MapTurboPatch("/items/{id}", (int id, PatchRequest req) => new { Patched = true });

// Route groups
var api = app.MapTurboGroup("/api");
api.MapTurboGet("/status", () => "OK");

var v1 = api.MapTurboGroup("/v1");
v1.MapTurboGet("/users", GetAllUsers);
v1.MapTurboPost("/users", CreateNewUser);
```

## Entity Gateway

Route directly to stateful Akka.NET actors for entity management. Each entity (e.g. Order, User, Account) gets its own actor, keeping state in memory with automatic persistence:

```csharp
app.MapTurboEntity<int>("/orders/{id}", entity =>
{
    // Inject the resolver (how to spawn/route to the actor)
    entity.UseResolver<OrderEntityResolver>();
    
    // Map HTTP methods to actor messages
    entity.OnGet((int id) => new GetOrder(id));
    entity.OnPost((int id, CreateOrderRequest req) => new CreateOrder(id, req.Items));
    entity.OnPut((int id, UpdateOrderRequest req) => new UpdateOrder(id, req.Status));
    entity.OnDelete((int id) => new CancelOrder(id));
});
```

The `OrderEntityResolver` spawns/locates order actors and handles routing:

```csharp
public class OrderEntityResolver : IEntityResolver<int>
{
    private readonly ActorSystem _system;
    
    public OrderEntityResolver(ActorSystem system)
    {
        _system = system;
    }
    
    public IActorRef<object> ResolveEntity(int orderId)
    {
        // Spawn or look up the actor for this order
        return _system.ActorOf<OrderActor>($"order-{orderId}");
    }
}

public class OrderActor : ReceiveActor
{
    public OrderActor()
    {
        Receive<GetOrder>(msg =>
        {
            Sender.Tell(new OrderResponse { Id = msg.Id, Status = "Confirmed" });
        });
    }
}
```

## Configuration

Configure endpoints, protocols, and certificates:

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // HTTP on localhost
    options.ListenLocalhost(5100);
    
    // HTTPS on specific address
    options.ListenLocalhost(5101, listen =>
    {
        listen.UseHttps();
    });
    
    // HTTPS with certificate
    options.ListenLocalhost(5102, listen =>
    {
        listen.UseHttps("/path/to/cert.pfx", "password");
    });
    
    // Any IPv4 address
    options.ListenAnyIP(5100);
    
    // Specific address
    options.Listen(IPAddress.Any, 5100);
});
```

Or use `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5100"
      },
      "Https": {
        "Url": "https://localhost:5101",
        "Certificate": {
          "Path": "/path/to/cert.pfx",
          "Password": "secret"
        }
      }
    }
  }
}
```

::: tip
The configuration section name `Kestrel` follows ASP.NET Core conventions for familiarity. TurboHTTP reads this section but does not use Kestrel — it's a standalone server.
:::

Then use configuration in `Program.cs`:

```csharp
builder.Services.AddTurboKestrel(builder.Configuration, options =>
{
    // Optional: override with code
});
```

## What's Included

TurboHTTP Server works out of the box with minimal configuration.

| Feature                      | Description                                                                                                      |
|------------------------------|------------------------------------------------------------------------------------------------------------------|
| **Middleware Pipeline**      | ASP.NET Core-style middleware composition with `Use`, `Run`, `Map`, and `MapWhen` for flexible request handling |
| **Routing**                  | Minimal API-style route registration with `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch`                 |
| **Entity Gateway**           | Route HTTP requests directly to stateful Akka.NET actors for per-entity state management                          |
| **Parameter Binding**        | Automatic binding of route parameters, query strings, and request bodies to handler function arguments            |
| **Standalone Server**        | Actor-based HTTP server with TCP/QUIC transport via Servus.Akka.Transport                                         |
| **Actor Lifecycle**          | Supervisor → Listener → Connection actor hierarchy with graceful shutdown and coordinated termination             |

## Next Steps

**Setup:**

- [Installation & Setup](./installation) — NuGet packages, endpoint configuration, DI registration

**Feature guides:**

- [Middleware Pipeline](./middleware) — composition, error handling, CORS, logging
- [Routing & Handlers](./routing) — route parameters, query strings, body binding, route groups
- [Entity Gateway](./entity-gateway) — actors, state management, message routing
- [Configuration](./configuration) — endpoints, protocols, certificates, environment variables
- [Hosting & Deployment](./hosting) — deployment targets, containerization, health checks, graceful shutdown

**Deep dive:**

- [Architecture Overview](/architecture/) — four-layer design, data flow, protocol engines, actor hierarchy
