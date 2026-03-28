---
title: Stage Patterns
description: GraphStage patterns, port naming conventions, and lifecycle management for Akka.Streams
tags: [patterns, akka, stages, design, conventions]
aliases: [StagePatterns, GraphStagePatterns, PortNaming]
---

# TurboHttp Akka.Streams Stage Patterns

## Port Naming Convention

All `GraphStage` inlet/outlet string names follow **PascalCase**: `StageName.Direction` or `StageName.Direction.Role`.

### String Name Patterns

| Shape Type | Inlet Pattern | Outlet Pattern | Example |
|-----------|--------------|----------------|---------|
| **FlowShape** (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| **FanOutShape** (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` / `"Redirect.Out.Redirect"` |
| **FanInShape** (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` / `"Http20Correlation.In.Response"` |
| **Custom Multi-Port** | `StageName.In.Role` | `StageName.Out.Role` | `"Http20Connection.In.Server"` / `"Http20Connection.Out.Stream"` |

### C# Field Name Patterns

| Shape Type | Inlet Fields | Outlet Fields |
|-----------|-------------|--------------|
| **FlowShape** | `_in` | `_out` |
| **FanOutShape** | `_in` | `_outRole` (e.g., `_outFinal`, `_outSignal`) |
| **FanInShape** | `_inRole` (e.g., `_inRequest`) | `_out` |
| **Custom Multi-Port** | `_inRole` | `_outRole` |

### Naming Rules

1. **PascalCase throughout** — matches C# idiom
2. **No protocol prefix** — stage class name already contains it (e.g., `Http11Encoder` not `Http.Http11Encoder`)
3. **Drop `Stage` suffix** — string name uses `Http11Encoder`, not `Http11EncoderStage`
4. **Semantic roles** — `Request`, `Response`, `Final`, `Retry`, `Redirect`, `Signal`, `Miss`, `Hit`, `Server`, `Stream`, `App`
5. **Globally unique** — no two stages share the same port string name

### Examples

**Http11EncoderStage** (FlowShape):
```csharp
private readonly Inlet<HttpRequestMessage> _in = new("Http11Encoder.In");
private readonly Outlet<ByteString> _out = new("Http11Encoder.Out");
```

**RedirectBidiStage** (FanOutShape — redirects are retry-like):
```csharp
private readonly Inlet<(HttpRequestMessage, TransportOptions)> _in = new("Redirect.In");
private readonly Outlet<(HttpRequestMessage, TransportOptions)> _outFinal = new("Redirect.Out.Final");
private readonly Outlet<(HttpRequestMessage, TransportOptions)> _outRedirect = new("Redirect.Out.Redirect");
```

**Http20CorrelationStage** (FanInShape):
```csharp
private readonly Inlet<HttpRequestMessage> _inRequest = new("Http20Correlation.In.Request");
private readonly Inlet<(int StreamId, HttpResponseMessage)> _inResponse = new("Http20Correlation.In.Response");
private readonly Outlet<(HttpRequestMessage, HttpResponseMessage)> _out = new("Http20Correlation.Out");
```

## Common Stage Patterns

### 1. Encoder Stage Pattern (FlowShape)

**Purpose**: Serialize domain objects → bytes

```csharp
public sealed class Http11EncoderStage : GraphStage<FlowShape<HttpRequestMessage, ByteString>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("Http11Encoder.In");
    private readonly Outlet<ByteString> _out = new("Http11Encoder.Out");
    
    public override FlowShape<HttpRequestMessage, ByteString> Shape =>
        new(_in, _out);
    
    protected override GraphStageLogic CreateLogic(Attributes attributes) =>
        new Logic(this, _in, _out);
    
    private sealed class Logic : InHandler, OutHandler
    {
        private readonly Http11Encoder _encoder = new();
        
        public void OnPush()
        {
            var request = Grab(_in);
            var encoded = _encoder.Encode(request);
            Push(_out, ByteString.FromBytes(encoded));
        }
        
        public void OnPull() => Pull(_in);
        
        public void OnUpstreamFinish() => CompleteStage();
        public void OnDownstreamFinish() => FailStage(new OperationCanceledException());
    }
}
```

**Responsibilities**:
- Maintain stateless or minimal state (`_encoder` is OK, but avoid large buffers)
- Use `Grab()` to consume exactly one element
- Use `Push()` to emit one element per Pull
- Handle upstream finish and downstream finish

### 2. Decoder Stage Pattern (FlowShape)

**Purpose**: Parse bytes → domain objects (stateful)

```csharp
public sealed class Http11DecoderStage : GraphStage<FlowShape<ByteString, HttpResponseMessage>>
{
    private readonly Inlet<ByteString> _in = new("Http11Decoder.In");
    private readonly Outlet<HttpResponseMessage> _out = new("Http11Decoder.Out");
    
    public override FlowShape<ByteString, HttpResponseMessage> Shape =>
        new(_in, _out);
    
    protected override GraphStageLogic CreateLogic(Attributes attributes) =>
        new Logic(this, _in, _out);
    
    private sealed class Logic : InHandler, OutHandler
    {
        private readonly Http11CompletionDecoder _decoder = new();
        
        public void OnPush()
        {
            var chunk = Grab(_in);
            if (_decoder.Process(chunk.ToArray()) is {} response)
            {
                Push(_out, response);
            }
            else
            {
                Pull(_in); // Need more data
            }
        }
        
        public void OnPull() => Pull(_in);
        public void OnUpstreamFinish() =>
            _decoder.TryDecodeEof() switch
            {
                { } response => Push(_out, response),
                null => CompleteStage()
            };
    }
}
```

**Responsibilities**:
- Maintain `_remainder` or internal buffer for partial frames
- Call `TryDecode()` when more data arrives
- Pull again if incomplete
- Call `TryDecodeEof()` on upstream finish (connection close)
- Reset state between connections if reusable

### 3. BidiStage Pattern (BidiShape — Request/Response correlation)

**Purpose**: Cross-cutting feature that touches both request and response

Example: **RedirectBidiStage** (actually FanOut for simplicity)

```csharp
public sealed class RedirectBidiStage : GraphStage<FanOutShape<(HttpRequestMessage, TransportOptions), 
                                                               (HttpRequestMessage, TransportOptions), 
                                                               (HttpRequestMessage, TransportOptions)>>
{
    private readonly Inlet<(HttpRequestMessage, TransportOptions)> _in = new("Redirect.In");
    private readonly Outlet<(HttpRequestMessage, TransportOptions)> _outFinal = new("Redirect.Out.Final");
    private readonly Outlet<(HttpRequestMessage, TransportOptions)> _outRetry = new("Redirect.Out.Retry");
    
    public override FanOutShape<...> Shape => new(_in, _outFinal, _outRetry);
    
    protected override GraphStageLogic CreateLogic(Attributes attributes) =>
        new Logic(this, _in, _outFinal, _outRetry);
    
    private sealed class Logic : InHandler, OutHandler
    {
        private Queue<(HttpRequestMessage, TransportOptions)> _redirectQueue = new();
        private bool _downstreamClosed = false;
        
        public void OnPush()
        {
            var (request, opts) = Grab(_in);
            if (_redirectHandler.TryGetRedirect(request) is {} redirectUrl)
            {
                _redirectQueue.Enqueue((newRequest, opts));
                Pull(_in); // Get next request while processing redirect
            }
            else
            {
                // No redirect, emit final response
                if (!_downstreamClosed)
                    Push(_outFinal, (request, opts));
            }
        }
    }
}
```

**Key Pattern**:
- Use `Inlet` + `Outlet` for typed channels
- `Grab()` to consume, `Push()` to emit
- `Pull()` to signal ready for more
- Handle backpressure (when downstream can't accept)

### 4. Connection/Stream Stage Pattern (Multi-Port Custom Shape)

**Purpose**: Manage connection-level or stream-level protocol state

Example: **Http20ConnectionStage** (handles SETTINGS, PING, GOAWAY)

```csharp
public sealed class Http20ConnectionStage : GraphStage<FlowShape<Http2Frame, Http2Frame>>
{
    private readonly Inlet<Http2Frame> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<Http2Frame> _outStream = new("Http20Connection.Out.Stream");
    
    protected override GraphStageLogic CreateLogic(Attributes attributes) =>
        new Logic(this, _inServer, _outStream);
    
    private sealed class Logic : InHandler, OutHandler
    {
        private readonly Http2ConnectionState _connState = new();
        
        public void OnPush()
        {
            var frame = Grab(_inServer);
            
            // Handle connection-level frames
            switch (frame)
            {
                case SettingsFrame sf:
                    _connState.ApplySettings(sf);
                    // Emit SETTINGS ACK implicitly
                    break;
                case GoAwayFrame gf:
                    _connState.MarkGoingAway(gf.LastStreamId);
                    CompleteStage();
                    break;
                default:
                    // Stream-level frames pass through
                    Push(_outStream, frame);
                    break;
            }
        }
        
        public void OnPull() => Pull(_inServer);
    }
}
```

## Stage Lifecycle

1. **OnPush()** — called when upstream has data (after `Pull()`)
2. **OnPull()** — called when downstream is ready (or on first demand)
3. **OnUpstreamFinish()** — called when upstream completes (no more data)
4. **OnDownstreamFinish()** — called when downstream cancels
5. **OnAsyncUpstreamFailure()** — error propagation from upstream

## Anti-Patterns to Avoid

1. ❌ **Don't buffer unbounded** — use `async` or external state if buffer > 10KB
2. ❌ **Don't call `Grab()` twice** — one `Grab()` per `OnPush()`
3. ❌ **Don't `Push()` without `Pull()`** — always pair them
4. ❌ **Don't ignore backpressure** — respect downstream readiness
5. ❌ **Don't mix thread contexts** — Akka stages are single-threaded per actor
6. ❌ **Don't put actor names in port strings** — stage class name is enough
7. ❌ **Don't reuse stage instances** — create new stage for each flow

## Testing Pattern

Use `StreamTestBase` (extends `TestKit`) for stage unit tests:

```csharp
public sealed class Http11EncoderStageTests : StreamTestBase
{
    [Fact]
    public void EncodeSimpleGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var source = Source.Single(request);
        var sink = Sink.Seq<ByteString>();
        
        var result = source
            .Via(new Http11EncoderStage())
            .To(sink)
            .Run(Materializer);
        
        result.Should().HaveCount(1);
        result[0].Should().StartWith("GET / HTTP/1.1");
    }
}
```
