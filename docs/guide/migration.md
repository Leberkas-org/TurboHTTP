# Migrating from HttpClient

This guide shows how to migrate common `HttpClient` patterns to TurboHttp. The API is intentionally similar — most code changes are mechanical.

## Quick Comparison

| HttpClient | TurboHttp |
|---|---|
| `new HttpClient()` | `TurboHttpClientFactory.Create(opts => ...)` |
| `IHttpClientFactory` | `ITurboHttpClientFactory` |
| `services.AddHttpClient()` | `services.AddTurboHttpClient()` |
| `client.GetAsync(url)` | `client.SendAsync(new HttpRequestMessage(Get, url), ct)` |
| `DelegatingHandler` | `TurboHandler` or Akka.Streams `GraphStage` |
| Polly retry policies | Built-in `RetryPolicy` |
| No caching | Built-in `CachePolicy` |
| `CookieContainer` (manual) | `CookieJar` (automatic) |

## Basic Request

**Before (HttpClient):**

```csharp
using var client = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
var response = await client.GetAsync("/users/42");
var body = await response.Content.ReadAsStringAsync();
```

**After (TurboHttp):**

```csharp
await using var client = TurboHttpClientFactory.Create(opts =>
{
    opts.BaseAddress = new Uri("https://api.example.com");
});

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users/42"),
    CancellationToken.None);
var body = await response.Content.ReadAsStringAsync();
```

Key differences:
- `await using` instead of `using` (async disposal for connection cleanup)
- Always pass `CancellationToken` explicitly
- No shorthand methods like `GetAsync` — use `SendAsync` with `HttpRequestMessage`

## Dependency Injection

**Before (HttpClient):**

```csharp
builder.Services.AddHttpClient("my-api", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// In service:
public class MyService(IHttpClientFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient("my-api");
}
```

**After (TurboHttp):**

```csharp
builder.Services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestHeaders.Add("Accept", "application/json");
});

// In service:
public class MyService(ITurboHttpClientFactory factory)
{
    private readonly ITurboHttpClient _client = factory.CreateClient("my-api");
}
```

The pattern is nearly identical — replace `IHttpClientFactory` with `ITurboHttpClientFactory`.

## Retry Policies

**Before (HttpClient + Polly):**

```csharp
builder.Services.AddHttpClient("my-api")
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, attempt =>
            TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

**After (TurboHttp):**

```csharp
builder.Services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.RetryPolicy = RetryPolicy.Default; // 3 retries, respects Retry-After
});
```

No Polly dependency needed. TurboHttp automatically:
- Retries only idempotent methods (GET, HEAD, PUT, DELETE, OPTIONS, TRACE)
- Never retries POST or PATCH
- Respects `Retry-After` headers
- Applies exponential backoff

For custom retry logic, implement `IRetryEvaluator` (see [Advanced Usage](./advanced)).

## Cookie Management

**Before (HttpClient):**

```csharp
var handler = new HttpClientHandler
{
    CookieContainer = new CookieContainer(),
    UseCookies = true,
};
using var client = new HttpClient(handler);
```

**After (TurboHttp):**

```csharp
// Cookies are automatic — no setup needed
builder.Services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCookies(); // enable automatic cookie handling
```

TurboHttp's `CookieJar` handles domain matching, path matching, Secure/HttpOnly attributes, and expiration automatically. Each client has its own isolated jar.

## Custom Middleware

**Before (HttpClient DelegatingHandler):**

```csharp
public class LoggingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Console.WriteLine($"→ {request.Method} {request.RequestUri}");
        var response = await base.SendAsync(request, ct);
        Console.WriteLine($"← {(int)response.StatusCode}");
        return response;
    }
}

builder.Services.AddHttpClient("my-api")
    .AddHttpMessageHandler<LoggingHandler>();
```

**After (TurboHttp TurboHandler):**

```csharp
public sealed class LoggingHandler : TurboHandler
{
    public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
    {
        Console.WriteLine($"→ {request.Method} {request.RequestUri}");
        return request;
    }

    public override HttpResponseMessage ProcessResponse(
        HttpRequestMessage request, HttpResponseMessage response)
    {
        Console.WriteLine($"← {(int)response.StatusCode}");
        return response;
    }
}

builder.Services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.AddHandler<LoggingHandler>();
```

::: info
`TurboHandler.ProcessRequest` sees initial requests only (not retries or redirects).
`TurboHandler.ProcessResponse` sees final responses only (after all retries and redirects).
:::

## HTTP/2

**Before (HttpClient):**

```csharp
var handler = new SocketsHttpHandler();
using var client = new HttpClient(handler);
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

**After (TurboHttp):**

```csharp
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});
```

TurboHttp provides full HTTP/2 multiplexing — all requests to the same host share a single TCP connection with concurrent streams. See [HTTP/2 & Multiplexing](./http2).

## Timeout

**Before (HttpClient):**

```csharp
client.Timeout = TimeSpan.FromSeconds(30);
```

**After (TurboHttp):**

```csharp
client.Timeout = TimeSpan.FromSeconds(30);

// Or per-request via CancellationToken:
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var response = await client.SendAsync(request, cts.Token);
```

Same API. TurboHttp additionally respects `CancellationToken` at every layer.

## What You Get for Free

By switching to TurboHttp, these features work out of the box without additional libraries:

| Feature | HttpClient Approach | TurboHttp |
|---|---|---|
| Retries | Polly + DelegatingHandler | Built-in `RetryPolicy` |
| Caching | Custom DelegatingHandler or nothing | Built-in `CachePolicy` |
| Cookies | Manual CookieContainer setup | Automatic `CookieJar` |
| Decompression | `AutomaticDecompression` flag | Automatic (gzip, deflate, brotli) |
| Connection pooling | SocketsHttpHandler (opaque) | Actor-based, per-host, configurable |
| Backpressure | None | End-to-end via Akka.Streams |
| Channel API | None | `ChannelWriter` / `ChannelReader` |

## What You Lose

Be aware of trade-offs:

- **No `GetAsync` / `PostAsync` / `PutAsync` convenience methods** — always use `SendAsync` with `HttpRequestMessage`
- **No typed client interfaces** — if you need Refit-style interfaces, TurboHttp isn't the right tool
- **Akka.NET dependency** — adds ~5 MB to your deployment; not an issue for most apps, but worth noting for size-constrained environments
- **Learning curve** — understanding the pipeline model requires reading the [Architecture](/architecture/) docs

## Gradual Migration

You don't have to migrate everything at once. TurboHttp and `HttpClient` can coexist in the same application:

```csharp
// Keep existing HttpClient registrations
builder.Services.AddHttpClient("legacy-api", client =>
{
    client.BaseAddress = new Uri("https://old.api.com");
});

// Add TurboHttp for new services
builder.Services.AddTurboHttpClient("new-api", options =>
{
    options.BaseAddress = new Uri("https://new.api.com");
    options.RetryPolicy = RetryPolicy.Default;
    options.CachePolicy = CachePolicy.Default;
});
```

Migrate service by service, starting with the ones that benefit most from retries, caching, or HTTP/2 multiplexing.
