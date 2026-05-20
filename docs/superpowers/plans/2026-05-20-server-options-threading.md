# Server Options Threading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thread `TurboServerOptions` through all server Engines → Stages → StateMachines so that every declared public option property is wired to the protocol layer, matching the client-side pattern.

**Architecture:** Each server StateMachine receives `TurboServerOptions` and builds `SharedHttpOptions` + protocol-specific encoder/decoder options internally. Engines and Stages become thin pass-throughs for the options object. Defaults are corrected to RFC/Kestrel alignment.

**Tech Stack:** C# 12, Akka.Streams, xUnit v3

**Spec:** `docs/superpowers/specs/2026-05-20-server-options-threading-design.md`

---

## File Map

| Layer | File | Action |
|---|---|---|
| Public Options | `src/TurboHTTP/Server/TurboServerOptions.cs` | Fix `KeepAliveTimeout` default |
| | `src/TurboHTTP/Server/Http1ServerOptions.cs` | Add 4 properties |
| | `src/TurboHTTP/Server/Http2ServerOptions.cs` | Add 1 property, fix 5 defaults |
| | `src/TurboHTTP/Server/Http3ServerOptions.cs` | Add 1 property, fix 2 defaults |
| Engines | `src/TurboHTTP/Streams/Http10ServerEngine.cs` | Constructor → `TurboServerOptions` |
| | `src/TurboHTTP/Streams/Http11ServerEngine.cs` | Constructor → `TurboServerOptions` |
| | `src/TurboHTTP/Streams/Http20ServerEngine.cs` | 8 fields → 1 field |
| | `src/TurboHTTP/Streams/Http30ServerEngine.cs` | 5 fields → 1 field |
| Stages | `src/TurboHTTP/Streams/Stages/Server/Http10ServerConnectionStage.cs` | Add `TurboServerOptions` field |
| | `src/TurboHTTP/Streams/Stages/Server/Http11ServerConnectionStage.cs` | Replace encoder/decoder opts with `TurboServerOptions` |
| | `src/TurboHTTP/Streams/Stages/Server/Http20ServerConnectionStage.cs` | 8 fields → 1 field |
| | `src/TurboHTTP/Streams/Stages/Server/Http30ServerConnectionStage.cs` | 5 fields → 1 field |
| StateMachines | `src/TurboHTTP/Protocol/Syntax/Http10/Server/Http10ServerStateMachine.cs` | New constructor pattern |
| | `src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs` | New constructor pattern |
| | `src/TurboHTTP/Protocol/Syntax/Http2/Server/Http2ServerStateMachine.cs` | New constructor pattern |
| | `src/TurboHTTP/Protocol/Syntax/Http3/Server/Http3ServerStateMachine.cs` | New constructor pattern |
| Router | `src/TurboHTTP/Streams/ProtocolRouter.cs` | Simplify engine calls |
| Tests | 14 test files across Http10/Http11/Http2/Http3 Server | Update SM constructors |

---

### Task 1: Fix public options defaults and add new properties

**Files:**
- Modify: `src/TurboHTTP/Server/TurboServerOptions.cs:14`
- Modify: `src/TurboHTTP/Server/Http1ServerOptions.cs`
- Modify: `src/TurboHTTP/Server/Http2ServerOptions.cs`
- Modify: `src/TurboHTTP/Server/Http3ServerOptions.cs`

- [ ] **Step 1: Fix TurboServerOptions.KeepAliveTimeout default from 120s to 130s**

In `src/TurboHTTP/Server/TurboServerOptions.cs`, change line 14:

```csharp
// Before:
public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);

// After:
public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
```

- [ ] **Step 2: Add 4 new properties to Http1ServerOptions**

In `src/TurboHTTP/Server/Http1ServerOptions.cs`, add after line 9 (after `BodyReadTimeout`):

```csharp
public long MaxRequestBodySize { get; set; } = 30_000_000;
public int MaxHeaderListSize { get; set; } = 32 * 1024;
public TimeSpan? KeepAliveTimeout { get; set; }
public TimeSpan? RequestHeadersTimeout { get; set; }
```

- [ ] **Step 3: Add HeaderTableSize and fix defaults in Http2ServerOptions**

Replace the full body of `src/TurboHTTP/Server/Http2ServerOptions.cs`:

```csharp
namespace TurboHTTP.Server;

public sealed class Http2ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialConnectionWindowSize { get; set; } = 1 * 1024 * 1024;
    public int InitialStreamWindowSize { get; set; } = 768 * 1024;
    public int MaxFrameSize { get; set; } = 16 * 1024;
    public int MaxHeaderListSize { get; set; } = 32 * 1024;
    public int HeaderTableSize { get; set; } = 4 * 1024;
    public long MaxRequestBodySize { get; set; } = 30_000_000;
    public long MaxResponseBufferSize { get; set; } = 64 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
```

- [ ] **Step 4: Add QpackMaxTableCapacity and fix defaults in Http3ServerOptions**

Replace the full body of `src/TurboHTTP/Server/Http3ServerOptions.cs`:

```csharp
namespace TurboHTTP.Server;

public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int MaxHeaderListSize { get; set; } = 32 * 1024;
    public int QpackMaxTableCapacity { get; set; }
    public bool EnableWebTransport { get; set; }
    public long MaxRequestBodySize { get; set; } = 30_000_000;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
```

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds (public API additions are backwards-compatible, default changes won't break callers)

- [ ] **Step 6: Commit**

```
git add src/TurboHTTP/Server/TurboServerOptions.cs src/TurboHTTP/Server/Http1ServerOptions.cs src/TurboHTTP/Server/Http2ServerOptions.cs src/TurboHTTP/Server/Http3ServerOptions.cs
git commit -m "refactor(server): add missing options properties and fix defaults to RFC/Kestrel alignment"
```

---

### Task 2: Refactor Http10ServerStateMachine to accept TurboServerOptions

**Files:**
- Modify: `src/TurboHTTP/Protocol/Syntax/Http10/Server/Http10ServerStateMachine.cs`

- [ ] **Step 1: Replace the constructor**

In `Http10ServerStateMachine.cs`, replace lines 1–34 (the entire top section through the constructor):

```csharp
using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http10ServerDecoder _decoder;
    private readonly Http10ServerEncoder _encoder;
    private readonly long _maxRequestBodySize;

    private HttpResponseMessage? _deferredResponse;
    private IMemoryOwner<byte>? _deferredBodyOwner;
    private int _deferredBodyLength;

    public bool CanAcceptResponse => true;
    public bool ShouldComplete { get; private set; }

    public Http10ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        _maxRequestBodySize = options.Http1.MaxRequestBodySize;

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http1.MaxRequestBodySize,
            MaxHeaderBytes = options.Http1.MaxHeaderListSize,
            HeaderLineMaxLength = options.Http1.MaxRequestLineLength,
            RequestLineMaxLength = options.Http1.MaxRequestLineLength,
        };

        var decoderOpts = new Http10ServerDecoderOptions { Shared = shared };
        var encoderOpts = new Http10ServerEncoderOptions { Shared = shared };

        _decoder = new Http10ServerDecoder(decoderOpts);
        _encoder = new Http10ServerEncoder(encoderOpts);
    }
```

The rest of the file (from `PreStart()` onward) stays identical.

- [ ] **Step 2: Build to check for compile errors**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Compile errors in `Http10ServerConnectionStage.cs` (still uses old constructor) and test files — this is expected, we'll fix them in Tasks 6 and 8.

- [ ] **Step 3: Commit**

```
git add src/TurboHTTP/Protocol/Syntax/Http10/Server/Http10ServerStateMachine.cs
git commit -m "refactor(http10): accept TurboServerOptions in Http10ServerStateMachine"
```

---

### Task 3: Refactor Http11ServerStateMachine to accept TurboServerOptions

**Files:**
- Modify: `src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs`

- [ ] **Step 1: Replace the usings and constructor (lines 1–45)**

```csharp
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using HttpVersion = System.Net.HttpVersion;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly int _maxPipelineDepth;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;

    private int _requestsPipelined;
    private int _pendingResponseCount;
    private bool _outboundBodyPending;
    private bool _requestHeadersTimerActive;

    public bool CanAcceptResponse => !_outboundBodyPending && _pendingResponseCount > 0;
    public bool ShouldComplete { get; private set; }

    public Http11ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http1.MaxRequestBodySize,
            MaxHeaderBytes = options.Http1.MaxHeaderListSize,
            HeaderLineMaxLength = options.Http1.MaxRequestLineLength,
            RequestLineMaxLength = options.Http1.MaxRequestLineLength,
        };

        var encOpts = new Http11ServerEncoderOptions
        {
            Shared = shared,
            KeepAliveTimeout = options.Http1.KeepAliveTimeout ?? options.KeepAliveTimeout,
            RequestHeadersTimeout = options.Http1.RequestHeadersTimeout ?? options.RequestHeadersTimeout,
        };

        var decOpts = new Http11ServerDecoderOptions
        {
            Shared = shared,
            MaxPipelinedRequests = options.Http1.MaxPipelinedRequests,
        };

        encOpts.Validate();
        decOpts.Validate();

        _decoder = new Http11ServerDecoder(decOpts);
        _encoder = new Http11ServerEncoder(encOpts);
        _keepAliveTimeout = encOpts.KeepAliveTimeout;
        _requestHeadersTimeout = encOpts.RequestHeadersTimeout;
        _maxPipelineDepth = decOpts.MaxPipelinedRequests;
    }
```

The rest of the file (from `PreStart()` onward) stays identical.

- [ ] **Step 2: Commit**

```
git add src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs
git commit -m "refactor(http11): accept TurboServerOptions in Http11ServerStateMachine"
```

---

### Task 4: Refactor Http2ServerStateMachine to accept TurboServerOptions

**Files:**
- Modify: `src/TurboHTTP/Protocol/Syntax/Http2/Server/Http2ServerStateMachine.cs`

- [ ] **Step 1: Replace the usings and constructor (lines 1–61)**

```csharp
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string BodyRateCheck = "body-rate-check:";

    private readonly IServerStageOperations _ops;
    private readonly Http2ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => false;

    public Http2ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http2.MaxRequestBodySize,
            MaxHeaderBytes = options.Http2.MaxHeaderListSize,
        };

        var encoderOpts = new Http2ServerEncoderOptions
        {
            Shared = shared,
            HeaderTableSize = options.Http2.HeaderTableSize,
            MaxFrameSize = options.Http2.MaxFrameSize,
        };

        var decoderOpts = new Http2ServerDecoderOptions
        {
            Shared = shared,
            MaxConcurrentStreams = options.Http2.MaxConcurrentStreams,
            MaxFieldSectionSize = options.Http2.MaxHeaderListSize,
        };

        _sessionManager = new Http2ServerSessionManager(
            encoderOpts,
            decoderOpts,
            ops,
            options.Http2.InitialConnectionWindowSize,
            options.Http2.InitialStreamWindowSize,
            options.Http2.MaxRequestBodySize);

        _keepAliveTimeout = options.Http2.KeepAliveTimeout;
        _requestHeadersTimeout = options.Http2.RequestHeadersTimeout;
        _minBodyDataRate = options.Http2.MinRequestBodyDataRate;
        _bodyRateGracePeriod = options.Http2.MinRequestBodyDataRateGracePeriod;
    }
```

The rest of the file (from `PreStart()` onward) stays identical.

- [ ] **Step 2: Commit**

```
git add src/TurboHTTP/Protocol/Syntax/Http2/Server/Http2ServerStateMachine.cs
git commit -m "refactor(http2): accept TurboServerOptions in Http2ServerStateMachine"
```

---

### Task 5: Refactor Http3ServerStateMachine to accept TurboServerOptions

**Files:**
- Modify: `src/TurboHTTP/Protocol/Syntax/Http3/Server/Http3ServerStateMachine.cs`

- [ ] **Step 1: Replace the usings and constructor (lines 1–54)**

```csharp
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string BodyRateCheck = "body-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly Http3ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => false;

    public Http3ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http3.MaxRequestBodySize,
            MaxHeaderBytes = options.Http3.MaxHeaderListSize,
        };

        var encoderOpts = new Http3ServerEncoderOptions
        {
            Shared = shared,
            QpackMaxTableCapacity = options.Http3.QpackMaxTableCapacity,
        };

        var decoderOpts = new Http3ServerDecoderOptions
        {
            Shared = shared,
            MaxConcurrentStreams = options.Http3.MaxConcurrentStreams,
            MaxFieldSectionSize = options.Http3.MaxHeaderListSize,
        };

        _sessionManager = new Http3ServerSessionManager(encoderOpts, decoderOpts, ops, options.Http3.MaxRequestBodySize);

        _keepAliveTimeout = options.Http3.KeepAliveTimeout;
        _requestHeadersTimeout = options.Http3.RequestHeadersTimeout;
        _minBodyDataRate = options.Http3.MinRequestBodyDataRate;
        _bodyRateGracePeriod = options.Http3.MinRequestBodyDataRateGracePeriod;
    }
```

The rest of the file (from `PreStart()` onward) stays identical.

- [ ] **Step 2: Commit**

```
git add src/TurboHTTP/Protocol/Syntax/Http3/Server/Http3ServerStateMachine.cs
git commit -m "refactor(http3): accept TurboServerOptions in Http3ServerStateMachine"
```

---

### Task 6: Refactor all ConnectionStages to accept TurboServerOptions

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Server/Http10ServerConnectionStage.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Server/Http11ServerConnectionStage.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Server/Http20ServerConnectionStage.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Server/Http30ServerConnectionStage.cs`

- [ ] **Step 1: Replace Http10ServerConnectionStage**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http10ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http10Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http10Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http10Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http10ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http10ServerStateMachine>(this,
            ops => new Http10ServerStateMachine(_options, ops));
}
```

- [ ] **Step 2: Replace Http11ServerConnectionStage**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http11ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http11Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http11Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http11Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http11ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http11ServerStateMachine>(this,
            ops => new Http11ServerStateMachine(_options, ops));
}
```

- [ ] **Step 3: Replace Http20ServerConnectionStage**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http20ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http20Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http20Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http20ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http2ServerStateMachine>(this,
            ops => new Http2ServerStateMachine(_options, ops));
}
```

- [ ] **Step 4: Replace Http30ServerConnectionStage**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http30ServerConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http30Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http30Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http30Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");
    private readonly TurboServerOptions _options;

    public Http30ServerConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http3ServerStateMachine>(this,
            ops => new Http3ServerStateMachine(_options, ops));
}
```

- [ ] **Step 5: Commit**

```
git add src/TurboHTTP/Streams/Stages/Server/Http10ServerConnectionStage.cs src/TurboHTTP/Streams/Stages/Server/Http11ServerConnectionStage.cs src/TurboHTTP/Streams/Stages/Server/Http20ServerConnectionStage.cs src/TurboHTTP/Streams/Stages/Server/Http30ServerConnectionStage.cs
git commit -m "refactor(stages): accept TurboServerOptions in all server ConnectionStages"
```

---

### Task 7: Simplify Engines and ProtocolRouter

**Files:**
- Modify: `src/TurboHTTP/Streams/Http10ServerEngine.cs`
- Modify: `src/TurboHTTP/Streams/Http11ServerEngine.cs`
- Modify: `src/TurboHTTP/Streams/Http20ServerEngine.cs`
- Modify: `src/TurboHTTP/Streams/Http30ServerEngine.cs`
- Modify: `src/TurboHTTP/Streams/ProtocolRouter.cs`

- [ ] **Step 1: Replace Http10ServerEngine**

```csharp
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http10ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http10ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http10ServerConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
```

- [ ] **Step 2: Replace Http11ServerEngine**

```csharp
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http11ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http11ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http11ServerConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
```

- [ ] **Step 3: Replace Http20ServerEngine**

```csharp
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http20ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http20ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ServerConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
```

- [ ] **Step 4: Replace Http30ServerEngine**

```csharp
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http30ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http30ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ServerConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
```

- [ ] **Step 5: Simplify ProtocolRouter**

Replace `src/TurboHTTP/Streams/ProtocolRouter.cs`:

```csharp
using System.Net.Security;
using TurboHTTP.Server;

namespace TurboHTTP.Streams;

internal static class ProtocolRouter
{
    internal static IServerProtocolEngine ResolveEngine(SslApplicationProtocol protocol, TurboServerOptions options)
    {
        return protocol == SslApplicationProtocol.Http2
            ? new Http20ServerEngine(options)
            : new Http11ServerEngine(options);
    }

    internal static IServerProtocolEngine ResolveEngine(Version version, TurboServerOptions options)
    {
        return version switch
        {
            { Major: 1, Minor: 0 } => new Http10ServerEngine(options),
            { Major: 1, Minor: 1 } => new Http11ServerEngine(options),
            { Major: 2, Minor: 0 } => new Http20ServerEngine(options),
            { Major: 3, Minor: 0 } => new Http30ServerEngine(options),
            _ => new Http11ServerEngine(options)
        };
    }
}
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build --configuration Release src/TurboHTTP/TurboHTTP.csproj`
Expected: Build succeeds (all production code compiles). Test project may still fail — we fix that next.

- [ ] **Step 7: Commit**

```
git add src/TurboHTTP/Streams/Http10ServerEngine.cs src/TurboHTTP/Streams/Http11ServerEngine.cs src/TurboHTTP/Streams/Http20ServerEngine.cs src/TurboHTTP/Streams/Http30ServerEngine.cs src/TurboHTTP/Streams/ProtocolRouter.cs
git commit -m "refactor(engines): simplify all server engines and ProtocolRouter to pass TurboServerOptions"
```

---

### Task 8: Update all test files to use TurboServerOptions

All server SM test files need their `new Http*ServerStateMachine(ops, ...)` calls updated to `new Http*ServerStateMachine(options, ops)`.

**Files to modify (14 test files):**
- `src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerStateMachineSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerStateMachineErrorSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/ServerStateMachineSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerStateMachineTimerSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerStateMachineConnectionSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerPipeliningSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerPipeliningLimitSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerConnectionPersistenceSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/StateMachine/Http2ServerStateMachineSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/StateMachine/Http2ServerStreamCorrelationSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/StateMachine/Http2ServerTimerErrorSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/Streaming/Http2ServerTimeoutSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/Streaming/Http2ServerFlowControlSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/Streaming/Http2ServerBodyStreamingSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/Encoder/Http2ServerResponseBufferSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http3/Server/Http3ServerStateMachineSpec.cs`
- `src/TurboHTTP.Tests/Protocol/Syntax/Http3/Server/Http3ServerStateMachineTimerSpec.cs`

**The pattern is mechanical.** In every test file:

- [ ] **Step 1: Add the `using TurboHTTP.Server;` import to every file that doesn't have it**

- [ ] **Step 2: For tests that use only defaults, replace:**

```csharp
// Before:
var sm = new Http10ServerStateMachine(ops);
// After:
var sm = new Http10ServerStateMachine(new TurboServerOptions(), ops);

// Before:
var sm = new Http11ServerStateMachine(ops);
// After:
var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

// Before:
var sm = new Http2ServerStateMachine(ops);
// After:
var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

// Before:
var sm = new Http3ServerStateMachine(ops);
// After:
var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);
```

- [ ] **Step 3: For tests with custom H2 params, use TurboServerOptions property setters:**

```csharp
// Before:
var sm = new Http2ServerStateMachine(ops, keepAliveTimeout: TimeSpan.FromSeconds(130));
// After:
var options = new TurboServerOptions();
options.Http2.KeepAliveTimeout = TimeSpan.FromSeconds(130);
var sm = new Http2ServerStateMachine(options, ops);

// Before:
var sm = new Http2ServerStateMachine(ops, requestHeadersTimeout: TimeSpan.FromSeconds(30));
// After:
var options = new TurboServerOptions();
options.Http2.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
var sm = new Http2ServerStateMachine(options, ops);

// Before:
var sm = new Http2ServerStateMachine(
//     ops,
//     maxConcurrentStreams: 100,
//     initialConnectionWindowSize: 65535,
//     initialStreamWindowSize: initialWindowSize);
// After:
var options = new TurboServerOptions();
options.Http2.MaxConcurrentStreams = 100;
options.Http2.InitialConnectionWindowSize = 65535;
options.Http2.InitialStreamWindowSize = initialWindowSize;
var sm = new Http2ServerStateMachine(options, ops);

// Before:
var sm = new Http2ServerStateMachine(ops, maxRequestBodySize: maxBodySize);
// After:
var options = new TurboServerOptions();
options.Http2.MaxRequestBodySize = maxBodySize;
var sm = new Http2ServerStateMachine(options, ops);
```

- [ ] **Step 4: For H3 tests with custom params:**

```csharp
// Before:
var sm = new Http3ServerStateMachine(ops, keepAliveTimeout: TimeSpan.FromSeconds(130));
// After:
var options = new TurboServerOptions();
options.Http3.KeepAliveTimeout = TimeSpan.FromSeconds(130);
var sm = new Http3ServerStateMachine(options, ops);
```

- [ ] **Step 5: For H11 pipelining limit tests that pass decoder options:**

```csharp
// Before:
var decoderOpts = new Http11ServerDecoderOptions { MaxPipelinedRequests = 3 };
var sm = new Http11ServerStateMachine(ops, decoderOptions: decoderOpts);
// After:
var options = new TurboServerOptions();
options.Http1.MaxPipelinedRequests = 3;
var sm = new Http11ServerStateMachine(options, ops);

// Before (validation tests):
Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(ops, decoderOptions: new Http11ServerDecoderOptions { MaxPipelinedRequests = 0 }));
// After:
var invalidOpts = new TurboServerOptions();
invalidOpts.Http1.MaxPipelinedRequests = 0;
Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(invalidOpts, ops));
```

- [ ] **Step 6: Build the full solution**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds with zero errors.

- [ ] **Step 7: Commit**

```
git add src/TurboHTTP.Tests/
git commit -m "test: update all server SM tests to use TurboServerOptions"
```

---

### Task 9: Run tests and verify

- [ ] **Step 1: Run all unit tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: All tests pass. No regressions.

- [ ] **Step 2: If any tests fail, diagnose and fix**

The most likely failure mode is default value mismatches — a test may assert a specific timeout or window size that has changed due to the new defaults. For example, if a test asserts `InitialConnectionWindowSize == 65535` but we changed the default to `1 * 1024 * 1024`, the test expectations need updating to match the new defaults.

- [ ] **Step 3: Run Roslyn diagnostics check**

Verify zero compile-time diagnostics across the solution.

- [ ] **Step 4: Commit any fixes**

```
git add -u
git commit -m "fix: align test expectations with corrected server option defaults"
```

---

### Task 10: Run integration tests

- [ ] **Step 1: Run integration tests per-namespace (not full suite)**

Run each namespace separately per project conventions:

```powershell
$env:TURBOHTTP_TEST_BACKEND = "kestrel"
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H1.*"
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H2.*"
```

Expected: All integration tests pass. The server options changes should be transparent since `ProtocolRouter` still receives `TurboServerOptions` and forwards it.

- [ ] **Step 2: Commit any integration test fixes if needed**
