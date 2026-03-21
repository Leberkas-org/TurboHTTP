# Middleware Design for TurboHttp

## What HttpClient Provides Out of the Box

`SocketsHttpHandler` (the default handler since .NET 5) ships with the following features out of the box:

| Feature | Default | Configurable |
|---|---|---|
| Redirect | **on** (`AllowAutoRedirect = true`, max 50) | via `MaxAutomaticRedirections` |
| Decompression | **on** (`DecompressionMethods.All`) | via `AutomaticDecompression` |
| Connection Pooling | **on** (per-host, idle eviction) | via `PooledConnectionLifetime` etc. |
| Cookies | **off** (`UseCookies = false`) | via `CookieContainer` |
| HTTP Caching | **not available** | — (Polly / external library) |
| Retry | **not available** | — (Polly / `AddStandardResilienceHandler`) |

The `IHttpClientFactory` middleware (`DelegatingHandler`) is an opt-in mechanism — it builds on top of the existing request/response model:

```csharp
services.AddHttpClient("myapi", c => c.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<LoggingHandler>()
    .AddHttpMessageHandler<AuthHandler>();
```

---

## Why a Standalone ClientBuilder Does Not Fit

`DelegatingHandler` is conceivable as synchronous per-request because `HttpClient` has no internal streaming pipeline — each request gets its own handler stack. TurboHttp is different:

- The **Akka.Streams pipeline is materialized once** and then runs as a persistent graph. There is no "per-request handler stack" that can be assembled at runtime.
- **N requests fly concurrently** through the same graph — no per-request slot.
- **Feedback loops** for retry and redirect are edges in the graph, not runtime decisions.

A `TurboHttpClientBuilder` called at `Build()` time would suggest otherwise — but that would be wrong. Configuration must be finalized at **registration time** (DI setup) so the graph can be materialized correctly.

---

## The Right Model: `ITurboHttpClientBuilder` on `IServiceCollection` Level

Identical to the `IHttpClientBuilder` pattern from `Microsoft.Extensions.Http`:

```csharp
public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
```

Extension methods on `IServiceCollection`:

```csharp
// Named Client
services.AddTurboHttpClient("myapi", options =>
{
    options.BaseAddress          = new Uri("https://api.example.com");
    options.ConnectTimeout       = TimeSpan.FromSeconds(5);
    options.DefaultRequestVersion = HttpVersion.Version20;
});

// Typed Client
services.AddTurboHttpClient<IGitHubClient, GitHubClient>(options =>
{
    options.BaseAddress = new Uri("https://api.github.com");
});
```

The return value is `ITurboHttpClientBuilder` — all further options are registered as extension methods on it. The graph is not materialized here, but on the first `CreateClient(name)` call of the factory.

> **Deprecated API:** `AddTurboHttpClient(services, configure)` is marked `[Obsolete]`
> and will be removed in a future version. Use `services.AddTurboHttpClient(name, configure)` instead.

---

## Built-in Features as Extension Methods

Analogous to `AddStandardResilienceHandler()` / `ConfigurePrimaryHttpMessageHandler()`:

```csharp
services.AddTurboHttpClient("myapi", options => { ... })
    // Redirect is off by default, opt-in
    .WithRedirect()                                  // Default policy: max 10, no HTTPS→HTTP downgrade
    .WithRedirect(new RedirectPolicy(MaxRedirects: 20))

    // Cookies: off by default, opt-in
    .WithCookies()                                   // Shared CookieJar for this client
    .WithCookies(existingJar)                        // Bring your own CookieJar instance

    // Cache: off by default, opt-in
    .WithCache(new CachePolicy(MaxEntries: 1000))

    // Retry: off by default, opt-in (like HttpClient — Polly does the same)
    .WithRetry(new RetryPolicy(MaxRetries: 3));
```

These methods only register their configuration in `IServiceCollection` (as `IOptions`/`IConfigureOptions`). The `TurboHttpClientFactory` reads all registered options at `CreateClient()` time and passes them to the engine.

> **Note:** `TurboClientOptions.RedirectPolicy`, `RetryPolicy`, and `CachePolicy` are backward-compatible
> and will be marked `[Obsolete]` in a future version. New code should use the
> builder extensions (`.WithRedirect()`, `.WithRetry()`, `.WithCache()`) instead.

---

## User Middleware

Instead of `DelegatingHandler`, TurboHttp provides its own stream-compatible middleware abstraction. The interface is intentionally simple — no Akka knowledge required:

```csharp
public abstract class TurboHandler
{
    // Optional: request transform — default is pass-through
    public virtual HttpRequestMessage ProcessRequest(
        HttpRequestMessage request) => request;

    // Optional: response transform — default is pass-through
    public virtual HttpResponseMessage ProcessResponse(
        HttpRequestMessage original,
        HttpResponseMessage response) => response;
}
```

Registration via DI and `ITurboHttpClientBuilder`:

```csharp
// Class — resolved via DI (can inject dependencies)
// AddHandler<T>() automatically registers T as Transient in IServiceCollection
services.AddTurboHttpClient("myapi", options => { ... })
    .AddHandler<AuthHandler>()
    .AddHandler<LoggingHandler>()
    .AddHandler<CorrelationIdHandler>();

// Inline delegate for simple cases
services.AddTurboHttpClient("myapi", options => { ... })
    .UseRequest(async (req, ct) =>
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    })
    .UseResponse(async (original, resp, ct) =>
    {
        metrics.Record(original.RequestUri!, resp.StatusCode);
        return resp;
    });
```

`AddHandler<T>()` registers `T` as `Transient` in `IServiceCollection` and records the order. `UseRequest`/`UseResponse` wrap the delegate directly — no separate DI entry needed. During materialization, one stage per registered handler is inserted into the Akka pipeline.

---

## Where User Handlers Run in the Pipeline

```
[RequestEnricher]          ← BaseAddress, DefaultHeaders, Version
      ↓
[User-Handler Request]     ← ProcessRequest — Auth, Correlation-ID, Custom-Headers
      ↓                       (initial requests only; redirect feedback enters the pipeline
      |                        AFTER this point and bypasses the handler)
[CookieBidiStage]          ← .WithCookies()
      ↓                       (retry feedback enters AFTER this point)
[CacheBidiStage]           ← .WithCache()
      ↓
── ASYNC BOUNDARY ──
      ↓
[Protocol Engine]          ← HTTP/1.0 / 1.1 / 2.0
[Decompression]
      ↓
── ASYNC BOUNDARY ──
      ↓
[CookieBidiStage]          ← .WithCookies()
[CacheBidiStage]           ← .WithCache()
[RetryBidiStage]           ← .WithRetry()   → retry feedback (back to CacheBidiStage)
[RedirectBidiStage]        ← .WithRedirect() → redirect feedback (back to CookieBidiStage)
      ↓
[User-Handler Response]    ← ProcessResponse — Logging, Metrics, Tracing
      ↓                       (final responses only — after redirect and retry are resolved)
[Client]
```

User handlers intentionally run **outside** the feedback loops:
- `ProcessRequest` sees each enriched initial request. Redirect requests (sent back by `RedirectBidiStage`) go directly into the `redirectMerge` and bypass the handler.
- `ProcessResponse` sees only **final** responses — after redirect and retry have been resolved. No intermediate results, no internal noise.

---

## Complete Example

```csharp
// Program.cs / Startup.cs

services.AddTurboHttpClient("payments", options =>
    {
        options.BaseAddress          = new Uri("https://api.payments.example.com");
        options.ConnectTimeout       = TimeSpan.FromSeconds(3);
        options.DefaultRequestVersion = HttpVersion.Version20;
    })
    .WithRedirect()
    .WithRetry(new RetryPolicy(MaxRetries: 2))
    .AddHandler<AuthHandler>()          // registers AuthHandler as Transient
    .AddHandler<ObservabilityHandler>(); // registers ObservabilityHandler as Transient

// Somewhere in application code:
public class PaymentService(ITurboHttpClientFactory factory)
{
    private readonly ITurboHttpClient _client = factory.CreateClient("payments");
}
```

```csharp
// Custom handler
public sealed class AuthHandler(ITokenProvider tokens) : TurboHandler
{
    public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetToken());
        return request;
    }
}
```

---

## Difference from `TurboClientOptions`

`TurboClientOptions` remains as **transport configuration** (timeouts, TLS, reconnect intervals). Handler configuration (cookies, cache, retry, redirect, user handlers) moves entirely into the `ITurboHttpClientBuilder` extensions.

| Configuration Type | Where |
|---|---|
| Connection parameters (timeouts, TLS, HTTP/2 frame size) | `TurboClientOptions` via `AddTurboHttpClient(name, options => ...)` |
| Redirect / Retry / Cookie / Cache | `ITurboHttpClientBuilder` extensions (`.WithRedirect()` etc.) |
| User handlers | `ITurboHttpClientBuilder` (`.AddHandler<T>()`) |
| DefaultRequestHeaders / BaseAddress / Version | `TurboClientOptions` |

---

## Internal Implementation: Engine Parameterization

### `TurboClientDescriptor`

An internal, mutable object that collects all settings registered per `ITurboHttpClientBuilder`. It is mutated in-place by `IConfigureNamedOptions` and read at `CreateClient()` time:

```csharp
internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public bool EnableCookies { get; set; }
    public CookieJar? CustomCookieJar { get; set; }
    public CachePolicy? CachePolicy { get; set; }

    // Type-based handlers (AddHandler<T>) — for DI lookup by type
    public List<Type> HandlerTypes { get; } = [];

    // Unified FIFO factory list: covers both type-based (AddHandler<T>) AND
    // delegate-based (UseRequest/UseResponse) handlers.
    // AddHandler<T> registers into BOTH lists; UseRequest/UseResponse only here.
    public List<Func<IServiceProvider, TurboHandler>> HandlerFactories { get; } = [];
}
```

### `PipelineDescriptor`

An immutable record passed from the factory to the engine. It contains all materialized objects — CookieJar instance, CacheStore instance, resolved middleware instances:

```csharp
internal sealed record PipelineDescriptor(
    RedirectPolicy?  RedirectPolicy,
    RetryPolicy?     RetryPolicy,
    CookieJar?       CookieJar,
    HttpCacheStore?  CacheStore,
    IReadOnlyList<TurboHandler> Handlers)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        CookieJar: null,
        CacheStore: null,
        Handlers: []);
}
```

`Engine.CreateFlow()` receives this descriptor and wires up only the stages that are actually configured — no hidden overhead for an empty pipeline. The engine itself remains internal — no Akka knowledge leaks out.

---

## Comparison: HttpClient vs. TurboHttp

| Aspect | HttpClient | TurboHttp |
|---|---|---|
| Registration | `services.AddHttpClient("name", ...)` | `services.AddTurboHttpClient("name", ...)` |
| Handlers | `.AddHttpMessageHandler<T>()` | `.AddHandler<T>()` |
| Redirect | on by default | off — opt-in via `.WithRedirect()` |
| Retry | off — Polly via `.AddStandardResilienceHandler()` | off — opt-in via `.WithRetry(policy)` |
| Cache | not available | off — opt-in via `.WithCache(policy)` |
| Cookies | off (SocketsHttpHandler) | off — opt-in via `.WithCookies()` |
| Handler base | `DelegatingHandler` (sync/async, per request) | `TurboHandler` (async, stream-compatible) |
| Factory | `IHttpClientFactory` | `ITurboHttpClientFactory` |
| Typed Clients | `AddHttpClient<TClient>()` | `AddTurboHttpClient<TClient>()` |