# Real-World Client Scenarios

This page shows how to combine multiple TurboHTTP features to solve common application challenges. Each scenario includes complete DI registration and usage examples.

## Authenticated REST API Client

**The problem:** Your app calls a protected REST API. You need to maintain a Bearer token, handle transient failures gracefully, and avoid fetching the same resource twice.

**Features in play:** `.UseRequest()` for token injection, `.WithRetry()` for automatic failure recovery, `.WithCache()` for response reuse.

```csharp
// DI Registration
builder.Services.AddTurboHttpClient("rest-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.ConnectTimeout = TimeSpan.FromSeconds(5);
})
.UseRequest(req =>
{
    // Inject Bearer token into every outgoing request
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAccessToken());
    return req;
})
.WithRetry(retry =>
{
    retry.MaxRetries = 5;
    retry.RespectRetryAfter = true;  // honour Retry-After from server
})
.WithCache(cache =>
{
    cache.MaxEntries = 500;
    cache.MaxBodyBytes = 10 * 1024 * 1024;  // 10 MiB
});
```

Usage:

```csharp
public class ApiService(ITurboHttpClientFactory factory)
{
    public async Task<UserDto> GetUserAsync(int userId, CancellationToken ct)
    {
        var client = factory.CreateClient("rest-api");
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"/users/{userId}");
        var response = await client.SendAsync(request, ct);
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<UserDto>(json)!;
    }
}

private string GetAccessToken()
{
    // Fetch from secure token store, refresh if expired, etc.
    return _tokenProvider.GetToken();
}
```

**How they interact:**

- **Request injection:** `UseRequest()` runs first, adding your Bearer token to every outgoing request before any feature processes it.
- **Retries:** If a GET fails due to transient error (503, connection drop, timeout), `.WithRetry()` automatically reattempts up to 5 times. The Bearer token is re-injected on each attempt via `UseRequest()`.
- **Caching:** GET responses that include freshness headers (e.g. `Cache-Control: max-age=300`) are stored. The next request for the same URL returns the cached response without a network round-trip. Once stale, TurboHTTP sends a conditional request (`If-None-Match`, `If-Modified-Since`) to avoid re-downloading unchanged content.

::: tip Use cases
Microservice-to-API calls, third-party API integrations, data fetching in background jobs.
:::

---

## Web Scraper with Session Cookies

**The problem:** You scrape a website that requires login. After posting credentials, subsequent page requests must include the session cookie automatically. Responses may be compressed and include redirects.

**Features in play:** `.WithCookies()` for automatic cookie storage/injection, `.WithRedirect()` for following redirects, `.WithDecompression()` for gzip/deflate/Brotli.

```csharp
// DI Registration
builder.Services.AddTurboHttpClient("scraper", options =>
{
    options.BaseAddress = new Uri("https://example.com");
    options.ConnectTimeout = TimeSpan.FromSeconds(10);
})
.WithCookies()  // No configuration needed — handles Set-Cookie automatically
.WithRedirect(redirect =>
{
    redirect.MaxRedirects = 5;
})
.WithDecompression();  // Decompress gzip, deflate, Brotli automatically
```

Usage:

```csharp
public class ScraperService(ITurboHttpClientFactory factory)
{
    public async Task ScrapeSiteAsync(CancellationToken ct)
    {
        var client = factory.CreateClient("scraper");
        
        // Step 1: Login — POST credentials
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/login");
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "username", "myusername" },
            { "password", "mypassword" }
        });
        
        var loginResponse = await client.SendAsync(loginRequest, ct);
        loginResponse.EnsureSuccessStatusCode();
        
        // The server's Set-Cookie is now stored in the client's CookieJar
        
        // Step 2: Request a protected page — cookie is automatically injected
        var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/profile");
        var pageResponse = await client.SendAsync(pageRequest, ct);
        pageResponse.EnsureSuccessStatusCode();
        
        var html = await pageResponse.Content.ReadAsStringAsync(ct);
        Console.WriteLine(html);
    }
}
```

**How they interact:**

- **Cookies:** After the POST request, the server responds with `Set-Cookie: session=abc123; Domain=example.com; Path=/`. TurboHTTP stores this automatically. All subsequent requests to `example.com` (at any path) now include `Cookie: session=abc123` in the request header.
- **Redirects:** If the server responds with a 301/302 (e.g. `Location: /profile/dashboard`), `.WithRedirect()` automatically follows it. The cookie from login is carried forward to the redirect target.
- **Decompression:** If the response includes `Content-Encoding: gzip`, `.WithDecompression()` transparently decompresses it. Your code reads the plain text HTML without worrying about compression.

::: warning Important
Each client has its own independent cookie jar. If you create multiple scraper clients, they do not share session state. Reuse the same client instance across login and subsequent requests.
:::

::: tip Use cases
Web scraping, automated testing against web apps, session-based client applications.
:::

---

## High-Throughput Batch Processor

**The problem:** You have 10,000 URLs to fetch. You want to issue many requests without blocking on each response, leverage HTTP/2 multiplexing for parallelism, and handle transient failures.

**Features in play:** Channel API (`.Requests`, `.Responses`) for producer/consumer pattern, `.WithRetry()` for resilience, HTTP/2 multiplexing configuration.

```csharp
// DI Registration
builder.Services.AddTurboHttpClient("batch-processor", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.Http2.MaxConcurrentStreams = 100;  // up to 100 concurrent streams per connection
    options.Http2.MaxConnectionsPerServer = 2;   // reuse 2 connections
})
.WithRetry(retry =>
{
    retry.MaxRetries = 3;
    retry.RespectRetryAfter = true;
});
```

Usage:

```csharp
public class BatchProcessor(ITurboHttpClientFactory factory)
{
    public async Task ProcessUrlsAsync(List<string> urls, CancellationToken ct)
    {
        var client = factory.CreateClient("batch-processor");
        client.DefaultRequestVersion = HttpVersion.Version20;  // default to HTTP/2 (set on the client instance, not options)
        
        var results = new ConcurrentBag<(string Url, int Status, string Body)>();
        
        // Producer task: write all requests to the channel without waiting for responses
        var producer = Task.Run(async () =>
        {
            foreach (var url in urls)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                await client.Requests.WriteAsync(request, ct);
            }
            client.Requests.Complete();  // Signal end of requests
        }, ct);
        
        // Consumer task: read responses as they arrive and process them
        var consumer = Task.Run(async () =>
        {
            await foreach (var response in client.Responses.ReadAllAsync(ct))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                results.Add((response.RequestMessage?.RequestUri?.ToString() ?? "", (int)response.StatusCode, body));
                response.Dispose();
            }
        }, ct);
        
        await Task.WhenAll(producer, consumer);
        
        Console.WriteLine($"Processed {results.Count} URLs");
        foreach (var (url, status, _) in results)
        {
            Console.WriteLine($"  {url}: {status}");
        }
    }
}
```

**How they interact:**

- **Channel API:** The producer writes 10,000 requests to `client.Requests` (a bounded channel) as fast as it can, without waiting. The consumer reads from `client.Responses` in parallel. TurboHTTP ensures requests are sent and responses are processed concurrently.
- **Backpressure:** If the producer writes faster than the connection can send requests, the channel fills. `WriteAsync()` blocks until there is room, preventing memory exhaustion.
- **HTTP/2 multiplexing:** With `MaxConcurrentStreams = 100` and `MaxConnectionsPerServer = 2`, TurboHTTP reuses 2 TCP connections and multiplexes up to 100 requests at a time over each connection. This is far more efficient than HTTP/1.1's 6 connections × 1 request per connection = 6 concurrent requests.
- **Retries:** If a GET fails transiently, `.WithRetry()` automatically reattempts up to 3 times without blocking the consumer loop. Retried responses are enqueued like any other, preserving order of completion.

::: warning Thread safety
`ConcurrentBag<T>` is thread-safe for concurrent add/enumerate, but the producer and consumer run independently. Always use thread-safe collections when results come from concurrent processing.
:::

::: tip Use cases
Batch URL fetching, parallel API polling, high-throughput data ingestion, distributed data collection.
:::

---

## Microservice Communication

**The problem:** Your service calls another internal service over HTTP/2. Connection setup needs a 5-second timeout, individual requests have a 10-second timeout. If the service is briefly unavailable, retry automatically.

**Features in play:** `ConnectTimeout` (in options) + `Timeout` (on client instance) for timeout management, `.WithRetry()` for resilience, HTTP/2 for efficiency.

```csharp
// DI Registration
builder.Services.AddTurboHttpClient("internal-service", options =>
{
    options.BaseAddress = new Uri("http://internal-service:8080");
    options.ConnectTimeout = TimeSpan.FromSeconds(5);    // TCP connect timeout
})
.WithDecompression()  // some responses may be compressed
.WithRetry(retry =>
{
    retry.MaxRetries = 2;  // 2 retries = 3 attempts total
    retry.RespectRetryAfter = true;
});
```

Then set the request timeout on the client instance:

```csharp
var client = factory.CreateClient("internal-service");
client.Timeout = TimeSpan.FromSeconds(10);  // per-request timeout
client.DefaultRequestVersion = HttpVersion.Version20;  // default to HTTP/2 (set on the client instance, not options)
```

Usage:

```csharp
public class OrderService(ITurboHttpClientFactory factory)
{
    public async Task<OrderDto> GetOrderAsync(string orderId, CancellationToken ct)
    {
        var client = factory.CreateClient("internal-service");
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
        var response = await client.SendAsync(request, ct);
        
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<OrderDto>(json)!;
    }
}
```

**How they interact:**

- **Connect timeout:** When TurboHTTP establishes a TCP connection to `internal-service:8080`, it waits at most 5 seconds. If the connection is not established in that time, the request fails immediately.
- **Request timeout:** Once connected, TurboHTTP sends the request and waits up to 10 seconds for a response. If the server is slow to respond, the request is cancelled after 10 seconds.
- **Retry with respect for Retry-After:** If the service responds with `503 Service Unavailable` and includes `Retry-After: 2`, TurboHTTP automatically waits 2 seconds and retries (up to 2 times). This is transparent to your code.
- **HTTP/2:** The connection is kept alive and reused for subsequent requests over the same underlying TCP socket. HTTP/2 multiplexing allows multiple in-flight requests at once.
- **Decompression:** If responses are gzip-compressed, `.WithDecompression()` transparently decompresses them.

::: tip Use cases
Internal service-to-service communication, calling backend APIs from frontend services, microservice orchestration.
:::

---

## Direct Channel-Based Processing

**The problem:** You want to drive request/response processing yourself without `SendAsync()` — perhaps to implement custom backpressure logic, or to coordinate TurboHTTP with other async systems.

**Features in play:** `client.Requests` (a `ChannelWriter<HttpRequestMessage>`) and `client.Responses` (a `ChannelReader<HttpResponseMessage>`).

TurboHTTP's channel API lets you:

1. Write requests directly to `client.Requests.WriteAsync(request)` instead of calling `SendAsync()`
2. Read responses from `client.Responses.ReadAllAsync()` in a loop
3. Both channels support `CancellationToken` for cancellation

This is useful if you want to coordinate request submission and response collection independently, or if you're building a custom orchestration layer. Example:

```csharp
var client = factory.CreateClient("my-client");

// Producer task: submit requests without waiting for responses
var producer = Task.Run(async () =>
{
    foreach (var url in urls)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await client.Requests.WriteAsync(request, ct);
    }
    client.Requests.Complete();  // Signal no more requests
}, ct);

// Consumer task: process responses as they arrive
var consumer = Task.Run(async () =>
{
    await foreach (var response in client.Responses.ReadAllAsync(ct))
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Console.WriteLine($"{response.StatusCode}: {body}");
        response.Dispose();
    }
}, ct);

await Task.WhenAll(producer, consumer);
```

For stream processing with backpressure, throttling, merging, or fan-out — use Akka.Streams directly with Akka.Streams adapters, or write your own adapter that bridges the channels to your stream DSL.

---

## Combining These Patterns

The scenarios above show different feature combinations, but there is no rule against mixing them further. For example:

- **Authenticated batch processor:** Add `.UseRequest()` to inject a Bearer token into every request in a batch job.
- **Cached microservice:** Add `.WithCache()` to an internal service call to avoid redundant backend queries.
- **Resilient scraper:** Add `.WithRetry()` to a web scraper to handle intermittent page server errors.

Each feature composes with the others. Start with the scenario that matches your use case most closely, then add features as needed.

::: info How it works
See [Architecture: Request Pipeline](/architecture/pipeline) to understand how all these features are wired together in the processing pipeline.
:::
