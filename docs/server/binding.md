# Parameter Binding

TurboHTTP Server automatically binds handler delegate parameters from multiple sources. The binding system inspects the handler's parameters and resolves them from the request context, enabling clean handler signatures without boilerplate request inspection.

## How It Works

When you define a handler, the server examines each parameter and determines where to resolve it based on the parameter name, type, and attributes. This happens automatically—no explicit binding configuration needed.

```csharp
app.MapPost("/users/{id}", async (int id, CreateUserRequest body, CancellationToken ct) =>
{
    // id comes from the route template {id}
    // body comes from the request JSON body
    // ct comes from the cancellation infrastructure
});
```

## Binding Sources

Parameters are resolved in this order of precedence:

### 1. Special Types (Highest Priority)

These are injected directly from the request context, regardless of parameter name:

- **`TurboHttpContext`** — The complete HTTP context containing request, response, and connection details
- **`CancellationToken`** — Scoped to this handler invocation

```csharp
app.MapGet("/info", (TurboHttpContext ctx, CancellationToken ct) =>
{
    var method = ctx.Request.Method;
    var path = ctx.Request.Path;
    // Handler will be cancelled if client disconnects
});
```

### 2. Route Values

Parameters matching route template placeholders are bound by name:

```csharp
app.MapGet("/posts/{id}/comments/{commentId}", (int id, int commentId) =>
{
    // id comes from {id} in the route
    // commentId comes from {commentId} in the route
});
```

**Supported route value types:**
- Integers: `int`, `long`
- Decimals: `float`, `double`, `decimal`
- Booleans: `bool`
- Identifiers: `Guid`
- Dates: `DateTime`, `DateTimeOffset`
- Time: `TimeSpan`
- Text: `string`

::: warning Parsing Behavior
Route values are parsed using `TypeDescriptor.GetConverter()`. If parsing fails, the route does not match.
:::

### 3. From Header

Use the `[FromHeader]` attribute to bind from request headers:

```csharp
app.MapGet("/secure", ([FromHeader] string authorization, [FromHeader("X-API-Key")] string apiKey) =>
{
    // authorization comes from the Authorization header
    // apiKey comes from the X-API-Key header
    // Header names are case-insensitive
});
```

Header names default to the parameter name (with hyphens replacing underscores). Override with the attribute argument.

::: tip Header Name Mapping
`[FromHeader] string user_agent` binds to the `User-Agent` header automatically.
:::

### 4. From Query String

Use the `[FromQuery]` attribute or rely on convention for simple types in GET requests:

```csharp
app.MapGet("/search", (string q, [FromQuery] int page = 1, [FromQuery("sort-by")] string sortBy = "date") =>
{
    // q comes from ?q=...
    // page comes from ?page=...
    // sortBy comes from ?sort-by=...
});
```

Query parameter names are matched to parameter names (with underscore-to-hyphen conversion). Defaults are respected.

### 5. From JSON Body

Complex types in POST, PUT, or PATCH requests are automatically bound from the request body as JSON:

```csharp
public record CreateUserRequest(string Name, string Email, int Age);

app.MapPost("/users", async (CreateUserRequest req) =>
{
    return new { Message = $"Created user: {req.Name}" };
});
```

The request body is deserialized into the parameter type. The handler receives the deserialized object.

```bash
curl -X POST http://localhost:5000/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com","age":30}'
```

::: warning Content Type
Body binding only occurs for `Content-Type: application/json`. Other content types are not automatically deserialized.
:::

### 6. From Body (Explicit)

Use `[FromBody]` to explicitly bind a parameter to the request body:

```csharp
app.MapPost("/upload-metadata", ([FromBody] MetadataRequest metadata) =>
{
    return new { Message = "Metadata stored" };
});
```

This is useful for disambiguation when multiple parameters could be interpreted as body parameters.

### 7. From Form Data

Use the `[FromForm]` attribute to bind from `application/x-www-form-urlencoded` or `multipart/form-data`:

```csharp
app.MapPost("/upload", ([FromForm] string title, [FromForm] TurboFormFile file) =>
{
    var content = file.Stream;
    var fileName = file.FileName;
    return new { Message = $"Uploaded: {fileName}" };
});
```

`TurboFormFile` provides access to uploaded file streams and metadata.

### 8. Service Injection

Parameters not matched by any above rule are resolved from the dependency injection container:

```csharp
// Assume IUserRepository is registered in DI
app.MapGet("/users/{id}", (int id, IUserRepository repo) =>
{
    var user = await repo.GetByIdAsync(id);
    return user;
});
```

If a parameter type is registered in the DI container and doesn't match earlier binding sources, it's injected.

::: warning Unresolvable Parameters
If a parameter cannot be bound and is not optional, the route registration fails or throws at invocation time.
:::

### 9. Context and Cancellation (Recap)

These are always available and bound first:

```csharp
app.MapGet("/info", (TurboHttpContext ctx, CancellationToken ct, int? id = null) =>
{
    var remoteIP = ctx.Connection.RemoteAddress;
    return new { remoteIP };
});
```

## Binding Order Summary

This table shows the complete precedence (top to bottom):

| Order | Source | Binding Method | Example |
|-------|--------|----------------|---------|
| 1 | Special Types | Direct injection | `TurboHttpContext ctx` |
| 1 | Special Types | Direct injection | `CancellationToken ct` |
| 2 | Route Values | Template match + parse | `(int id)` → `{id:int}` |
| 3 | Headers | `[FromHeader]` or named match | `[FromHeader] string authorization` |
| 4 | Query String | `[FromQuery]` or inferred | `[FromQuery] int page` |
| 5 | JSON Body | Auto for complex types in POST/PUT/PATCH | `(CreateUserRequest req)` |
| 6 | Body (Explicit) | `[FromBody]` | `[FromBody] string raw` |
| 7 | Form Data | `[FromForm]` | `[FromForm] string title` |
| 8 | Service Injection | DI container lookup | `(IUserService service)` |

## Advanced: Composite Parameters with `[AsParameters]`

Use `[AsParameters]` to bind multiple source values into a single composite type:

```csharp
public record PaginationFilter(
    [FromQuery] int Page = 1,
    [FromQuery] int Limit = 10,
    [FromQuery] string? Sort = null
);

app.MapGet("/posts", ([AsParameters] PaginationFilter filter) =>
{
    return new { filter.Page, filter.Limit, filter.Sort };
});
```

This is equivalent to:

```csharp
app.MapGet("/posts", (
    [FromQuery] int page = 1,
    [FromQuery] int limit = 10,
    [FromQuery] string? sort = null) =>
{
    return new { page, limit, sort };
});
```

`[AsParameters]` works with:
- Records with constructor parameters
- Classes with settable properties
- Named tuple types

Each member is bound according to its own attributes and type.

## Practical Examples

### Example 1: REST Resource Endpoint

```csharp
public record UpdateProductRequest(string Name, decimal Price);

app.MapPut("/products/{id}", async (
    int id,
    UpdateProductRequest req,
    IProductService service,
    CancellationToken ct) =>
{
    var product = await service.UpdateAsync(id, req.Name, req.Price, ct);
    return Results.Ok(product);
});
```

- `id` from route `{id}`
- `req` from JSON body
- `service` from DI
- `ct` from cancellation infrastructure

### Example 2: Complex Query Filtering

```csharp
public record SearchFilter(
    [FromQuery] string Q,
    [FromQuery] int Page = 1,
    [FromQuery] int Limit = 20,
    [FromQuery("date-from")] DateTime? DateFrom = null
);

app.MapGet("/articles", (
    [AsParameters] SearchFilter filter,
    IArticleRepository repo) =>
{
    return repo.Search(filter.Q, filter.Page, filter.Limit, filter.DateFrom);
});
```

Query: `?q=turbohttp&page=2&limit=50&date-from=2024-01-01`

### Example 3: File Upload with Metadata

```csharp
app.MapPost("/files", async (
    [FromForm] string title,
    [FromForm] string? description,
    [FromForm] TurboFormFile file,
    IFileService fileService,
    CancellationToken ct) =>
{
    using var stream = file.Stream;
    var id = await fileService.StoreAsync(title, description, stream, ct);
    return Results.Created($"/files/{id}", new { id });
});
```

### Example 4: Conditional Headers and Request Context

```csharp
app.MapGet("/data/{id}", (
    int id,
    [FromHeader("If-None-Match")] string? etag,
    TurboHttpContext ctx) =>
{
    var data = GetData(id);
    if (etag == data.ETag)
    {
        ctx.Response.StatusCode = 304; // Not Modified
        return null;
    }
    return data;
});
```

## Best Practices

::: tip Keep Signatures Clean
Use `[AsParameters]` to group related query/header parameters into records. This improves readability and reusability.
:::

::: tip Validate Early
Complex types from the body should have constructor validation or use a middleware to validate before handler invocation.
:::

::: tip Use Cancellation Tokens
Always accept `CancellationToken` in async handlers. It signals graceful shutdown and client disconnection.
:::

::: warning Avoid Ambiguity
If a parameter could match multiple sources, be explicit with attributes (`[FromQuery]`, `[FromBody]`, etc.).
:::

## What's Not Bound

- **Static values** — No global constants or configuration binding
- **Ambient context** — Beyond `TurboHttpContext` and `CancellationToken`
- **Implicit collection binding** — `List<int>` from multiple query values requires custom parsing
