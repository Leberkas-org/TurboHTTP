# Routing

TurboHTTP Server uses a minimal API-style route registration system. Register routes directly on the `WebApplication` instance using extension methods that follow ASP.NET Core conventions, with fluent builders for metadata and configuration.

## Basic Routes

Register routes using `MapTurboGet`, `MapTurboPost`, `MapTurboPut`, `MapTurboDelete`, or `MapTurboPatch`:

```csharp
var builder = WebApplication.CreateBuilder();
builder.Services.AddTurboKestrel();
var app = builder.Build();

// Simple handler returning a string
app.MapTurboGet("/hello", () => "Hello from TurboHTTP");

// Handler returning typed result
app.MapTurboGet("/status", () => TypedResults.Ok(new { Status = "running" }));

// Handler accepting TurboHttpContext for low-level access
app.MapTurboPost("/echo", (TurboHttpContext context) =>
{
    var body = await context.Request.Body.ReadAsStringAsync();
    return Results.Ok(body);
});

// Handler with dependency injection
app.MapTurboPost("/data", async (IDataService service) =>
{
    var data = await service.GetDataAsync();
    return TypedResults.Ok(data);
});

await app.RunAsync();
```

Route handlers are bound at startup and frozen — the route table is immutable after the app starts.

::: tip
Any delegate or lambda that accepts supported parameter types will work. See [Parameter Binding](#parameter-binding) for what can be injected.
:::

## Route Patterns

Patterns consist of literal segments and parameters:

### Literal Routes

```csharp
app.MapTurboGet("/health", () => "OK");
app.MapTurboGet("/api/status", () => TypedResults.Ok());
```

### Route Parameters

Parameters are enclosed in curly braces. By default, they capture path segments and are bound as strings:

```csharp
app.MapTurboGet("/users/{id}", (string id) => TypedResults.Ok($"User: {id}"));

app.MapTurboGet("/posts/{postId}/comments/{commentId}",
    (string postId, string commentId) =>
        TypedResults.Ok(new { Post = postId, Comment = commentId })
);
```

Parameter names are matched to handler arguments by name (case-insensitive):

```csharp
app.MapTurboGet("/items/{id}", (int id) =>
{
    // Parameter 'id' is automatically parsed as int
    return TypedResults.Ok($"Item ID: {id}");
});
```

### Supported Route Value Types

| Type | Example | Notes |
|------|---------|-------|
| `string` | `/users/{name}` | Default, no parsing needed |
| `int` | `/posts/{id}` | 32-bit signed integer |
| `long` | `/archives/{id}` | 64-bit signed integer |
| `float` | `/temperature/{value}` | Single-precision floating point |
| `double` | `/distance/{value}` | Double-precision floating point |
| `decimal` | `/price/{amount}` | High-precision decimal |
| `bool` | `/settings/{enabled}` | Parses "true"/"false" |
| `Guid` | `/items/{key}` | UUID format |
| `DateTime` | `/events/{date}` | ISO 8601 format |
| `DateTimeOffset` | `/logs/{timestamp}` | Timezone-aware datetime |
| `TimeSpan` | `/delays/{duration}` | ISO 8601 duration format |

Parse failures for route parameters return a 400 status code automatically.

::: warning
Parameter names must match handler argument names exactly (case-insensitive). Misnamed parameters are treated as query string parameters or dependency injection targets instead of route values.
:::

## Parameter Binding

Handlers can accept multiple types of parameters. The binder infers the source based on the parameter type and optional attributes:

### Request Properties

Access the request object directly:

```csharp
app.MapTurboPost("/upload", async (TurboHttpContext context, HttpRequest request) =>
{
    var contentType = request.ContentType;
    var body = request.Body;
    return TypedResults.Accepted();
});
```

**Implicit types:**
- `TurboHttpContext` — the full request context
- `HttpRequest` — the HTTP request
- `CancellationToken` — request cancellation token

### Route Parameters

Parameters with names matching route segments are automatically bound:

```csharp
app.MapTurboGet("/users/{userId}/posts/{postId}",
    (int userId, int postId) =>
        TypedResults.Ok(new { UserId = userId, PostId = postId })
);
```

### Query String Parameters

By default, non-route parameters of simple types are bound from the query string:

```csharp
app.MapTurboGet("/search", (string q, int page = 1) =>
    TypedResults.Ok(new { Query = q, Page = page })
);

// GET /search?q=hello&page=2 binds q="hello", page=2
```

### Headers

Use the `[FromHeader]` attribute to bind request headers:

```csharp
using Microsoft.AspNetCore.Mvc;

app.MapTurboPost("/data", ([FromHeader] string authorization) =>
{
    var token = authorization; // Value of Authorization header
    return TypedResults.Ok();
});

app.MapTurboGet("/info", ([FromHeader(Name = "X-Custom")] string custom) =>
{
    // Bind custom header, default to "X-Custom"
    return TypedResults.Ok(custom);
});
```

### JSON Body

Use the `[FromBody]` attribute or the parameter type must be a complex type:

```csharp
public record CreateUserRequest(string Name, string Email);

app.MapTurboPost("/users", ([FromBody] CreateUserRequest body) =>
    TypedResults.Created("/users/1", body)
);
```

If no explicit attributes are used and the type is a class or interface (not a simple type or service), it is treated as JSON body.

### Form Data

Bind form fields and files with `[FromForm]`:

```csharp
app.MapTurboPost("/upload",
    ([FromForm] string name, [FromForm] IFormFile file) =>
    {
        var fileName = file.FileName;
        var size = file.Length;
        return TypedResults.Accepted();
    }
);
```

### Dependency Injection

Services registered in the DI container are resolved automatically:

```csharp
public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}

builder.Services.AddScoped<IEmailService, EmailService>();

app.MapTurboPost("/notify", async (IEmailService email, string recipient) =>
{
    await email.SendAsync(recipient, "Hello", "Welcome!");
    return TypedResults.NoContent();
});
```

**Rules:**
- Parameters matching route segments are route values
- Simple types (string, int, bool, etc.) default to query string
- Complex types, interfaces, and classes are resolved from DI
- Use explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromForm]`, `[FromServices]`) to override

## Route Groups

Group multiple routes under a common prefix using `MapTurboGroup`:

```csharp
var api = app.MapTurboGroup("/api");

api.MapGet("/users", () => TypedResults.Ok());
api.MapPost("/users", () => TypedResults.Created("/users/1", null));
api.MapGet("/users/{id}", (int id) => TypedResults.Ok());
api.MapPut("/users/{id}", (int id) => TypedResults.NoContent());
api.MapDelete("/users/{id}", (int id) => TypedResults.NoContent());
```

All routes under the group are prefixed with `/api`.

### Nested Groups

Groups can be nested:

```csharp
var api = app.MapTurboGroup("/api");
var v1 = api.MapGroup("/v1");
var users = v1.MapGroup("/users");

users.MapGet("", () => TypedResults.Ok());           // GET /api/v1/users
users.MapPost("", () => TypedResults.Created("", null));  // POST /api/v1/users
users.MapGet("/{id}", (int id) => TypedResults.Ok()); // GET /api/v1/users/{id}
```

### Group Metadata

Groups support metadata for documentation and filtering (though metadata is not applied to routes at runtime):

```csharp
var adminApi = app.MapTurboGroup("/admin")
    .WithTags("administration")
    .WithMetadata(new AuthorizeAttribute());

adminApi.MapGet("/stats", () => TypedResults.Ok());
```

Metadata is stored but not enforced by the routing engine. Use it for API documentation, OpenAPI schemas, or custom processing.

## Route Handler Builder

The builder returned by `MapTurboGet()`, `MapTurboPost()`, etc. allows you to add metadata and configure the route:

```csharp
app.MapTurboGet("/users", () => TypedResults.Ok())
    .WithName("GetUsers")
    .WithTags("users", "public")
    .WithMetadata(new CustomMetadata())
    .Produces<List<User>>(200)
    .ProducesProblem(500);
```

### Builder Methods

| Method | Purpose |
|--------|---------|
| `WithName(string name)` | Assign a name for documentation and routing references |
| `WithTags(params string[] tags)` | Add tags (e.g., "users", "admin") for grouping |
| `WithMetadata(params object[] metadata)` | Store arbitrary metadata objects |
| `RequireAuthorization()` | Mark route as requiring authorization (informational) |
| `AllowAnonymous()` | Mark route as allowing anonymous access (informational) |
| `Produces<T>(int statusCode = 200)` | Declare response type and status code |
| `ProducesProblem(int statusCode = 500)` | Declare problem response status code |

Metadata is stored on the route but not enforced by the routing engine. Use it for API documentation, OpenAPI generation, or custom middleware that inspects endpoint metadata.

```csharp
app.MapTurboPost("/items", async (IItemService service) =>
    TypedResults.Created("/items/1", new { Id = 1 })
)
    .WithName("CreateItem")
    .WithTags("items")
    .Produces<ItemResponse>(201)
    .ProducesProblem(400);
```

## Multi-Method Routes

Register a single handler for multiple HTTP methods using `MapTurboMethods`:

```csharp
app.MapTurboMethods(
    "/items",
    new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put },
    (TurboHttpContext context) =>
    {
        return context.Request.Method switch
        {
            "GET" => TypedResults.Ok(),
            "POST" => TypedResults.Created("/items/1", null),
            "PUT" => TypedResults.NoContent(),
            _ => TypedResults.BadRequest()
        };
    }
);
```

The handler receives the full request context and can branch on `context.Request.Method`.

::: warning
Only use multi-method routes when the same handler genuinely implements multiple methods. Prefer separate `MapTurboGet`, `MapTurboPost`, etc. for clarity.
:::

## How Routing Works

### Route Registration (Startup)

Routes are registered during application startup as you call `MapTurboGet`, `MapTurboPost`, etc. Each route is added to the `TurboRouteTable`:

1. Pattern is stored as-is (e.g., `/users/{id}`)
2. Handler is bound — parameters are introspected and matched to route/query/service sources
3. A dispatcher is created that will invoke the bound handler at request time

### Route Freezing

After the app starts, the route table is frozen. No new routes can be added, and lookups are optimized.

### Request Matching

When a request arrives:

1. **Method matching** — exact HTTP method match required (GET, POST, etc.)
2. **Path matching** — request path is split into segments and compared to route patterns
3. **Segment count** — pattern segments must equal path segments (both count-based)
4. **Literal matching** — literal segments match exactly (case-insensitive)
5. **Parameter capture** — route parameters are extracted and stored in `RouteValues`
6. **Binding** — handler parameters are bound from route values, query string, headers, DI, or JSON body
7. **Invocation** — handler is called with bound arguments

If no route matches, a 404 response is sent.

### Middleware Order

Middleware runs **before** routing. This means:

- CORS, logging, authentication middleware execute for all requests
- Routing happens after middleware pipeline
- Route handlers are the last step in request processing

```csharp
// Logging runs for all requests
app.UseTurbo(async (context, next) =>
{
    Console.WriteLine($"Incoming {context.Request.Method} {context.Request.Path}");
    await next(context);
});

// Route handlers execute here if a route matches
app.MapTurboGet("/hello", () => "Hi");

// Terminal fallback for no match
app.RunTurbo(context => context.Response.StatusCode = 404);
```

## Entity Routes

For actor-based CQRS or event-driven request handling, TurboHTTP provides entity routes that map HTTP requests to actor messages and responses.

Entity routes are a specialized feature that integrate with Akka.Streams and actor systems. See [Entity Gateway](./entity-gateway.md) for full details on configuring entity routes, request/response mapping, and actor resolution.

```csharp
app.MapTurboEntity<string>("/items/{id}", entity =>
{
    entity.UseActorRef<ItemActor>();
    entity.OnGet((string id) => new GetItemRequest(id));
    entity.OnPut((string id, UpdateItemRequest req) => new UpdateItem(id, req));
    entity.OnDelete((string id) => new DeleteItem(id));
});
```

## Error Handling

### Validation Errors

Parse errors (invalid route parameter types) return 400 Bad Request automatically. Validation attribute failures also return 400 with error details.

### Handler Exceptions

Unhandled exceptions in handlers result in a 500 Internal Server Error. Use middleware to implement custom exception handling.

```csharp
app.UseTurbo(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (NotFoundException)
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Not found");
    }
    catch (UnauthorizedException)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
    }
    catch (Exception)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync("Internal error");
    }
});

app.MapTurboGet("/items/{id}", async (int id, IItemService service) =>
{
    var item = await service.GetItemAsync(id); // May throw NotFoundException
    return TypedResults.Ok(item);
});
```

## Next Steps

- [Getting Started](./index) — minimal setup and basic patterns
- [Middleware](./middleware) — composing request handlers
- [Entity Gateway](./entity-gateway) — actor-based request handling
- [Configuration](./configuration) — server options and performance tuning
