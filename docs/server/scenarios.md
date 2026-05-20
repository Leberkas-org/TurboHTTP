# Real-World Server Scenarios

TurboHTTP Server combines routing, middleware, validation, and entity gateway to handle practical application patterns. This page walks through four common scenarios with complete, runnable examples.

## Scenario 1: REST API with Validation and Entity Gateway

Build a REST API with Content-Type validation middleware, health endpoints, and entity-based order management using the actor gateway.

**Use this when you have**:
- Stateful domain entities (orders, users, accounts)
- Need to validate request structure before routing
- Want actor-based message handling with typed responses

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using TurboHTTP.Hosting;

// Domain types
public sealed class OrderId;

public record CreateOrderRequest(
    [Required][StringLength(50)] string CustomerId,
    [Range(1, int.MaxValue)] decimal Amount,
    [Required] string[] ItemIds);

public record UpdateOrderRequest(
    [Required] string Status);

// Messages
public sealed record GetOrder(string Id);
public sealed record CreateOrder(string CustomerId, decimal Amount, string[] ItemIds);
public sealed record UpdateOrder(string Id, string Status);
public sealed record CancelOrder(string Id);

// Responses
public sealed record OrderResponse(string Id, string CustomerId, string Status, decimal Amount);
public sealed record NotFoundResponse();
public sealed record ValidationFailedResponse(string[] Errors);

// Order actor — handles all order state
public sealed class OrderActor : ReceiveActor
{
    private readonly Dictionary<string, (string CustomerId, string Status, decimal Amount)> _orders = new();

    public OrderActor()
    {
        Receive<GetOrder>(Handle);
        Receive<CreateOrder>(Handle);
        Receive<UpdateOrder>(Handle);
        Receive<CancelOrder>(Handle);
    }

    private void Handle(GetOrder msg)
    {
        if (!_orders.TryGetValue(msg.Id, out var order))
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }

        var (customerId, status, amount) = order;
        Sender.Tell(new OrderResponse(msg.Id, customerId, status, amount));
    }

    private void Handle(CreateOrder msg)
    {
        var orderId = Guid.NewGuid().ToString();
        _orders[orderId] = (msg.CustomerId, "pending", msg.Amount);
        Sender.Tell(new OrderResponse(orderId, msg.CustomerId, "pending", msg.Amount));
    }

    private void Handle(UpdateOrder msg)
    {
        if (!_orders.TryGetValue(msg.Id, out var order))
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }

        var (customerId, _, amount) = order;
        _orders[msg.Id] = (customerId, msg.Status, amount);
        Sender.Tell(new OrderResponse(msg.Id, customerId, msg.Status, amount));
    }

    private void Handle(CancelOrder msg)
    {
        if (!_orders.ContainsKey(msg.Id))
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }

        _orders.Remove(msg.Id);
        Sender.Tell(new OrderResponse(msg.Id, "", "cancelled", 0));
    }
}

// Validation middleware — ensures Content-Type is JSON for POST/PUT
public sealed class ContentTypeValidationMiddleware : ITurboMiddleware
{
    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        if ((context.Request.Method == "POST" || context.Request.Method == "PUT") &&
            !context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Content-Type must be application/json" });
            return;
        }

        await next(context);
    }
}

// Startup
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

// Add Akka with OrderActor
builder.Services.AddAkka("order-system", cfg =>
{
    cfg.StartActors((system, registry) =>
    {
        var orderRef = system.ActorOf(Props.Create<OrderActor>(), "order-manager");
        registry.Register<OrderId>(orderRef);
    });
});

var app = builder.Build();

// Validation middleware on all routes
app.UseTurbo<ContentTypeValidationMiddleware>();

// Health endpoint (outside validation path)
app.MapTurboGet("/health", () => new { status = "healthy" });

// Entity gateway for orders
app.MapTurboEntity<int>("/orders/{id}", entity =>
{
    entity.UseResolver<OrderEntityResolver>();

    entity.OnGet((int id) => new GetOrder(id))
        .WithTimeout(TimeSpan.FromSeconds(10));

    entity.OnPost((int id, CreateOrderRequest req) =>
        new CreateOrder(req.CustomerId, req.Amount, req.ItemIds))
        .WithTimeout(TimeSpan.FromSeconds(5));

    entity.OnPut((int id, UpdateOrderRequest req) =>
        new UpdateOrder(id, req.Status))
        .WithTimeout(TimeSpan.FromSeconds(5));

    entity.OnDelete((int id) => new CancelOrder(id))
        .WithTimeout(TimeSpan.FromSeconds(5));

    // Response mappers
    entity.MapResponse<OrderResponse>((ctx, resp) =>
    {
        ctx.Response.StatusCode = 200;
        return ctx.Response.WriteAsJsonAsync(resp);
    });

    entity.MapResponse<NotFoundResponse>((ctx, _) =>
    {
        ctx.Response.StatusCode = 404;
        return ctx.Response.WriteAsJsonAsync(new { error = "Order not found" });
    });
});

await app.RunAsync();
```

**Test with curl**:

```bash
# Health check (no validation)
curl http://localhost:5100/health

# Create order (validation applies)
curl -X POST http://localhost:5100/orders/order-1 \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "cust-123",
    "amount": 99.99,
    "itemIds": ["item-1", "item-2"]
  }'

# Get order
curl http://localhost:5100/orders/order-1

# Update order status
curl -X PUT http://localhost:5100/orders/order-1 \
  -H "Content-Type: application/json" \
  -d '{"status": "shipped"}'

# Cancel order
curl -X DELETE http://localhost:5100/orders/order-1

# 404 on unknown order
curl http://localhost:5100/orders/unknown
```

**Key points**:
- Validation middleware enforces Content-Type before routing reaches handlers
- Entity gateway routes HTTP methods to actor messages automatically
- Multiple `MapResponse<T>` handlers map different actor response types to HTTP status codes
- Per-method `WithTimeout()` differentiates fast reads (10s) from slower writes (5s)
- Actors hold all state; handlers are stateless

---

## Scenario 2: Middleware Pipeline — Logging + Auth + CORS

Compose three middleware layers to measure request time, enforce authentication on restricted paths, and allow cross-origin requests.

**Use this when you have**:
- Need to measure or log all requests
- Some endpoints require authentication, others are public
- Need CORS support for browser clients

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using TurboHTTP.Hosting;

// Timing middleware — measures request duration
public sealed class TimingMiddleware : ITurboMiddleware
{
    private readonly ILogger<TimingMiddleware> _logger;

    public TimingMiddleware(ILogger<TimingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        
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

// CORS middleware — adds cross-origin headers
public sealed class CorsMiddleware : ITurboMiddleware
{
    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, PATCH, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

        // Respond to preflight requests
        if (context.Request.Method == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            return;
        }

        await next(context);
    }
}

// Authorization middleware — validates bearer token
public sealed class AuthorizationMiddleware : ITurboMiddleware
{
    public async Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next)
    {
        var token = context.Request.Headers["Authorization"]
            .FirstOrDefault()?
            .Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token) || !ValidateToken(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        await next(context);
    }

    private static bool ValidateToken(string token)
    {
        // Demo: accept any token starting with "valid-"
        return token.StartsWith("valid-");
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTurboKestrel(options => options.ListenLocalhost(5100));

var app = builder.Build();

// Layer 1: Timing (outermost — measures all requests)
app.UseTurbo<TimingMiddleware>();

// Layer 2: CORS (applies to all routes)
app.UseTurbo<CorsMiddleware>();

// Public routes (no auth required)
app.MapTurboGet("/", () => new { message = "Public endpoint" });
app.MapTurboGet("/health", () => new { status = "healthy" });

// Layer 3: Protected routes (auth required)
// Execution order: Timing → CORS → Auth → Handler
app.MapTurboWhen(
    predicate: context => context.Request.Path.StartsWithSegments("/api"),
    configure: builder =>
    {
        builder.UseTurbo<AuthorizationMiddleware>();

        var api = builder.MapTurboGroup("/api");
        api.MapGet("/users", () => new { users = new[] { "Alice", "Bob" } });
        api.MapGet("/users/{id}", (int id) => new { id, name = $"User {id}" });
        api.MapPost("/users", (object req) => new { created = true });
    });

await app.RunAsync();
```

**Test with curl**:

```bash
# Public endpoints (no auth needed)
curl http://localhost:5100/
curl http://localhost:5100/health

# Protected endpoint without auth — returns 401
curl http://localhost:5100/api/users

# Protected endpoint with valid token — returns 200
curl -H "Authorization: Bearer valid-mytoken" \
  http://localhost:5100/api/users

# Preflight CORS request
curl -X OPTIONS http://localhost:5100/api/users \
  -H "Origin: http://example.com"

# Invalid token — returns 401
curl -H "Authorization: Bearer invalid-token" \
  http://localhost:5100/api/users
```

**Key points**:
- Middleware executes in registration order: Timing (outer) → CORS → Auth → Handler
- Code before `await next()` runs on the way in; code after runs on the way out
- `MapTurboWhen()` protects only `/api/*` routes with auth; public routes bypass it
- CORS middleware runs for all requests, including OPTIONS preflight
- Timing middleware measures total time including nested middleware and handler

---

## Scenario 3: Actor-Based CQRS

Separate read and write paths using distinct message types and actor handlers. Each path can have different timeouts, response types, and business logic.

**Use this when you have**:
- Read-heavy workloads where queries are fast but commands are slow
- Need different response types for reads vs. writes
- Want to audit or log commands separately from queries

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Hosting;

// Domain identifier
public sealed class UserId;

// CQRS message types — separate queries and commands
public sealed record GetUserQuery(string UserId);
public sealed record CreateUserCommand(string UserId, string Name, string Email);
public sealed record UpdateUserCommand(string UserId, string Name);

// Response types — different shapes for read vs. write
public sealed record UserReadModel(string UserId, string Name, string Email, DateTime CreatedAt);
public sealed record UserWriteResult(string UserId, string Message);
public sealed record UserNotFound(string UserId);

// CQRS actor — handles both queries and commands
public sealed class UserActor : ReceiveActor
{
    private readonly Dictionary<string, (string Name, string Email, DateTime CreatedAt)> _users = new();

    public UserActor()
    {
        Receive<GetUserQuery>(Handle);
        Receive<CreateUserCommand>(Handle);
        Receive<UpdateUserCommand>(Handle);
    }

    private void Handle(GetUserQuery query)
    {
        // Query path — fast read, no side effects
        if (_users.TryGetValue(query.UserId, out var user))
        {
            Sender.Tell(new UserReadModel(query.UserId, user.Name, user.Email, user.CreatedAt));
        }
        else
        {
            Sender.Tell(new UserNotFound(query.UserId));
        }
    }

    private void Handle(CreateUserCommand cmd)
    {
        // Command path — write operation, returns write result
        if (_users.ContainsKey(cmd.UserId))
        {
            Sender.Tell(new UserNotFound(cmd.UserId)); // Or custom DuplicateUserResponse
            return;
        }

        _users[cmd.UserId] = (cmd.Name, cmd.Email, DateTime.UtcNow);
        Sender.Tell(new UserWriteResult(cmd.UserId, "User created successfully"));
    }

    private void Handle(UpdateUserCommand cmd)
    {
        // Command path — update operation
        if (!_users.TryGetValue(cmd.UserId, out var user))
        {
            Sender.Tell(new UserNotFound(cmd.UserId));
            return;
        }

        _users[cmd.UserId] = (cmd.Name, user.Email, user.CreatedAt);
        Sender.Tell(new UserWriteResult(cmd.UserId, "User updated successfully"));
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

builder.Services.AddAkka("cqrs-system", cfg =>
{
    cfg.StartActors((system, registry) =>
    {
        var userRef = system.ActorOf(Props.Create<UserActor>(), "user-aggregate");
        registry.Register<UserId>(userRef);
    });
});

var app = builder.Build();

// CQRS entity gateway — same actor, different message paths
app.MapTurboEntity<int>("/users/{id}", entity =>
{
    entity.UseActorRef<UserActor>();

    // Read path (query)
    entity.OnGet((int id) => new GetUserQuery(id));

    // Write path (commands)
    entity.OnPost((int id, CreateUserRequest req) =>
        new CreateUserCommand(id, req.Name, req.Email));

    entity.OnPut((int id, UpdateUserRequest req) =>
        new UpdateUserCommand(id, req.Name));
});

await app.RunAsync();

// DTOs
public record CreateUserRequest(string Name, string Email);
public record UpdateUserRequest(string Name);
```

**Test with curl**:

```bash
# Create user (command — 5s timeout)
curl -X POST http://localhost:5100/users/user-1 \
  -H "Content-Type: application/json" \
  -d '{"name": "Alice", "email": "alice@example.com"}'

# Get user (query — 30s timeout, returns UserReadModel)
curl http://localhost:5100/users/user-1

# Update user (command — 5s timeout, returns UserWriteResult)
curl -X PUT http://localhost:5100/users/user-1 \
  -H "Content-Type: application/json" \
  -d '{"name": "Alice Smith"}'

# Get unknown user (returns UserNotFound mapped to 404)
curl http://localhost:5100/users/unknown
```

**Key points**:
- Separate message types (`GetUserQuery`, `CreateUserCommand`) define read vs. write paths
- Query path uses generous timeout (30s) for potentially expensive reads
- Command path uses tight timeout (5s) to fail fast if writes hang
- Different response types (`UserReadModel` vs. `UserWriteResult`) are mapped independently
- Same actor receives all messages; message handlers separate business logic per operation

---

## Scenario 4: Multi-Protocol Endpoint

Listen on multiple ports with different protocol combinations: plaintext HTTP/1.1 on port 5100, and HTTPS HTTP/1.1+HTTP/2 on port 5101. Tune performance per protocol.

**Use this when you have**:
- Need to support both plaintext and encrypted endpoints
- Want HTTP/2 multiplexing only on secure connections
- Need to tune connection and stream limits separately

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Authentication;
using TurboHTTP.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    // HTTP/1.1 plaintext — suitable for internal APIs or load balancer backends
    options.ListenAnyIP(5100, listen =>
    {
        listen.Protocols = HttpProtocols.Http1;
    });

    // HTTPS HTTP/1.1 + HTTP/2 — production endpoint with protocol negotiation
    options.ListenLocalhost(5101, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps();  // Auto-discover certificate or use development cert
    });

    // Server-wide limits
    options.MaxConcurrentConnections = 1000;
    options.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    options.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);

    // HTTP/1.1 tuning
    options.Http1.MaxRequestLineLength = 8 * 1024;
    options.Http1.MaxPipelinedRequests = 16;
    options.Http1.BodyReadTimeout = TimeSpan.FromSeconds(30);

    // HTTP/2 tuning (only applies to HTTPS endpoint)
    options.Http2.MaxConcurrentStreams = 100;           // Limit parallel streams per connection
    options.Http2.InitialWindowSize = 64 * 1024;         // Per-stream flow control window (64 KiB)
    options.Http2.MaxFrameSize = 16 * 1024;              // Max frame payload (16 KiB)
    options.Http2.MaxHeaderListSize = 8 * 1024;          // Decompressed header block limit (8 KiB)
    options.Http2.MaxRequestBodySize = 30 * 1024 * 1024; // Per-request body limit (30 MiB)
    options.Http2.MinRequestBodyDataRate = 240;          // Slowloris protection: bytes/sec
    options.Http2.MinRequestBodyDataRateGracePeriod = TimeSpan.FromSeconds(5);

    // HTTPS defaults (applies to all endpoints with .UseHttps())
    options.ConfigureHttpsDefaults(https =>
    {
        https.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        https.HandshakeTimeout = TimeSpan.FromSeconds(10);
    });
});

var app = builder.Build();

// Routes work on both endpoints
app.MapTurboGet("/", () => new { message = "Hello, TurboHTTP" });
app.MapTurboGet("/health", () => new { status = "healthy" });

// API endpoint
var api = app.MapTurboGroup("/api");
api.MapGet("/status", () => new { uptime = "100%" });
api.MapPost("/submit", (object body) => new { received = true });

// Stream a large response (tests HTTP/2 frame buffering)
app.MapTurboGet("/stream", async context =>
{
    context.Response.ContentType = "application/json";
    context.Response.Headers["Transfer-Encoding"] = "chunked";

    for (int i = 0; i < 1000; i++)
    {
        await context.Response.WriteAsync($"{{\"chunk\":{i}}}\n");
    }
});

await app.RunAsync();
```

**Test with curl**:

```bash
# HTTP/1.1 plaintext (port 5100)
curl http://localhost:5100/
curl http://localhost:5100/health

# Measure HTTP/1.1 connection overhead (slow, no multiplexing)
curl -w "Time: %{time_total}s\n" http://localhost:5100/api/status

# HTTPS — HTTP/1.1 (port 5101, no multiplexing)
curl --insecure https://localhost:5101/

# HTTPS — request protocol version
curl -I --insecure https://localhost:5101/health

# Test HTTP/2 multiplexing (requires h2 or nghttp2)
# Install: `brew install nghttp2` or `apt-get install nghttp2-client`
nghttp2 -v https://localhost:5101/

# Stream test (shows frame buffering behavior)
curl --insecure https://localhost:5101/stream | head -20

# Concurrent requests on HTTP/2 (multiplexed in single connection)
# This is much faster on HTTP/2 than HTTP/1.1 due to multiplexing
for i in {1..10}; do
  curl --insecure https://localhost:5101/api/status &
done
wait
```

**Key points**:
- Port 5100 (plaintext HTTP/1.1) is suitable for internal APIs or behind a reverse proxy
- Port 5101 (HTTPS HTTP/1.1+HTTP/2) uses ALPN to negotiate protocol at TLS handshake
- HTTP/2 tuning applies only to the HTTPS endpoint (HTTP/1.1 doesn't use these options)
- `MaxConcurrentStreams = 100` prevents excessive parallelism per connection
- `MinRequestBodyDataRate = 240 bytes/sec` protects against slowloris attacks on HTTP/2
- `GracefulShutdownTimeout` allows inflight requests to complete before server exits

---

## Common Patterns

### Request Context Isolation

Use `context.Items` to pass data between middleware and handlers:

```csharp
app.UseTurbo(async (context, next) =>
{
    context.Items["RequestId"] = Guid.NewGuid().ToString();
    context.Items["StartTime"] = DateTime.UtcNow;
    await next(context);
});

app.MapTurboGet("/trace", context =>
{
    var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";
    return context.Response.WriteAsJsonAsync(new { requestId });
});
```

### Entity Gateway with Fire-and-Forget

Use `AcceptedResponse()` to return 202 Accepted immediately without waiting for the actor:

```csharp
entity.OnPost((int id, CreateOrderRequest req) => new PlaceOrder(id, req.Amount))
    .AcceptedResponse();  // Returns 202 immediately, actor processes async
```

### Per-Handler Error Mapping

Map different error types in the same entity route:

```csharp
entity.MapResponse<ValidationError>((ctx, err) =>
{
    ctx.Response.StatusCode = 400;
    return ctx.Response.WriteAsJsonAsync(new { errors = err.Messages });
});

entity.MapResponse<NotFoundError>((ctx, _) =>
{
    ctx.Response.StatusCode = 404;
    return Task.CompletedTask;
});

entity.MapResponse<TimeoutError>((ctx, _) =>
{
    ctx.Response.StatusCode = 504;
    return ctx.Response.WriteAsJsonAsync(new { error = "Request timeout" });
});
```

---

## See Also

- [Entity Gateway](./entity-gateway) — detailed actor routing and resolver patterns
- [Middleware Pipeline](./middleware) — composition, ordering, and error handling
- [Configuration](./configuration) — endpoint binding and protocol tuning
- [Validation](./validation) — automatic parameter validation with data annotations
