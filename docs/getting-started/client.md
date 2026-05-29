# Client Quick Start

Build a working TurboHTTP client in under 5 minutes.

## 1. Install

```bash
dotnet add package TurboHTTP
```

## 2. Register a Client

```csharp
using TurboHTTP.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var app = builder.Build();
```

## 3. Send a Request

```csharp
using TurboHTTP.Client;
using System.Net.Http;

var factory = app.Services.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient("api");

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);

response.EnsureSuccessStatusCode();
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## 4. Add Features

Features are opt-in via the fluent builder:

```csharp
using TurboHTTP.Client;

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()              // automatic retries for GET, PUT, DELETE
.WithCache()              // in-memory HTTP caching with ETag
.WithCookies()            // automatic cookie storage and injection
.WithRedirect()           // follow redirect chains
.WithDecompression();     // gzip/deflate/Brotli decompression
```

Each `.With*()` method adds a pipeline stage. They compose — order doesn't matter.

## 5. High-Throughput Usage

For batch processing, use the channel-based API instead of `SendAsync`:

```csharp
using TurboHTTP.Client;
using System.Net.Http;
using System.Threading.Channels;

var client = factory.CreateClient("api");

// Producer: write requests without waiting
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"));
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"));
client.Requests.Complete();

// Consumer: read responses as they arrive
await foreach (var response in client.Responses.ReadAllAsync())
{
    Console.WriteLine($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
```

With HTTP/2, all requests flow over a single TCP connection as multiplexed streams. The channel applies backpressure if the connection can't keep up.

## Next Steps

- [Installation & Setup](/client/installation) — DI registration, named clients, typed clients
- [Configuration](/client/configuration) — all client options
- [Full Client Guide](/client/) — retries, caching, cookies, HTTP/2, HTTP/3
- [Real-World Scenarios](/client/scenarios) — combined feature examples
