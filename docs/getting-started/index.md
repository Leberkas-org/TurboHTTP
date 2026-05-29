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
| **What it does** | Makes HTTP requests with built-in retries, caching, cookies, and connection pooling | Serves HTTP/1.0, 1.1, 2, 3 as a drop-in ASP.NET Core IServer (Kestrel replacement); middleware, routing, Minimal APIs, and Controllers are standard ASP.NET Core; an optional actor-based entity gateway is available via the separate Servus.Akka.AspNetCore package |
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

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

app.MapGet("/health", () => new { status = "healthy" });
app.MapGet("/users/{id}", (int id) => new { id, name = "User " + id });

await app.RunAsync();
```

::: tip About UseTurboHttp
TurboHTTP Server is a high-performance IServer implementation for ASP.NET Core built on Akka.Streams; it replaces Kestrel as the transport layer and integrates with standard ASP.NET Core middleware, routing, and DI. Register it on `builder.Host` using `UseTurboHttp()`.
:::

## Next Steps

- [Client Quick Start](./client) — build your first TurboHTTP client
- [Server Quick Start](./server) — build your first TurboHTTP server
- [Architecture Overview](./architecture) — understand how the pipeline works
- [Migration from HttpClient](./migration) — coming from `System.Net.Http`?
