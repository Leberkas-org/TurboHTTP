---
title: "Feature 025: Clean Protocol Core — Single GroupByRequestKey"
description: "Invert the protocol-core topology so GroupByRequestKey is called once at the top level, with HTTP version routing and engine connection flows living inside each substream"
tags: [features, architecture, streams, protocol-core, refactoring]
status: planned
---

# Feature 025: Clean Protocol Core — Single GroupByRequestKey

## Summary

| Field | Value |
|-------|-------|
| **Status** | 🟡 Planned |
| **Category** | Architecture Refactoring |
| **Scope** | 2 files (delete 1, rewrite 1) |

## Problem

`ProtocolCoreGraphBuilder` inverts the natural execution order. The current topology is:

```
Partition (by HTTP version)
    ├─ GroupByRequestKey(256) → ConnectionFlow<Http10Engine> → MergeSubstreams
    ├─ GroupByRequestKey(256) → ConnectionFlow<Http11Engine> → MergeSubstreams
    ├─ GroupByRequestKey(64)  → ConnectionFlow<Http20Engine> → MergeSubstreams
    └─ GroupByRequestKey(64)  → ConnectionFlow<Http30Engine> → MergeSubstreams
Merge
```

`GroupByRequestKey` is instantiated **four times** — once per HTTP version lane. The grouping key (`RequestEndpoint`) already contains the HTTP version, so the Partition and the per-lane GroupBy are doing redundant work at different levels of the graph.

## Target Topology

Invert: group first, then route by version inside each substream.

```
GroupByRequestKey(host:port:scheme:version, maxSubstreams=256)   ← called once
    └─ substream per endpoint (all requests have the same version)
         Partition (by HTTP version)
             ├─ ConnectionFlow<Http10Engine>
             ├─ ConnectionFlow<Http11Engine>
             ├─ ConnectionFlow<Http20Engine>
             └─ ConnectionFlow<Http30Engine>
         Merge
MergeSubstreams
```

Because `Version` is part of the `RequestEndpoint` key, every substream carries requests of exactly one HTTP version. The inner Partition always routes to a single branch — it is explicit rather than clever.

## Design Decisions

### Version stays in RequestEndpoint key

`RequestEndpoint = (host, port, scheme, version)` is unchanged. Removing version from the key would be a semantic change: it would collapse HTTP/1.1 and HTTP/2 connections to the same host into one substream, which introduces mixed-version connection management complexity. The structural refactor is sufficient without changing semantics.

### Single maxSubstreams = 256

Previously each HTTP version had its own GroupByRequestKey with a separate limit:

| Version | Old limit |
|---------|-----------|
| HTTP/1.0 | 256 |
| HTTP/1.1 | 256 |
| HTTP/2   | 64  |
| HTTP/3   | 64  |

With one GroupByRequestKey the limit is shared across all versions. `256` is used as the default — it matches the existing HTTP/1.x ceiling and is a reasonable upper bound for distinct endpoints. Because version is in the key, an HTTP/2 + HTTP/1.1 dual-stack host counts as two substreams, preserving relative separation.

## Files

| Action | File |
|--------|------|
| **Delete** | `src/TurboHTTP/Streams/ProtocolCoreGraphBuilder.cs` |
| **Rewrite** | `src/TurboHTTP/Streams/Engine.cs` |
| Keep | `src/TurboHTTP/Internal/RequestEndpoint.cs` |
| Keep | `src/TurboHTTP/Streams/Stages/Internal/GroupByRequestKeyStage.cs` |
| Keep | `src/TurboHTTP/Streams/Stages/Internal/HostKeyGroupByExtensions.cs` |
| Keep | `src/TurboHTTP.StreamTests/Streams/10_EngineVersionRoutingTests.cs` |

## Implementation Sketch

### `Engine.cs` changes

Replace the `ProtocolCoreGraphBuilder.Build(...)` call in `BuildExtendedPipeline` with a call to a new private `BuildProtocolCore` method:

```csharp
private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed>
    BuildProtocolCore(
        ConnectionPool pool,
        TurboClientOptions clientOptions,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http10Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http11Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http20Factory,
        Func<Flow<IOutputItem, IInputItem, NotUsed>>? http30Factory)
{
    var http10 = BuildConnectionFlow<Http10Engine>(pool, http10Factory, clientOptions);
    var http11 = BuildConnectionFlow<Http11Engine>(pool, http11Factory, clientOptions);
    var http20 = BuildConnectionFlow<Http20Engine>(pool, http20Factory, clientOptions);
    var http30 = BuildConnectionFlow<Http30Engine>(pool, http30Factory, clientOptions);

    var versionRouter = BuildVersionRouter(http10, http11, http20, http30);
    var highThroughputBuffer = Attributes.CreateInputBuffer(16, 64);

    return (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
        Flow.Create<HttpRequestMessage>()
            .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: 256)
            .ViaSubFlow(versionRouter)
            .MergeSubstreams()
            .WithAttributes(highThroughputBuffer);
}

private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed>
    BuildVersionRouter(/* four ConnectionFlow graphs */)
{
    return GraphDsl.Create(b =>
    {
        var partition = b.Add(new Partition<HttpRequestMessage>(4, msg
            => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0,
                _ => throw new ArgumentOutOfRangeException(...)
            }));

        var merge = b.Add(new Merge<HttpResponseMessage>(4));

        b.From(partition.Out(0)).Via(b.Add(http10)).To(merge);
        b.From(partition.Out(1)).Via(b.Add(http11)).To(merge);
        b.From(partition.Out(2)).Via(b.Add(http20)).To(merge);
        b.From(partition.Out(3)).Via(b.Add(http30)).To(merge);

        return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
    });
}
```

`BuildConnectionFlow<TEngine>` moves from `ProtocolCoreGraphBuilder` into `Engine` unchanged.

## Verification

```bash
dotnet build --configuration Release ./src/TurboHTTP.sln

dotnet test ./src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj \
    -- --filter-class "TurboHTTP.StreamTests.Streams.EngineVersionRoutingTests"

dotnet test ./src/TurboHTTP.sln
```

## See Also

- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — pipeline layer overview
- [[Architecture/Design/02-STAGE_PATTERNS|Stage Patterns]] — GraphStage conventions
