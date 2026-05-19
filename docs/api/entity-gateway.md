# Entity Gateway API

The Entity Gateway provides actor-based request routing. Incoming HTTP requests are routed to actors based on an entity key extracted from the URL, enabling stateful per-entity request handling.

## Registration

Register entity routes via `MapTurboEntity` on `WebApplication`:

```csharp
public static class TurboRoutingExtensions
{
    TurboRouteHandlerBuilder MapTurboEntity(
        this WebApplication app, 
        string pattern, 
        Action<TurboEntityBuilder> configure);
    
    TurboRouteHandlerBuilder MapTurboEntity<TActorKey>(
        this WebApplication app, 
        string pattern, 
        Action<TurboEntityBuilder> configure);
}
```

### Untyped Key

When no type parameter is provided, entity keys are boxed `object`:

```csharp
app.MapTurboEntity("/users/{id}", builder =>
{
    builder.OnGet(ctx => new GetUser(ctx.RouteValues["id"]));
});
```

### Typed Key

Specify a typed key for type safety:

```csharp
app.MapTurboEntity<int>("/orders/{orderId}", builder =>
{
    builder.OnGet(ctx => 
    {
        var orderId = (int)ctx.RouteValues["orderId"];
        return new GetOrder(orderId);
    });
});
```

Inside route groups:

```csharp
var api = app.MapTurboGroup("/api");

api.MapEntity("/users/{id}", builder => { /* ... */ });
api.MapEntity<string>("/posts/{slug}", builder => { /* ... */ });
```

---

## TurboEntityBuilder

```csharp
public sealed class TurboEntityBuilder
{
    public TurboEntityMethodBuilder OnGet(Delegate messageFactory);
    public TurboEntityMethodBuilder OnPost(Delegate messageFactory);
    public TurboEntityMethodBuilder OnPut(Delegate messageFactory);
    public TurboEntityMethodBuilder OnDelete(Delegate messageFactory);
    public TurboEntityMethodBuilder OnPatch(Delegate messageFactory);
    
    public TurboEntityBuilder MapResponse<TResponse>(
        Func<TurboHttpContext, TResponse, Task> mapper);
    
    public TurboEntityBuilder WithTimeout(TimeSpan timeout);
    
    public TurboEntityBuilder UseResolver(IEntityActorResolver resolver);
    public TurboEntityBuilder UseResolver<TResolver>() where TResolver : IEntityActorResolver, new();
    
    public TurboEntityBuilder UseActorRef<TActorKey>();
    public TurboEntityBuilder UseActorRef(Func<IServiceProvider, IActorRef> factory);
    public TurboEntityBuilder UseActorRef(Func<IReadOnlyActorRegistry, IActorRef> actorRefFactory);
}
```

### HTTP Method Handlers

Specify what message to send for each HTTP method:

```csharp
builder.OnGet(ctx => new GetUser(ctx.RouteValues["id"]));
builder.OnPost(ctx => JsonConvert.DeserializeObject<CreateUserRequest>(
    Encoding.UTF8.GetString(ctx.Request.Body)));
builder.OnPut(ctx => new UpdateUser(ctx.RouteValues["id"], ctx.Request.Body));
builder.OnDelete(ctx => new DeleteUser(ctx.RouteValues["id"]));
builder.OnPatch(ctx => new PatchUser(ctx.RouteValues["id"], ctx.Request.Body));
```

Each handler receives the `TurboHttpContext` and returns a message to send to the actor.

### Response Mapping

By default, responses from actors are serialized as JSON. Customize mapping with `MapResponse<T>`:

```csharp
builder.MapResponse<User>(async (context, user) =>
{
    context.Response.StatusCode = 200;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(user);
});

builder.MapResponse<ErrorResponse>(async (context, error) =>
{
    context.Response.StatusCode = error.StatusCode;
    await context.Response.WriteAsJsonAsync(new { error = error.Message });
});
```

### Timeout Configuration

Set per-route timeout for actor responses:

```csharp
// Default: 30 seconds
builder.WithTimeout(TimeSpan.FromSeconds(60));
```

If the actor doesn't respond within the timeout, the response is `504 Gateway Timeout`.

---

## TurboEntityMethodBuilder

```csharp
public sealed class TurboEntityMethodBuilder
{
    public TurboEntityMethodBuilder AcceptedResponse();
    public TurboEntityMethodBuilder WithTimeout(TimeSpan timeout);
}
```

### AcceptedResponse

Respond with `202 Accepted` instead of waiting for the actor's full response:

```csharp
builder.OnPost(ctx => new CreateUser(...))
    .AcceptedResponse();
```

Useful for long-running operations where you want to return immediately.

### Per-Method Timeout

Override the route-level timeout for a specific method:

```csharp
builder.OnGet(ctx => new GetUser(...))
    .WithTimeout(TimeSpan.FromSeconds(5));

builder.OnPost(ctx => new CreateUser(...))
    .WithTimeout(TimeSpan.FromSeconds(30));
```

---

## Resolver Strategies

Entity routes need a strategy to locate or create the actor that will handle the request. TurboHTTP supports three approaches:

### 1. Custom Resolver

Implement `IEntityActorResolver` for full control:

```csharp
public interface IEntityActorResolver
{
    IActorRef ResolveActor<TKey>(TKey key);
}

public class OrderActorResolver : IEntityActorResolver
{
    private readonly IActorRef _orderManager;

    public OrderActorResolver(IActorRef orderManager)
    {
        _orderManager = orderManager;
    }

    public IActorRef ResolveActor<TKey>(TKey key)
    {
        // Route to shard based on order ID
        return _orderManager;
    }
}

// Register it
builder.UseResolver(new OrderActorResolver(orderManagerRef));
// Or as a type
builder.UseResolver<OrderActorResolver>();
```

### 2. ActorRef Factory

Direct reference to a specific actor:

```csharp
// From dependency injection
builder.UseActorRef((serviceProvider) =>
{
    return serviceProvider.GetRequiredService<IActorRef>("userActorRef");
});

// From actor registry
builder.UseActorRef((registry) =>
{
    return registry.Resolve<UserActor>();
});
```

### 3. Generic ActorRef Lookup

Use the type system to locate actors by type:

```csharp
builder.UseActorRef<UserActor>();
```

This looks up `UserActor` in the actor registry.

---

## Complete Example

```csharp
// Actor definition
public class UserActor : ReceiveActor
{
    public sealed class GetUser { public int Id { get; set; } }
    public sealed class CreateUser { public string Name { get; set; } }
    public sealed class User { public int Id { get; set; } public string Name { get; set; } }

    public UserActor()
    {
        Receive<GetUser>(msg =>
        {
            // Simulate database lookup
            var user = new User { Id = msg.Id, Name = "John Doe" };
            Sender.Tell(user);
        });

        Receive<CreateUser>(msg =>
        {
            var user = new User { Id = 123, Name = msg.Name };
            Sender.Tell(user);
        });
    }
}

// Route registration
app.MapTurboEntity<int>("/users/{id}", builder =>
{
    // GET /users/123
    builder.OnGet(ctx =>
    {
        var id = (int)ctx.RouteValues["id"];
        return new UserActor.GetUser { Id = id };
    });

    // POST /users (body contains name)
    builder.OnPost(async ctx =>
    {
        var body = await ctx.Request.BodyReader.ReadAsync();
        var json = Encoding.UTF8.GetString(body.Buffer);
        var req = JsonConvert.DeserializeObject<CreateUserRequest>(json);
        return new UserActor.CreateUser { Name = req.Name };
    });

    // Custom response mapping
    builder.MapResponse<UserActor.User>(async (context, user) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(user);
    });

    // 30-second timeout for responses
    builder.WithTimeout(TimeSpan.FromSeconds(30));

    // Resolve to the UserActor
    builder.UseActorRef<UserActor>();
});
```

---

## Request Flow

1. HTTP request arrives for `/users/123`
2. Route pattern matches; entity key `123` is extracted
3. Message factory is called: `OnGet(ctx) => new GetUser { Id = 123 }`
4. Resolver locates or creates the actor
5. Message is sent to the actor (ask pattern with timeout)
6. Actor responds with a result (e.g., `User` object)
7. Response mapper serializes the result to HTTP response
8. Response is sent to the client

If the actor doesn't respond within the timeout, the response is `504 Gateway Timeout`. If `AcceptedResponse()` is used, step 8 returns `202 Accepted` immediately after sending the message.

---

## Error Handling

If the actor throws an exception or the message doesn't match a handler, the gateway responds with `500 Internal Server Error`. Use status code routing or custom response mappers to handle errors from the actor:

```csharp
builder.MapResponse<Result<User>>(async (context, result) =>
{
    if (result.IsSuccess)
    {
        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(result.Value);
    }
    else
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = result.Error });
    }
});
```

Or use a wrapper response type:

```csharp
builder.MapResponse<ApiResponse>(async (context, response) =>
{
    context.Response.StatusCode = response.StatusCode;
    await context.Response.WriteAsJsonAsync(response);
});
```
