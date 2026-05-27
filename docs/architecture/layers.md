# Client API

TurboHTTP provides a simple, familiar interface for making HTTP requests.

## SendAsync

The standard way to send a request:

```csharp
var client = factory.CreateClient("my-api");
var request = new HttpRequestMessage(HttpMethod.Get, "/users/123");
var response = await client.SendAsync(request, ct);

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
var client = factory.CreateClient("my-api");
var writer = client.Requests;
var reader = client.Responses;

// Producer task: send many requests
_ = Task.Run(async () =>
{
    for (int i = 0; i < 1000; i++)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/item/{i}");
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

Transport options are set via `TurboClientOptions` at registration time. Request defaults like HTTP version and headers are set on the client instance:

```csharp
// Register via DI
services.AddTurboHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// Resolve and configure the client instance
var client = factory.CreateClient("my-api");
client.DefaultRequestVersion = HttpVersion.Version20;  // Force HTTP/2
client.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
```

See [Configuration Guide](../client/configuration) for full options.

## Server API

TurboHTTP Server provides an ASP.NET Core-style programming model for handling incoming HTTP requests through Akka actors.

### Registration

Register the server via `UseTurboHttp` on `builder.Host` and configure endpoints:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5100);
    options.ListenLocalhost(5101, listen => listen.UseHttps());
});

var app = builder.Build();
```

### Middleware

Register middleware and routes on the `WebApplication` instance using standard ASP.NET Core methods:

```csharp
app.Use(async (context, next) =>
{
    // process request
    await next(context);
    // process response
});
```

### Routes

Define routes with standard ASP.NET Core Minimal API methods:

```csharp
app.MapGet("/users/{id:int}", (int id) => GetUser(id));
app.MapPost("/users", (CreateUserRequest req) => Results.Created($"/users/{req.Id}", req));
```
