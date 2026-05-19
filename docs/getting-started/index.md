# Getting Started

TurboHTTP is a high-performance HTTP client and server for .NET built on Akka.Streams. It supports HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with automatic retries, caching, cookies, connection pooling, middleware, routing, and entity gateway — all in one package.

## Install

```bash
dotnet add package TurboHTTP
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="TurboHTTP" Version="1.*" />
```

## Choose Your Path

TurboHTTP has two sides — use either or both:

| | Client | Server |
|---|---|---|
| **What it does** | Makes HTTP requests with built-in retries, caching, cookies, and connection pooling | Handles HTTP requests with middleware, routing, and actor-based entity gateway |
| **Get started** | [Client Quick Start →](./client) | [Server Quick Start →](./server) |
| **Full docs** | [Client Guide →](/client/) | [Server Guide →](/server/) |

## Quick Look

### Client

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache()
.WithCookies()
.WithRedirect();

var app = builder.Build();
var factory = app.Services.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient("api");

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);
```

### Server

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

app.MapTurboGet("/health", () => new { status = "healthy" });
app.MapTurboGet("/users/{id}", (int id) => new { id, name = "User " + id });

await app.RunAsync();
```

::: tip About AddTurboKestrel
Despite the name, TurboHTTP Server is a fully standalone HTTP server built on Akka.Streams with its own TCP/QUIC transport layer. The method is named `AddTurboKestrel` for configuration familiarity — it does not use or depend on Kestrel.
:::

## Next Steps

- [Client Quick Start](./client) — build your first TurboHTTP client
- [Server Quick Start](./server) — build your first TurboHTTP server
- [Architecture Overview](./architecture) — understand how the pipeline works
- [Migration from HttpClient](./migration) — coming from `System.Net.Http`?
