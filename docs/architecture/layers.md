# Client API

TurboHttp provides a simple, familiar interface for making HTTP requests.

## SendAsync

The standard way to send a request:

```csharp
var client = new TurboHttpClient(options);
var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users/123");
var response = await client.SendAsync(request);

Console.WriteLine(response.StatusCode);
```

`SendAsync` behaves like `HttpClient.SendAsync`:
- Takes an `HttpRequestMessage`
- Returns a `Task<HttpResponseMessage>`
- Supports `CancellationToken` for cancellation
- Respects timeouts in `TurboClientOptions`

All pipeline features (cookies, caching, retries, redirects) apply automatically. You don't think about them.

## Channel API

For high-throughput scenarios where you're sending many requests and processing responses in a producer/consumer pattern:

```csharp
var client = new TurboHttpClient(options);
var writer = client.Requests;
var reader = client.Responses;

// Producer task: send many requests
_ = Task.Run(async () =>
{
    for (int i = 0; i < 1000; i++)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.example.com/item/{i}");
        await writer.WriteAsync(req);
    }
    writer.TryComplete();
});

// Consumer task: process responses as they arrive
await foreach (var response in reader.ReadAllAsync())
{
    // Process response
    Console.WriteLine(response.StatusCode);
}
```

The channel API provides **backpressure** — if the producer sends too fast, `WriteAsync` will wait until the consumer has processed responses.

## Configuration

All client options are set via `TurboClientOptions`:

```csharp
var options = new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    DefaultRequestVersion = HttpVersion.Version20,  // Force HTTP/2
    DefaultRequestHeaders = new Dictionary<string, string>
    {
        { "User-Agent", "MyApp/1.0" },
        { "Accept", "application/json" }
    }
};

var client = new TurboHttpClient(options);
```

See [Configuration Guide](../guide/configuration) for full options.
