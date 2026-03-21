# Installation & Setup

## Requirements

- **.NET 10.0** or later
- **Akka.NET** is pulled in as a transitive dependency — no manual installation needed

## Install the Package

```bash
dotnet add package TurboHttp
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="TurboHttp" Version="1.*" />
```

## Dependency Injection (Recommended)

Register TurboHttp in your `IServiceCollection`:

```csharp
using TurboHttp;

var builder = WebApplication.CreateBuilder(args);

// Register a default client
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var app = builder.Build();
```

Inject `ITurboHttpClientFactory` into your services:

```csharp
public sealed class OrderService
{
    private readonly ITurboHttpClient _client;

    public OrderService(ITurboHttpClientFactory factory)
    {
        _client = factory.CreateClient();
    }

    public async Task<Order> GetOrderAsync(int id, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}");
        var response = await _client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Order>(ct);
    }
}
```

## Named Clients

Register multiple clients with different configurations:

```csharp
// Public API — HTTP/2, caching enabled
builder.Services.AddTurboHttpClient("public-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
    options.CachePolicy = CachePolicy.Default;
});

// Internal service — HTTP/1.1, aggressive retries
builder.Services.AddTurboHttpClient("internal", options =>
{
    options.BaseAddress = new Uri("http://internal-service:8080");
    options.RetryPolicy = RetryPolicy.Default with { MaxRetries = 5 };
});
```

Resolve by name:

```csharp
public sealed class GatewayService
{
    private readonly ITurboHttpClient _publicApi;
    private readonly ITurboHttpClient _internal;

    public GatewayService(ITurboHttpClientFactory factory)
    {
        _publicApi = factory.CreateClient("public-api");
        _internal = factory.CreateClient("internal");
    }
}
```

## Typed Clients

Bind a client directly to a service class:

```csharp
builder.Services.AddTurboHttpClient<OrderService>(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.RetryPolicy = RetryPolicy.Default;
});
```

The DI container injects `ITurboHttpClient` into `OrderService` automatically.

## Fluent Builder API

Use the builder pattern to compose features:

```csharp
builder.Services.AddTurboHttpClient("full-featured", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect()                          // follow redirects (default policy)
.WithRetry(RetryPolicy.Default)          // automatic retries
.WithCookies()                           // automatic cookie management
.WithCache(CachePolicy.Default)          // HTTP caching
.WithDecompression(true);                // gzip/deflate/brotli
```

## Standalone Usage (Without DI)

For scripts, tests, or applications without a DI container:

```csharp
var actorSystem = ActorSystem.Create("turbo");
await using var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    DefaultRequestVersion = HttpVersion.Version20,
}, actorSystem);

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/health"),
    CancellationToken.None);
```

::: warning
Always `await using` the client to ensure connections are properly cleaned up.
:::

## Minimal Example

A complete console application:

```csharp
using TurboHttp.Client;
using Akka.Actor;

var actorSystem = ActorSystem.Create("turbo");
await using var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://jsonplaceholder.typicode.com"),
}, actorSystem);

var request = new HttpRequestMessage(HttpMethod.Get, "/posts/1");
var response = await client.SendAsync(request, CancellationToken.None);

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## Next Steps

- [Getting Started](./index) — basic usage patterns and feature overview
- [Configuration](./configuration) — all options explained in detail
- [API Reference](/api/) — full public API surface
