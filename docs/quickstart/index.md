# Quick Guide

TurboHTTP is a unified HTTP client and server library for .NET built on Akka.Streams. Use it to build high-performance HTTP services with automatic retries, caching, cookies, connection pooling, middleware pipelines, routing, and entity gateways — all in one package.

## Install

```bash
dotnet add package TurboHTTP
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="TurboHTTP" Version="1.*" />
```

## Client — Make Requests

Register a client with dependency injection and compose features using the fluent builder API:

```csharp
using TurboHTTP;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register a named client with features
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()                  // automatic retries for idempotent requests
.WithCache()                  // in-memory HTTP caching with ETag support
.WithCookies()                // automatic cookie storage and injection
.WithRedirect();              // follow redirect chains automatically

var app = builder.Build();
```

Resolve the client from the factory and send requests:

```csharp
var factory = app.Services.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient("api");

// Make a request
var request = new HttpRequestMessage(HttpMethod.Get, "/users");
var response = await client.SendAsync(request, CancellationToken.None);

response.EnsureSuccessStatusCode();
var content = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(content);
```

::: tip High-Throughput Usage
TurboHTTP also exposes a channel-based API (`client.Requests` and `client.Responses`) for scenarios where you want to stream requests and responses concurrently without awaiting each one individually. See [Client Guide — High-Throughput Usage](/client/#high-throughput-usage) for batch processing examples.
:::

## Server — Handle Requests

Register Kestrel with TurboHTTP and define routes and middleware:

```csharp
using TurboHTTP;
using TurboHTTP.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register TurboHTTP server (Kestrel integration)
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

// Define middleware (runs for every request)
app.UseTurbo(async (context, next) =>
{
    // Runs before the matched route handler
    Console.WriteLine($"[Middleware] {context.Request.Method} {context.Request.Path}");
    await next(context);
    // Runs after the handler
    Console.WriteLine($"[Middleware] Response status: {context.Response.StatusCode}");
});

// Define GET routes
app.MapTurboGet("/health", async context =>
{
    await context.Response.WriteAsJsonAsync(new { status = "ok" });
});

app.MapTurboGet("/users/{id}", async context =>
{
    var id = context.Request.RouteValues["id"];
    await context.Response.WriteAsJsonAsync(new { id, name = "Alice" });
});

// Define POST routes with request body
app.MapTurboPost("/users", async context =>
{
    var user = await context.Request.Content.ReadFromJsonAsync<User>();
    // Process the user...
    context.Response.StatusCode = 201; // Created
    await context.Response.WriteAsJsonAsync(user);
});

app.MapTurboDelete("/users/{id}", async context =>
{
    var id = context.Request.RouteValues["id"];
    // Delete the user...
    context.Response.StatusCode = 204; // No Content
});

app.Run();

public sealed class User
{
    public required string Name { get; set; }
    public required string Email { get; set; }
}
```

::: tip Entity Gateway
For stateful request handling, route directly to Akka.NET actors using the entity gateway pattern. This allows you to manage session state, validation, and side effects at the actor level. See [Server Guide — Entity Gateway](/server/entity-gateway) for patterns and examples.
:::

## Next Steps

**Learn more about the client:**
- [Installation & Setup](/client/installation) — DI registration, named clients, typed clients
- [Configuration](/client/configuration) — all client options explained
- [Full Client Guide](/client/) — retries, caching, cookies, redirects, HTTP/2, HTTP/3

**Learn more about the server:**
- [Installation & Setup](/server/installation) — Kestrel setup, HTTPS configuration
- [Middleware Pipeline](/server/middleware) — building composable request processing
- [Full Server Guide](/server/) — routing, entity gateway, actor integration

**Understand the architecture:**
- [Architecture Overview](/architecture/) — layers, stages, and data flow
