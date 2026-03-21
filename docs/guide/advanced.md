# Advanced Usage

This page covers the channel-based streaming API, extension points for custom policies, and patterns for high-throughput workloads.

## Channel-Based API

In addition to `SendAsync`, TurboHttp exposes a lower-level channel API for scenarios where you want to stream requests and responses without `await`-ing each one individually.

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
}, actorSystem);

// Write requests to the input channel
ChannelWriter<HttpRequestMessage> requestWriter = client.Requests;

// Read responses from the output channel
ChannelReader<HttpResponseMessage> responseReader = client.Responses;
```

This API is useful when:
- You have a producer loop generating requests faster than you can await responses
- You want to decouple request creation from response processing
- You are integrating TurboHttp into a pipeline that already uses `System.Threading.Channels`

### High-Throughput Batch Pattern

Write requests from one task and read responses from another, running concurrently:

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    DefaultRequestVersion = HttpVersion.Version20,
}, actorSystem);

var ids = Enumerable.Range(1, 1000).ToList();

// Producer: write all requests without waiting for responses
var producer = Task.Run(async () =>
{
    foreach (var id in ids)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/items/{id}");
        await client.Requests.WriteAsync(request);
    }
    client.Requests.Complete();
});

// Consumer: process responses as they arrive
var consumer = Task.Run(async () =>
{
    await foreach (var response in client.Responses.ReadAllAsync())
    {
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{(int)response.StatusCode}: {body.Length} bytes");
    }
});

await Task.WhenAll(producer, consumer);
```

With HTTP/2, all 1000 requests flow over a single TCP connection as concurrent streams. With HTTP/1.1, they are serialised per connection but the producer/consumer split still keeps throughput high.

### Backpressure

The channel has a bounded capacity. If the connection cannot keep up with your producer, `WriteAsync` will pause automatically until there is room. You never drop requests — the channel applies backpressure instead.

## Extension Points

TurboHttp's built-in policies — retry, redirect, cookie, cache — are all replaceable. Pass a custom implementation to `TurboClientOptions`.

### Custom Retry Evaluator

The retry evaluator decides whether a failed request should be retried. Implement `IRetryEvaluator`:

```csharp
public sealed class AggressiveRetryEvaluator : IRetryEvaluator
{
    public bool ShouldRetry(HttpRequestMessage request, HttpResponseMessage? response, Exception? exception, int attempt)
    {
        if (attempt >= 5)
        {
            return false;
        }

        // Retry any server error, not just 503
        if (response is not null)
        {
            return (int)response.StatusCode >= 500;
        }

        // Retry network-level failures
        return exception is HttpRequestException or SocketException;
    }

    public TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        // Exponential backoff: 100 ms, 200 ms, 400 ms, …
        return TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
    }
}

var options = new TurboClientOptions
{
    RetryEvaluator = new AggressiveRetryEvaluator(),
};
```

### Custom Redirect Handler

Implement `IRedirectHandler` to take full control of redirect decisions:

```csharp
public sealed class NoRedirectHandler : IRedirectHandler
{
    // Never follow redirects — caller handles them
    public RedirectDecision Evaluate(HttpRequestMessage original, HttpResponseMessage response)
        => RedirectDecision.Stop;
}

var options = new TurboClientOptions
{
    RedirectHandler = new NoRedirectHandler(),
};
```

A redirect handler receives the original request and the redirect response and returns one of:
- `RedirectDecision.Follow(newRequest)` — follow the redirect with `newRequest`
- `RedirectDecision.Stop` — return the redirect response to the caller as-is

### Custom Cookie Jar

Implement `ICookieJar` to store cookies in a custom backend (database, distributed cache, etc.):

```csharp
public sealed class RedisCookieJar : ICookieJar
{
    private readonly IDatabase _db;

    public RedisCookieJar(IDatabase db) => _db = db;

    public void Store(Uri uri, IEnumerable<string> setCookieHeaders)
    {
        foreach (var header in setCookieHeaders)
        {
            // Persist to Redis using URI as namespace
            _db.StringSet($"cookies:{uri.Host}:{Guid.NewGuid()}", header);
        }
    }

    public IEnumerable<string> Get(Uri uri)
    {
        // Retrieve matching cookies for this URI
        return _db.StringGet($"cookies:{uri.Host}:*")
                  .Where(v => v.HasValue)
                  .Select(v => (string)v!);
    }
}

var options = new TurboClientOptions
{
    CookieJar = new RedisCookieJar(redisDatabase),
};
```

Pass `null` to disable cookie management entirely:

```csharp
var options = new TurboClientOptions
{
    CookieJar = null,   // no cookies stored or sent
};
```

### Custom Cache Store

Implement `IHttpCacheStore` to replace the built-in in-memory LRU cache:

```csharp
public sealed class NullCacheStore : IHttpCacheStore
{
    // Disable caching entirely
    public bool TryGet(HttpRequestMessage request, out CacheEntry? entry)
    {
        entry = null;
        return false;
    }

    public void Store(HttpRequestMessage request, CacheEntry entry) { }
    public void Invalidate(HttpRequestMessage request) { }
}

var options = new TurboClientOptions
{
    CacheStore = new NullCacheStore(),
};
```

A distributed cache store (Redis, SQL) follows the same pattern — `TryGet` queries the store and `Store` writes an entry.

## Extending the Pipeline with Akka.Streams

TurboHttp's request pipeline is built on [Akka.Streams](https://getakka.net/articles/streams/introduction.html). If you need to add custom stages — request signing, telemetry, protocol translation — you can insert Akka graph stages directly into the pipeline.

A graph stage is a small, composable unit that transforms the stream:

```csharp
public sealed class RequestSigningStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly string _apiKey;

    public RequestSigningStage(string apiKey) => _apiKey = apiKey;

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }
        = new FlowShape<HttpRequestMessage, HttpRequestMessage>(
            new Inlet<HttpRequestMessage>("RequestSigning.In"),
            new Outlet<HttpRequestMessage>("RequestSigning.Out"));

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(Shape, _apiKey);

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly string _apiKey;

        public Logic(FlowShape<HttpRequestMessage, HttpRequestMessage> shape, string apiKey)
            : base(shape)
        {
            _apiKey = apiKey;

            SetHandler(shape.Inlet, onPush: () =>
            {
                var request = Grab(shape.Inlet);
                request.Headers.Add("X-Api-Key", _apiKey);
                Push(shape.Outlet, request);
            });

            SetHandler(shape.Outlet, onPull: () => Pull(shape.Inlet));
        }
    }
}
```

Register the stage when building the client:

```csharp
var options = new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    PipelineStages = builder => builder.AddStage(new RequestSigningStage("my-secret-key")),
};
```

Custom stages run inside the existing pipeline — they see every request before it is encoded and every response after it is decoded. This makes them suitable for:

- **Request signing** (HMAC, OAuth signatures)
- **Header injection** (correlation IDs, tenant context)
- **Response transformation** (unwrap envelopes, normalise status codes)
- **Observability** (latency histograms, request logging)

Akka.Streams guarantees backpressure through the entire stage chain — your custom stage will never be pushed faster than it can process.
