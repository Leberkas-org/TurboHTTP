# Middleware-Design für TurboHttp

## Was HttpClient standardmässig mitbringt

`SocketsHttpHandler` (der Default-Handler seit .NET 5) liefert folgendes out-of-the-box:

| Feature | Standard | Konfigurierbar |
|---|---|---|
| Redirect | **an** (`AllowAutoRedirect = true`, max 50) | via `MaxAutomaticRedirections` |
| Decompression | **an** (`DecompressionMethods.All`) | via `AutomaticDecompression` |
| Connection Pooling | **an** (per-Host, idle eviction) | via `PooledConnectionLifetime` etc. |
| Cookies | **aus** (`UseCookies = false`) | via `CookieContainer` |
| HTTP-Caching | **nicht vorhanden** | — (Polly / externe Library) |
| Retry | **nicht vorhanden** | — (Polly / `AddStandardResilienceHandler`) |

Die `IHttpClientFactory`-Middleware (`DelegatingHandler`) ist ein Opt-in-Mechanismus — sie setzt auf das bestehende Request/Response-Modell auf:

```csharp
services.AddHttpClient("myapi", c => c.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<LoggingHandler>()
    .AddHttpMessageHandler<AuthHandler>();
```

---

## Warum ein standalone ClientBuilder nicht passt

`DelegatingHandler` ist synchron pro Anfrage denkbar, weil `HttpClient` intern keine Streaming-Pipeline hat — jede Anfrage bekommt ihren eigenen Handler-Stack. TurboHttp ist anders:

- Die **Akka.Streams-Pipeline wird einmal materialisiert** und läuft dann als dauerhafter Graph. Es gibt keinen "Handler-Stack pro Anfrage" den man zur Laufzeit zusammenbauen könnte.
- **N Requests fliegen gleichzeitig** durch denselben Graph — kein Slot pro Request.
- **Feedback-Schleifen** für Retry und Redirect sind Kanten im Graph, keine Laufzeit-Entscheidungen.

Ein `TurboHttpClientBuilder` der beim `Build()` aufgerufen wird würde das suggerieren — ist aber falsch. Die Konfiguration muss zur **Registrierungszeit** (DI-Setup) abgeschlossen sein, damit der Graph korrekt materialisiert werden kann.

---

## Das richtige Modell: `ITurboHttpClientBuilder` auf `IServiceCollection`-Ebene

Identisch zum `IHttpClientBuilder`-Pattern aus `Microsoft.Extensions.Http`:

```csharp
public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
```

Extension-Methoden auf `IServiceCollection`:

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

Der Rückgabewert ist `ITurboHttpClientBuilder` — darauf werden alle weiteren Optionen als Extension-Methoden registriert. Der Graph wird nicht hier, sondern erst beim ersten `CreateClient(name)` des Factory materialisiert.

> **Veraltete API:** `AddTurboHttpClientFactory(services, configure)` ist mit `[Obsolete]` markiert
> und wird in einer künftigen Version entfernt. Neu: `services.AddTurboHttpClient(name, configure)`.

---

## Built-in Features als Extension-Methoden

Analog zu `AddStandardResilienceHandler()` / `ConfigurePrimaryHttpMessageHandler()`:

```csharp
services.AddTurboHttpClient("myapi", options => { ... })
    // Redirect ist standardmässig aus, opt-in
    .WithRedirect()                                  // Default-Policy: max 10, kein HTTPS→HTTP Downgrade
    .WithRedirect(new RedirectPolicy(MaxRedirects: 20))

    // Cookies: aus by default, opt-in
    .WithCookies()                                   // shared CookieJar für diesen Client
    .WithCookies(existingJar)                        // eigene CookieJar-Instanz einbringen

    // Cache: aus by default, opt-in
    .WithCache(new CachePolicy(MaxEntries: 1000))

    // Retry: aus by default, opt-in (wie HttpClient — Polly macht's analog)
    .WithRetry(new RetryPolicy(MaxRetries: 3));
```

Diese Methoden tragen ihre Konfiguration lediglich in `IServiceCollection` ein (als `IOptions`/`IConfigureOptions`). Die `TurboHttpClientFactory` liest beim `CreateClient()` alle registrierten Optionen aus und übergibt sie der Engine.

> **Hinweis:** `TurboClientOptions.RedirectPolicy`, `RetryPolicy` und `CachePolicy` sind backward-kompatibel
> und werden in einer künftigen Version mit `[Obsolete]` markiert. Neu-Code sollte stattdessen die
> Builder-Extensions (`.WithRedirect()`, `.WithRetry()`, `.WithCache()`) verwenden.

---

## User-Middleware

Statt `DelegatingHandler` bekommt TurboHttp eine eigene, stream-kompatible Middleware-Abstraktion. Die Schnittstelle ist bewusst einfach gehalten — kein Akka-Wissen erforderlich:

```csharp
public abstract class TurboMiddleware
{
    // Optional: Request-Transform — Standard ist Pass-through
    public virtual ValueTask<HttpRequestMessage> ProcessRequestAsync(
        HttpRequestMessage request,
        CancellationToken ct) => ValueTask.FromResult(request);

    // Optional: Response-Transform — Standard ist Pass-through
    public virtual ValueTask<HttpResponseMessage> ProcessResponseAsync(
        HttpRequestMessage original,
        HttpResponseMessage response,
        CancellationToken ct) => ValueTask.FromResult(response);
}
```

Registrierung via DI und `ITurboHttpClientBuilder`:

```csharp
// Klasse — wird per DI aufgelöst (kann Dependencies injecten)
// AddMiddleware<T>() registriert T automatisch als Transient in IServiceCollection
services.AddTurboHttpClient("myapi", options => { ... })
    .AddMiddleware<AuthMiddleware>()
    .AddMiddleware<LoggingMiddleware>()
    .AddMiddleware<CorrelationIdMiddleware>();

// Inline-Delegate für einfache Fälle
services.AddTurboHttpClient("myapi", options => { ... })
    .UseRequest((req, ct) =>
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.FromResult(req);
    })
    .UseResponse((req, resp, ct) =>
    {
        metrics.Record(req.RequestUri!, resp.StatusCode);
        return ValueTask.FromResult(resp);
    });
```

`AddMiddleware<T>()` registriert `T` als `Transient` in `IServiceCollection` und merkt sich die Reihenfolge. `UseRequest`/`UseResponse` wrappen den Delegate direkt — kein separater DI-Eintrag nötig. Beim Materialisieren wird pro registrierter Middleware eine Stage in die Akka-Pipeline eingefügt.

---

## Wo in der Pipeline läuft User-Middleware

```
[RequestEnricher]          ← BaseAddress, DefaultHeaders, Version
      ↓
[User-Middleware Request]  ← ProcessRequestAsync — Auth, Correlation-ID, Custom-Headers
      ↓                       (nur initiale Requests; Redirect-Feedback tritt NACH dieser
      |                        Stelle in die Pipeline ein und bypasses die Middleware)
[CookieInjection]          ← .WithCookies()
      ↓                       (Retry-Feedback tritt NACH dieser Stelle ein)
[CacheLookup]              ← .WithCache()
      ↓
── ASYNC BOUNDARY ──
      ↓
[Protocol Engine]          ← HTTP/1.0 / 1.1 / 2.0
[Decompression]
      ↓
── ASYNC BOUNDARY ──
      ↓
[CookieStorage]            ← .WithCookies()
[CacheStorage]             ← .WithCache()
[RetryStage]               ← .WithRetry()   → retry feedback (zurück zu CacheLookup)
[RedirectStage]            ← .WithRedirect() → redirect feedback (zurück zu CookieInjection)
      ↓
[User-Middleware Response] ← ProcessResponseAsync — Logging, Metrics, Tracing
      ↓                       (nur finale Responses — nach Redirect und Retry)
[Client]
```

User-Middleware läuft absichtlich **ausserhalb** der Feedback-Schleifen:
- `ProcessRequestAsync` sieht jeden angereicherten initialen Request. Redirect-Requests (die der `RedirectStage` zurückschickt) gehen direkt in den `redirectMerge` und überspringen die Middleware.
- `ProcessResponseAsync` sieht nur **finale** Responses — nach Redirect und Retry abgearbeitet. Keine Zwischenergebnisse, kein interner Lärm.

---

## Vollständiges Beispiel

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
    .AddMiddleware<AuthMiddleware>()         // registriert AuthMiddleware als Transient
    .AddMiddleware<ObservabilityMiddleware>(); // registriert ObservabilityMiddleware als Transient

// Irgendwo im Code:
public class PaymentService(ITurboHttpClientFactory factory)
{
    private readonly ITurboHttpClient _client = factory.CreateClient("payments");
}
```

```csharp
// Eigene Middleware
public sealed class AuthMiddleware(ITokenProvider tokens) : TurboMiddleware
{
    public override async ValueTask<HttpRequestMessage> ProcessRequestAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
```

---

## Unterschied zu `TurboClientOptions`

`TurboClientOptions` bleibt als **Transport-Konfiguration** bestehen (Timeouts, TLS, Reconnect-Intervalle). Die Middleware-Konfiguration (Cookies, Cache, Retry, Redirect, User-Middleware) wandert vollständig in die `ITurboHttpClientBuilder`-Extensions.

| Konfigurationsart | Wo |
|---|---|
| Verbindungsparameter (Timeouts, TLS, HTTP/2 Frame-Size) | `TurboClientOptions` via `AddTurboHttpClient(name, options => ...)` |
| Redirect / Retry / Cookie / Cache | `ITurboHttpClientBuilder` Extensions (`.WithRedirect()` etc.) |
| User-Middleware | `ITurboHttpClientBuilder` (`.AddMiddleware<T>()`) |
| DefaultRequestHeaders / BaseAddress / Version | `TurboClientOptions` |

---

## Interne Umsetzung: Engine-Parametrisierung

### `TurboClientDescriptor`

Ein internes, mutabless Objekt, das alle per-`ITurboHttpClientBuilder` registrierten Einstellungen sammelt. Es wird von `IConfigureNamedOptions` in-place mutiert und beim `CreateClient()` ausgelesen:

```csharp
internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public bool EnableCookies { get; set; }
    public CookieJar? CustomCookieJar { get; set; }
    public CachePolicy? CachePolicy { get; set; }

    // Typ-basierte Middleware (AddMiddleware<T>) — für DI-Lookup nach Typ
    public List<Type> MiddlewareTypes { get; } = [];

    // Unified FIFO factory list: deckt type-based (AddMiddleware<T>) UND
    // delegate-based (UseRequest/UseResponse) Middleware ab.
    // AddMiddleware<T> trägt sich in BEIDE Listen ein; UseRequest/UseResponse nur hier.
    public List<Func<IServiceProvider, TurboMiddleware>> MiddlewareFactories { get; } = [];
}
```

### `PipelineDescriptor`

Ein immutabler Record, der vom Factory zur Engine übergeben wird. Er enthält alle materialisierten Objekte — CookieJar-Instanz, CacheStore-Instanz, aufgelöste Middleware-Instanzen:

```csharp
internal sealed record PipelineDescriptor(
    RedirectPolicy?  RedirectPolicy,
    RetryPolicy?     RetryPolicy,
    CookieJar?       CookieJar,
    HttpCacheStore?  CacheStore,
    IReadOnlyList<TurboMiddleware> Middlewares)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        CookieJar: null,
        CacheStore: null,
        Middlewares: []);
}
```

`Engine.CreateFlow()` bekommt diesen Descriptor übergeben und verdrahtet nur die Stages, die tatsächlich konfiguriert sind — kein versteckter Overhead bei leerer Pipeline. Die Engine selbst bleibt intern — kein Akka-Wissen tritt nach aussen.

---

## Vergleich: HttpClient vs. TurboHttp

| Aspekt | HttpClient | TurboHttp (nach Umbau) |
|---|---|---|
| Registrierung | `services.AddHttpClient("name", ...)` | `services.AddTurboHttpClient("name", ...)` |
| Middleware | `.AddHttpMessageHandler<T>()` | `.AddMiddleware<T>()` |
| Redirect | an by default | aus — opt-in via `.WithRedirect()` |
| Retry | aus — Polly via `.AddStandardResilienceHandler()` | aus — opt-in via `.WithRetry(policy)` |
| Cache | nicht vorhanden | aus — opt-in via `.WithCache(policy)` |
| Cookies | aus (SocketsHttpHandler) | aus — opt-in via `.WithCookies()` |
| Middleware-Basis | `DelegatingHandler` (sync/async, pro Request) | `TurboMiddleware` (async, stream-kompatibel) |
| Factory | `IHttpClientFactory` | `ITurboHttpClientFactory` |
| Typed Clients | `AddHttpClient<TClient>()` | `AddTurboHttpClient<TClient>()` |
