# Eager Pipeline Materialization Design

**Date:** 2026-05-08  
**Status:** Approved  
**Scope:** Move pipeline materialization from message-triggered to constructor/PreStart in ClientStreamOwner.

## Problem

`ClientStreamOwner` receives its configuration via a `CreateStreamInstance` message after construction. This creates a window where `RegisterConsumer` messages could arrive before the pipeline is materialized, requiring a `_streamRunning` guard. The message-based initialization also adds unnecessary indirection — `CreateStreamInstance`, `StreamInstanceCreated`, `StreamInstanceFailed`, and `_createRequest` / `_createRequester` fields exist solely to bridge the gap between actor creation and pipeline startup.

## Goals

1. Pass pipeline configuration to `ClientStreamOwner` via constructor arguments.
2. Materialize the pipeline eagerly in `PreStart` — before any message is processed.
3. Remove the `CreateStreamInstance` message and related reply messages.
4. Simplify `HandleRegisterConsumer` — no `_streamRunning` guard needed.

## Non-Goals

1. Changing ConsumerActor, ClientStreamManager message protocol, or TurboHttpClient.
2. Changing retry/backoff behavior (stays the same, just uses constructor config).

## Design

### ClientStreamOwner Constructor

Current: parameterless constructor, receives config via `CreateStreamInstance` message.

Proposed: constructor takes `TurboClientOptions` and `PipelineDescriptor`. Stores them as readonly fields.

```
ClientStreamOwner(TurboClientOptions clientOptions, PipelineDescriptor pipeline)
```

The legacy request/response channels (used by the manager for the MergeHub legacy feed) are also passed to the constructor. The owner creates its own channels internally — no external channel ownership needed.

### PreStart Materialization

`PreStart` calls `MaterializeStream()` using the constructor-provided config. If materialization fails, the retry/backoff logic kicks in exactly as today — the only difference is the config comes from fields instead of `_createRequest`.

### What Gets Removed

| Component | Status |
|---|---|
| `CreateStreamInstance` message record | Removed |
| `StreamInstanceCreated` message record | Removed |
| `StreamInstanceFailed` message record | Removed |
| `_createRequest` field | Removed — config is in constructor fields |
| `_createRequester` field | Removed — no one to notify |
| `HandleCreateStreamInstance` method | Removed — PreStart handles it |
| `HandleStreamInstanceFailed` method | Simplified — no requester to notify |
| `_streamRunning` guard in `HandleRegisterConsumer` | Removed — pipeline is always started |
| `Receive<StreamInstanceCreated>` in manager | Removed |

### What Changes in ClientStreamManager

The manager currently does:
```csharp
var owner = Context.ActorOf(Props.Create(() => new ClientStreamOwner()), name);
owner.Tell(new ClientStreamOwner.CreateStreamInstance(options, pipeline, channel.Reader, channel.Writer));
```

Changes to:
```csharp
var owner = Context.ActorOf(Props.Create(() => new ClientStreamOwner(options, pipeline)), name);
```

The manager no longer creates or owns the legacy request/response channels — the owner creates its own internally. The manager's `OwnerState` record simplifies to just the `IActorRef`.

### Retry Logic

Stays the same. `ExecuteRetryCreate` calls `MaterializeStream()` using the constructor-stored fields. `CalculateBackoff`, `MaxRetryAttempts`, and timer-based retry scheduling are unchanged.

### HandleRegisterConsumer Simplification

Current:
```csharp
if (!_streamRunning || _requestIngress is null || _responseFanoutSource is null || _materializer is null)
{
    _log.Warning("Cannot register consumer — stream not running");
    return;
}
```

Proposed: Remove the guard. If the pipeline failed and is retrying, the consumer child will be created but its materialization will fail (the shared references are null). The consumer actor's supervision (Stop) handles this — the consumer fails, the client observes the channel closing. When the pipeline eventually succeeds on retry, subsequent `RegisterConsumer` messages will work.

Actually, for safety: keep a lightweight guard that queues registration messages if `_requestIngress` is null (pipeline still materializing or retrying). Process queued registrations after successful materialization.

### Queued Registrations (Simple Stash Alternative)

When `RegisterConsumer` arrives but `_requestIngress` is null:
- Store the message in a `List<RegisterConsumer>` field.
- After successful materialization in `MaterializeStream`, process all queued messages.
- This handles the race between PreStart materialization and early RegisterConsumer messages.

This is simpler than Akka's Stash because it's scoped to one message type and processed at a known point.

## Testing

1. **Owner materializes pipeline on PreStart** — Create owner with config, verify `StreamInstanceCreated` is NOT needed (no message sent), verify pipeline is ready by registering a consumer.
2. **Owner retries on materialization failure** — Verify retry/backoff still works with constructor config.
3. **RegisterConsumer queued during materialization** — Send RegisterConsumer before pipeline is ready, verify it's processed after materialization completes.
4. **Manager creates owner with config** — Verify manager passes options and pipeline to owner constructor.

## Trade-offs

### Benefits
- Simpler mental model: owner = pipeline. Created = pipeline started.
- 3 message types removed.
- No `_streamRunning` guard in registration path.
- Manager doesn't need to manage channels — owner is self-contained.

### Costs
- Constructor becomes heavier (takes config params).
- Need a queued registration list for the materialization window.
