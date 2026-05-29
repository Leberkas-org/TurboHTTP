# Scenarios

TurboHTTP combines a full HTTP stack with Akka Streams, giving you streaming, backpressure, and actor integration out of the box. These scenarios show what that looks like in practice — from actor-backed REST resources to real-time event streams.

---

## Standard ASP.NET Core with Actor Backends

TurboHTTP is a transport layer — your application code uses standard ASP.NET Core routing and DI. Combine it with Akka.NET actors for stateful backends by injecting `ActorSystem` or typed actor references into your handlers:

```csharp
app.MapGet("/orders/{id}", async (int id, ActorSystem system) =>
{
    var orderActor = system.ActorSelection($"/user/orders/order-{id}");
    var order = await orderActor.Ask<OrderResponse>(
        new GetOrder(id), TimeSpan.FromSeconds(5));
    return Results.Ok(order);
});

app.MapPost("/orders", async (CreateOrderRequest req, ActorSystem system) =>
{
    var manager = system.ActorSelection("/user/orders");
    var result = await manager.Ask<OrderCreated>(
        new CreateOrder(req.Items), TimeSpan.FromSeconds(5));
    return Results.Created($"/orders/{result.Id}", result);
});
```

::: tip Key Insight
TurboHTTP reuses an existing `ActorSystem` from DI if one is registered (e.g. via Akka.Hosting). Your server connections and your domain actors share the same system — no extra infrastructure.
:::

---

## Real-Time SSE Streaming

Server-Sent Events let you push data to clients over a long-lived HTTP connection. TurboHTTP makes this trivial — return an Akka Streams `Source` wrapped in `AkkaResults.ServerSentEvent`, and the framework handles SSE framing, connection lifecycle, and backpressure for you.

Streaming helpers come from the `Servus.Akka.AspNetCore` package and require an `IMaterializer` instance (typically injected from DI).

```csharp
using Servus.Akka.AspNetCore;

app.MapGet("/events/orders", (HttpContext ctx, IOrderEventSource orderEvents, IMaterializer materializer) =>
{
    var events = orderEvents
        .AsSource()
        .Select(e => new ServerSentEvent(
            Data: e.ToJson(),
            EventType: e.GetType().Name,
            Id: e.OrderId.ToString()));

    return AkkaResults.ServerSentEvent(events, materializer);
});
```

::: tip Key Insight
The `Source` is materialized when the client connects and torn down when they disconnect. Backpressure flows end-to-end: if the client's network is slow, the stream slows down automatically — no manual buffering, no dropped events, no out-of-memory risk from unbounded queues.
:::

---

## Raw Byte Streaming

When you need to stream binary data — file downloads, video, sensor feeds — you want bytes to flow from the source to the network without piling up in memory. `AkkaResults.Stream` takes an Akka Streams `Source` of byte chunks and pipes it directly into the HTTP response body.

```csharp
using Servus.Akka.AspNetCore;

app.MapGet("/files/{fileId}", (HttpContext ctx, IFileStore fileStore, string fileId, IMaterializer materializer) =>
{
    var metadata = fileStore.GetMetadata(fileId);

    var bytes = Akka.Streams.IO.FileIO
        .FromFile(new FileInfo(metadata.Path), chunkSize: 8 * 1024)
        .Select(chunk => (ReadOnlyMemory<byte>)chunk.Memory);

    return AkkaResults.Stream(bytes, materializer, contentType: metadata.ContentType);
});
```

::: tip Key Insight
The `chunkSize` parameter controls how much data is in flight at any moment. Whether the file is 1 KB or 10 GB, memory usage stays constant — Akka Streams pulls the next chunk only when the previous one has been written to the network.
:::

---

## Client-Side Stream Consumption

The `TurboHttpClient` exposes its request/response pipeline as channels. Instead of awaiting one response at a time, you write requests into a `ChannelWriter` and read responses from a `ChannelReader` — turning HTTP into a stream you can process with `await foreach`.

```csharp
var client = factory.CreateClient("api");

var urls = Enumerable.Range(1, 100)
    .Select(i => new HttpRequestMessage(HttpMethod.Get, $"/api/products/{i}"));

foreach (var request in urls)
{
    await client.Requests.WriteAsync(request, ct);
}

client.Requests.Complete();

await foreach (var response in client.Responses.ReadAllAsync(ct))
{
    var product = await response.Content.ReadFromJsonAsync<Product>(ct);
    await ProcessProduct(product);
}
```

::: tip Key Insight
Over HTTP/2, all 100 requests multiplex on a single connection. Responses arrive as their streams complete — not in request order — so fast endpoints don't wait behind slow ones. The channel-based API makes this natural: write requests as fast as you want, consume responses as they show up.
:::

---

## Backpressure-Aware Pipeline

TurboHTTP doesn't just use Akka Streams for internal plumbing — it exposes the full operator toolkit for you to shape, merge, and throttle data before it hits the wire. Every operator in the pipeline participates in backpressure, from the data source all the way to the client's TCP receive window.

```csharp
using Servus.Akka.AspNetCore;

app.MapGet("/metrics/live", (HttpContext ctx, IMetricsSource metrics, IMaterializer materializer) =>
{
    var cpuMetrics = metrics.CpuEvents();
    var memoryMetrics = metrics.MemoryEvents();

    var merged = cpuMetrics
        .Merge(memoryMetrics)
        .Throttle(100, TimeSpan.FromSeconds(1), ThrottleMode.Shaping)
        .Select(m => new ServerSentEvent(
            Data: m.ToJson(),
            EventType: m.Category));

    return AkkaResults.ServerSentEvent(merged, materializer);
});
```

::: tip Key Insight
`Merge`, `Throttle`, `Buffer`, `GroupBy`, `Broadcast` — these are standard Akka Streams operators, not TurboHTTP-specific APIs. Any stream processing graph you can build with Akka Streams plugs directly into an HTTP response. The pipeline handles framing, chunked transfer, and connection lifecycle automatically.
:::
