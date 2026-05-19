# ITurboHttpClient

TurboHTTP exposes a small, focused public API. The primary entry point is `ITurboHttpClient`, obtained via `ITurboHttpClientFactory`.

## ITurboHttpClientFactory

```csharp
public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(string name);
}
```

Registered via dependency injection. Resolve a named client by passing the name used at registration:

```csharp
// Default (unnamed) client — registered with AddTurboHttpClient()
var client = factory.CreateClient();          // extension method: CreateClient(string.Empty)

// Named client — registered with AddTurboHttpClient("search", ...)
var searchClient = factory.CreateClient("search");
```

See [Configuration guide](/client/configuration) for DI setup and named client registration.

---

## ITurboHttpClient

```csharp
public interface ITurboHttpClient : IDisposable
{
    Uri? BaseAddress { get; set; }
    HttpRequestHeaders DefaultRequestHeaders { get; }
    Version DefaultRequestVersion { get; set; }
    HttpVersionPolicy DefaultVersionPolicy { get; set; }
    TimeSpan Timeout { get; set; }
    ChannelWriter<HttpRequestMessage> Requests { get; }
    ChannelReader<HttpResponseMessage> Responses { get; }
    
    void CancelPendingRequests();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
```

### BaseAddress

Base address used to resolve relative URIs. When set, relative URIs in `SendAsync` and `Requests` are combined with this base:

```csharp
client.BaseAddress = new Uri("https://api.example.com/v2/");

// Resolves to https://api.example.com/v2/users/42
var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "users/42"), ct);
```

### DefaultRequestHeaders

Headers added to every outgoing request. Useful for authentication tokens, `User-Agent`, or `Accept`:

```csharp
client.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);
```

### DefaultRequestVersion and DefaultVersionPolicy

Controls which HTTP version is used. `DefaultRequestVersion` sets the preferred version; `DefaultVersionPolicy` controls the negotiation behaviour:

```csharp
// Force HTTP/2 (fails if server doesn't support it)
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

// Prefer HTTP/2, fall back to HTTP/1.1
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

Per-request version overrides are also supported via `HttpRequestMessage.Version` and `HttpRequestMessage.VersionPolicy`.

See [HTTP/2 & Multiplexing guide](/client/http2) for multiplexing details.

### Timeout

Per-request timeout applied by `SendAsync`. Defaults to 60 seconds. Does not affect the channel-based API:

```csharp
client.Timeout = TimeSpan.FromSeconds(30);

// Times out after 30 s:
var response = await client.SendAsync(request, CancellationToken.None);
```

### Requests and Responses channels

High-throughput channel API for submitting many requests without waiting for individual responses:

```csharp
// Producer: write multiple requests without awaiting responses
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"), ct);
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"), ct);

// Consumer: read responses as they arrive
await foreach (var response in client.Responses.ReadAllAsync(ct))
{
    Console.WriteLine($"{response.RequestMessage?.RequestUri} → {response.StatusCode}");
}
```

Requests are matched to responses in submission order (HTTP/1.x) or by stream ID (HTTP/2). See [Getting Started guide](/client/#high-throughput-usage) for batch patterns and backpressure.

### CancelPendingRequests

Cancels all in-flight `SendAsync` calls and clears the pending request map. Does not affect the channel-based API:

```csharp
// Cancel everything in-flight (e.g., on application shutdown)
client.CancelPendingRequests();
```

### SendAsync

Sends a single request and returns the response. Internally writes to `Requests` and awaits the matching response. The call respects `Timeout` and the provided `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
{
    Content = JsonContent.Create(new { ProductId = 42, Quantity = 1 })
};
var response = await client.SendAsync(request, cts.Token);
response.EnsureSuccessStatusCode();
var order = await response.Content.ReadFromJsonAsync<Order>(cts.Token);
```
