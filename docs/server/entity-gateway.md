# Entity Gateway

The Entity Gateway bridges HTTP requests to Akka actors, enabling actor-based request handling within TurboHTTP. Instead of returning immediate responses, route handlers send messages to actors and map the actor's response back to HTTP. This pattern is ideal for stateful entities, CQRS architectures, and event-driven systems.

Entity routes are registered using `MapTurboEntity<TKey>()`, where `TKey` identifies the entity type. Each route automatically extracts an entity key from the URL, resolves the corresponding actor, and dispatches the request message to that actor.

## When to Use Entity Gateway

- **Stateful entities** — each entity key maps to a persistent actor maintaining state
- **CQRS** — separate read/write models with command handlers returning events or aggregate state
- **Event sourcing** — entities that accumulate events and answer queries about their history
- **Fire-and-forget workflows** — accepting a command and returning 202 Accepted without waiting for completion
- **Resilient request handling** — actors can retry, timeout, and handle failures gracefully

## Basic Setup

Register entity routes using `MapTurboEntity<TKey>()` on the `WebApplication` instance:

```csharp
var builder = WebApplication.CreateBuilder();
builder.Services.AddTurboKestrel();
var app = builder.Build();

app.MapTurboEntity<int>("/orders/{id}", entity =>
{
    entity.UseResolver<OrderEntityResolver>();

    entity.OnGet((int id) => new GetOrder(id));
    entity.OnPost((int id, CreateOrderRequest req) => new CreateOrder(id, req.Items));
    entity.OnPut((int id, UpdateOrderRequest req) => new UpdateOrder(id, req.Status));
    entity.OnDelete((int id) => new CancelOrder(id));
});

await app.RunAsync();
```

The configuration fluent API allows you to:
- Define HTTP methods and their message factories
- Choose an actor resolver strategy
- Optionally specify response mappers and timeouts

## Message Factories

A message factory is a delegate that receives the HTTP request (including route values, query parameters, and body) and returns a message object to send to the actor. The factory can accept:

- **Route values** — parameters from the URL pattern (e.g., `string id` from `/orders/{id}`)
- **Query parameters** — simple types from the query string
- **Request body** — complex types marked with `[FromBody]`
- **Headers** — values marked with `[FromHeader]`
- **Services** — dependencies from the DI container marked with `[FromServices]`
- **TurboHttpContext** — full request context for low-level access

### Simple Message Factory

```csharp
entity.OnGet((int id) => new GetOrder(id));
```

Extract the entity key from the route and construct a message.

### Message Factory with Body

```csharp
public record CreateOrderDto(decimal Amount);

entity.OnPost((int id, CreateOrderDto dto) => new PlaceOrder(id, dto.Amount));
```

The second parameter is automatically bound from the request body as JSON.

### Message Factory with Multiple Parameters

```csharp
public record UpdateOrderDto(string Status, string Notes);

entity.OnPut((int id, UpdateOrderDto dto, [FromHeader] string authorization) =>
    new UpdateOrder(id, dto.Status, dto.Notes, authorization));
```

### Message Factory with Dependency Injection

```csharp
entity.OnPost((int id, CreateOrderDto dto, IValidator<CreateOrderDto> validator) =>
{
    var result = validator.Validate(dto);
    if (!result.IsValid)
        throw new ValidationException(result.Errors);
    return new PlaceOrder(id, dto.Amount);
});
```

Resolve services from the DI container by type.

::: tip
Message factories are **synchronous**. If you need to perform async validation or enrichment, do it in the actor before responding, or use a separate middleware.
:::

## Entity Actors

Entity actors receive messages from the Entity Gateway and send responses back. The actor defines the message types and response logic:

```csharp
public sealed class OrderActor : ReceiveActor
{
    private OrderState _state = new(Guid.NewGuid().ToString(), null, 0m);

    public OrderActor()
    {
        Receive<GetOrder>(Handle);
        Receive<PlaceOrder>(Handle);
        Receive<UpdateOrder>(Handle);
    }

    private void Handle(GetOrder msg)
    {
        var response = _state.Status == null
            ? new NotFoundResponse()
            : new OrderResponse(_state.Id, _state.Status, _state.Amount);
        
        Sender.Tell(response);
    }

    private void Handle(PlaceOrder msg)
    {
        _state = _state with { Status = "pending", Amount = msg.Amount };
        Sender.Tell(new OrderResponse(_state.Id, _state.Status, _state.Amount));
    }

    private void Handle(UpdateOrder msg)
    {
        if (_state.Status == null)
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }
        
        _state = _state with { Status = msg.Status };
        Sender.Tell(new OrderResponse(_state.Id, _state.Status, _state.Amount));
    }

    private sealed record OrderState(string Id, string? Status, decimal Amount);
}

// Message types
public sealed record GetOrder(string Id);
public sealed record PlaceOrder(string Id, decimal Amount);
public sealed record UpdateOrder(string Id, string Status);
public sealed record OrderResponse(string Id, string? Status, decimal Amount);
public sealed record NotFoundResponse();
```

The actor handles each message type and responds using `Sender.Tell()`. The response is matched against registered response mappers and written to the HTTP response.

## Resolvers

A resolver locates the actor for a given entity key. TurboHTTP includes two built-in strategies, and you can implement custom resolvers.

### ChildPerEntityResolver

Creates a child actor per entity key on demand. The first request for an entity key creates a new actor; subsequent requests reuse the same actor:

```csharp
entity.UseResolver<ChildPerEntityResolver>();
```

The resolver expects a parent actor (usually created during startup) that acts as a factory. This is useful for short-lived or dynamically created entities.

::: warning
Requires proper actor lifecycle management. Ensure child actors are terminated when no longer needed to avoid memory leaks.
:::

### RegistryResolver

Looks up a single, pre-registered actor from Akka.Hosting's `ActorRegistry`. Use this when entities are registered at startup:

```csharp
public sealed class RegistryResolver<TKey> : IEntityActorResolver
{
    public ValueTask<IActorRef> ResolveAsync(
        string entityKey, IServiceProvider services, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ActorRegistry>();
        return ValueTask.FromResult(registry.Get<TKey>());
    }
}

// Usage
entity.UseResolver<RegistryResolver<OrderId>>();
```

Setup:

```csharp
builder.Services.AddAkka("actor-system", cfg =>
{
    cfg.StartActors((system, registry) =>
    {
        var orderActorRef = system.ActorOf(Props.Create<OrderActor>(), "order-actor");
        registry.Register<OrderId>(orderActorRef);
    });
});
```

### Custom Resolver

Implement `IEntityActorResolver` to define your own resolution strategy:

```csharp
public sealed class PooledResolver<TKey> : IEntityActorResolver
{
    public async ValueTask<IActorRef> ResolveAsync(
        string entityKey, IServiceProvider services, CancellationToken ct)
    {
        var pool = services.GetRequiredService<ActorPool<TKey>>();
        return await pool.GetOrCreateAsync(entityKey, ct);
    }
}

// Usage
entity.UseResolver<PooledResolver<OrderId>>();
```

The resolver is instantiated at request time. Return the actor reference corresponding to the entity key.

## Ask vs Tell

Entity Gateway supports two dispatch patterns: **Ask** (default) and **Tell** (fire-and-forget).

### Ask Pattern (Default)

The handler sends a message to the actor and waits for a response:

```csharp
entity.OnGet((int id) => new GetOrder(id));
// Returns 200 and the actor's response mapped to JSON
```

- Sends the message using the Ask pattern
- Waits for the actor to respond (respects timeout)
- Maps the response using registered mappers
- Returns the HTTP response with the mapped data

**Status codes:**
- 200 — response received and mapped successfully
- 400 — request parameter binding failed
- 404 — response mapper not found for actor response type
- 504 — Ask timeout (actor didn't respond within timeout)
- 500 — other errors

### Tell Pattern (Fire-and-Forget)

Call `AcceptedResponse()` to use fire-and-forget semantics:

```csharp
entity.OnPost((int id, CreateOrderDto dto) => new PlaceOrder(id, dto.Amount))
    .AcceptedResponse();
// Returns 202 Accepted immediately, actor processes asynchronously
```

- Sends the message using Tell (fire-and-forget)
- Returns 202 Accepted immediately
- Does not wait for or map a response
- No timeout (the actor processes independently)

**Status codes:**
- 202 — message accepted, will be processed asynchronously
- 400 — request parameter binding failed
- 503 — resolver or dispatch failed

::: tip
Use Tell for long-running operations where the client doesn't need the result, or for event logging where the actor simply persists data.
:::

## Response Mapping

Register mappers to convert actor responses to HTTP responses. Each mapper handles a specific response type:

```csharp
entity.MapResponse<OrderResponse>((ctx, resp) =>
    ctx.Response.WriteAsJsonAsync(resp));

entity.MapResponse<NotFoundResponse>((ctx, _) =>
{
    ctx.Response.StatusCode = 404;
    return Task.CompletedTask;
});

entity.MapResponse<ValidationErrorResponse>((ctx, err) =>
{
    ctx.Response.StatusCode = 400;
    return ctx.Response.WriteAsJsonAsync(new { errors = err.Errors });
});
```

When an actor responds, the gateway finds the mapper matching the response type and invokes it. The mapper is responsible for:
- Setting the HTTP status code
- Writing response headers
- Writing the response body

### Exact Type Matching

If you register a mapper for `OrderResponse`, it matches responses of type `OrderResponse` exactly:

```csharp
entity.MapResponse<OrderResponse>((ctx, resp) => ...);

Sender.Tell(new OrderResponse(...)); // Matches
Sender.Tell(new SuccessfulOrderResponse(...)); // Doesn't match (different type)
```

### Subtype Matching

If a more specific mapper doesn't match, the gateway falls back to base type mappers:

```csharp
public record OrderResponse(string Id);
public record SuccessfulOrderResponse(string Id, string Status) : OrderResponse(Id);

entity.MapResponse<OrderResponse>((ctx, resp) =>
    ctx.Response.WriteAsJsonAsync(resp));

Sender.Tell(new SuccessfulOrderResponse("1", "complete")); // Matches OrderResponse mapper
```

### Response Mapper Not Found

If no mapper matches the actor's response type, the gateway returns 500 Internal Server Error. This prevents accidentally exposing internal actor types as HTTP responses.

Always register mappers for all possible actor response types.

::: warning
If you forget to register a mapper for a response type, requests will fail with 500. Add mappers for all responses your actors can send.
:::

## Timeouts

Timeouts apply to the Ask pattern, protecting against hanging requests. Set default timeouts on the builder or override per-method:

### Global Timeout

```csharp
entity.WithTimeout(TimeSpan.FromSeconds(10));
```

Applies to all methods unless overridden. Default is 5 seconds.

### Per-Method Timeout

```csharp
entity.OnGet((int id) => new GetOrder(id))
    .WithTimeout(TimeSpan.FromSeconds(30));

entity.OnPost((int id, CreateOrderDto dto) => new PlaceOrder(id, dto.Amount))
    .WithTimeout(TimeSpan.FromSeconds(5));
```

Override the global timeout for specific methods. Useful for separating fast reads (high timeout) from slow writes (low timeout).

**Timeout behavior:**
- If the actor responds within the timeout, the response is mapped normally
- If the timeout expires, the gateway returns 504 Gateway Timeout
- Tell patterns ignore timeouts (no waiting)

::: tip
Set generous timeouts for queries (10-30s) and tight timeouts for commands (2-5s). This distinguishes between expected slowness and hung actors.
:::

## Complete Example

Full working example with an Order entity, actor, messages, resolver, and registration:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Hosting;
using TurboHTTP.Routing;

// Message types
public sealed record CreateOrderRequest(decimal Amount);
public sealed record GetOrder(string Id);
public sealed record PlaceOrder(string Id, decimal Amount);
public sealed record CancelOrder(string Id);
public sealed record OrderResponse(string Id, string? Status, decimal Amount);
public sealed record NotFoundResponse();

// Entity identifier (used as TKey)
public sealed class OrderId;

// Actor implementation
public sealed class OrderActor : ReceiveActor
{
    private Dictionary<string, (string? Status, decimal Amount)> _orders = new();

    public OrderActor()
    {
        Receive<GetOrder>(Handle);
        Receive<PlaceOrder>(Handle);
        Receive<CancelOrder>(Handle);
    }

    private void Handle(GetOrder msg)
    {
        var exists = _orders.TryGetValue(msg.Id, out var order);
        if (!exists)
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }

        var (status, amount) = order;
        Sender.Tell(new OrderResponse(msg.Id, status, amount));
    }

    private void Handle(PlaceOrder msg)
    {
        _orders[msg.Id] = ("pending", msg.Amount);
        Sender.Tell(new OrderResponse(msg.Id, "pending", msg.Amount));
    }

    private void Handle(CancelOrder msg)
    {
        if (!_orders.ContainsKey(msg.Id))
        {
            Sender.Tell(new NotFoundResponse());
            return;
        }

        _orders.Remove(msg.Id);
        Sender.Tell(new OrderResponse(msg.Id, "cancelled", 0m));
    }
}

// Startup
var builder = WebApplication.CreateBuilder();

builder.Services.AddTurboKestrel();

// Add Akka.Hosting with registry
builder.Services.AddAkka("order-system", cfg =>
{
    cfg.StartActors((system, registry) =>
    {
        var parentRef = system.ActorOf(Props.Create<OrderActor>(), "orders");
        registry.Register<OrderId>(parentRef);
    });
});

var app = builder.Build();

// Register entity route
app.MapTurboEntity<int>("/orders/{id}", entity =>
{
    entity.UseActorRef<OrderActor>();

    entity.OnGet((int id) => new GetOrder(id));
    entity.OnPost((int id, CreateOrderRequest req) => new PlaceOrder(id, req.Amount));
    entity.OnDelete((int id) => new CancelOrder(id));
});

await app.RunAsync();
```

**Usage:**

```bash
# Create an order
curl -X POST http://localhost:5000/orders/order-1 \
  -H "Content-Type: application/json" \
  -d '{"amount": 99.99}'

# Retrieve the order
curl http://localhost:5000/orders/order-1

# Cancel the order
curl -X DELETE http://localhost:5000/orders/order-1

# 404 for unknown order
curl http://localhost:5000/orders/unknown
```

## Error Handling

### Binding Errors

If parameter binding fails (invalid route value, missing body, etc.), the gateway returns 400 Bad Request with error details:

```json
{
  "errors": [
    {
      "parameterName": "amount",
      "message": "Value must be a positive decimal"
    }
  ]
}
```

### Timeout Errors

If an Ask times out, the gateway returns 504 Gateway Timeout. No response mapper is invoked.

### Unmapped Response Types

If an actor responds with a type that has no registered mapper, the gateway returns 500 Internal Server Error. This is a programming error — add a mapper for all possible response types.

### Actor Errors

If an actor throws an exception (or crashes), the Ask pattern detects this and returns 500 Internal Server Error. Implement error handling in the actor:

```csharp
private void Handle(PlaceOrder msg)
{
    try
    {
        _orders[msg.Id] = ("pending", msg.Amount);
        Sender.Tell(new OrderResponse(msg.Id, "pending", msg.Amount));
    }
    catch (Exception ex)
    {
        Sender.Tell(new ErrorResponse(ex.Message));
    }
}
```

## Next Steps

- [Getting Started](./index) — minimal setup and basic patterns
- [Routing](./routing) — route patterns, parameter binding, and route groups
- [Middleware](./middleware) — composing request handlers
- [Configuration](./configuration) — server options and performance tuning
