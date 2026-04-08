# Extending the Pipeline

::: warning Prerequisites
This page assumes familiarity with [Akka.Streams](https://getakka.net/articles/streams/introduction.html) graph stages. If you haven't worked with Akka.Streams before, start with their documentation first.
:::

TurboHTTP's request pipeline is built on Akka.Streams. You can insert custom graph stages directly into the pipeline for request signing, telemetry, protocol translation, and other transformations.

## Writing a Custom Stage

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

## Use Cases

Custom stages run inside the existing pipeline — they see every request before it is encoded and every response after it is decoded. This makes them suitable for:

- **Request signing** (HMAC, OAuth signatures)
- **Header injection** (correlation IDs, tenant context)
- **Response transformation** (unwrap envelopes, normalise status codes)
- **Observability** (latency histograms, request logging)

Akka.Streams guarantees backpressure through the entire stage chain — your custom stage will never be pushed faster than it can process.

## When to Use Stages vs Handlers

For most use cases, the [`TurboHandler` middleware API](/architecture/handlers) is simpler and sufficient. Use direct stage insertion only when you need access to the underlying stream topology — for example, when you need to buffer, fan out, or apply rate limiting that a simple request/response transform cannot express.
