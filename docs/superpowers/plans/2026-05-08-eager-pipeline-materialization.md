# Eager Pipeline Materialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move pipeline materialization from message-triggered (`CreateStreamInstance`) to constructor/`PreStart` in `ClientStreamOwner`, eliminating 3 message types and simplifying the registration flow.

**Architecture:** `ClientStreamOwner` takes `TurboClientOptions` and `PipelineDescriptor` in its constructor, materializes the pipeline in `PreStart`, and queues `RegisterConsumer` messages that arrive before materialization completes. `ClientStreamManager` passes config to the owner's constructor via `Props` instead of sending a separate `CreateStreamInstance` message.

**Tech Stack:** .NET 10, C#, Akka.Actor (ReceiveActor, Props), Akka.Streams, xUnit v3

---

## File Structure

- **Modify:** `src/TurboHTTP/Streams/Lifecycle/ClientStreamOwner.cs`  
  Constructor takes config. PreStart materializes. Remove CreateStreamInstance/StreamInstanceCreated/StreamInstanceFailed messages. Add queued registration list. Simplify retry logic.

- **Modify:** `src/TurboHTTP/Streams/Lifecycle/ClientStreamManager.cs`  
  Pass config to owner Props. Remove OwnerState channels. Remove Receive<StreamInstanceCreated> handler.

- **Modify:** `src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamOwnerSpec.cs`  
  Update all tests for constructor-based owner. Remove CreateStreamInstance message usage.

- **Modify:** `src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamManagerSpec.cs`  
  Update RegisterConsumer message if shape changes.

---

### Task 1: Refactor ClientStreamOwner to constructor-based initialization

Move config to constructor, materialize in PreStart, add registration queue, remove obsolete messages.

**Files:**
- Modify: `src/TurboHTTP/Streams/Lifecycle/ClientStreamOwner.cs`
- Modify: `src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamOwnerSpec.cs`

- [ ] **Step 1: Update ClientStreamOwner constructor and PreStart**

Change the constructor to accept config:
```csharp
private readonly TurboClientOptions _clientOptions;
private readonly PipelineDescriptor _pipeline;
private readonly List<RegisterConsumer> _pendingRegistrations = [];

public ClientStreamOwner(TurboClientOptions clientOptions, PipelineDescriptor pipeline)
{
    _clientOptions = clientOptions;
    _pipeline = pipeline;

    Receive<Shutdown>(_ => HandleShutdown());
    Receive<RegisterConsumer>(HandleRegisterConsumer);
    Receive<UnregisterConsumer>(HandleUnregisterConsumer);
    Receive<StreamSinkCompleted>(HandleStreamSinkCompleted);
    Receive<RetryCreateInstance>(_ => ExecuteRetryCreate());
    Receive<ShutdownTimeoutExpired>(_ => HandleShutdownTimeout());
}

protected override void PreStart()
{
    base.PreStart();
    MaterializeStream();
}
```

- [ ] **Step 2: Update MaterializeStream to use fields instead of parameter**

Change signature from `MaterializeStream(CreateStreamInstance create)` to `MaterializeStream()`. Replace all `create.ClientOptions` with `_clientOptions` and `create.Pipeline` with `_pipeline`. Remove the `_createRequester.Tell(new StreamInstanceCreated())` block. After successful materialization, process queued registrations:

```csharp
private void MaterializeStream()
{
    Tracing.For("Request").Info(this, "Materializing pipeline");
    _log.Debug("Materializing stream pipeline (BaseAddress={0})", _clientOptions.BaseAddress);

    try
    {
        var opts = _clientOptions;
        // ... pool config, transports, engine creation unchanged but using _clientOptions/_pipeline ...

        var engine = new Engine();
        var engineFlow = engine.CreateFlow(transports, _pipeline, _clientOptions);

        // ... materializer, KillSwitch, hub materialization unchanged ...

        _streamRunning = true;
        Tracing.For("Request").Debug(this, "Pipeline ready");
        _log.Debug("Stream pipeline materialized successfully");

        ProcessPendingRegistrations();
    }
    catch (Exception ex)
    {
        Tracing.For("Request").Warning(this, "Pipeline failed: {0}", ex.Message);
        _log.Error(ex, "Failed to materialize stream pipeline");
        CleanupResources();
        HandleMaterializationFailed(ex);
    }
}
```

- [ ] **Step 3: Add registration queueing**

Update `HandleRegisterConsumer`:
```csharp
private void HandleRegisterConsumer(RegisterConsumer message)
{
    if (!_streamRunning || _requestIngress is null || _responseFanoutSource is null || _materializer is null)
    {
        _pendingRegistrations.Add(message);
        return;
    }

    CreateConsumerChild(message);
}

private void CreateConsumerChild(RegisterConsumer message)
{
    var childName = $"consumer-{message.ConsumerId:N}";
    Context.ActorOf(ConsumerActor.Props(
        message.ConsumerId,
        message.RequestReader,
        message.OptionsFactory,
        message.FallbackResponseWriter,
        _requestIngress,
        _responseFanoutSource,
        _materializer), childName);

    _consumerPartitions[message.ConsumerId] = _nextPartitionIndex++;
}

private void ProcessPendingRegistrations()
{
    foreach (var pending in _pendingRegistrations)
    {
        CreateConsumerChild(pending);
    }

    _pendingRegistrations.Clear();
}
```

- [ ] **Step 4: Remove obsolete messages and handlers**

Delete these records:
- `CreateStreamInstance`
- `StreamInstanceCreated`
- `StreamInstanceFailed`

Delete these fields:
- `_createRequest`
- `_createRequester`

Delete these methods:
- `HandleCreateStreamInstance`
- `HandleStreamInstanceFailed`

Remove `Receive<CreateStreamInstance>` and `Receive<StreamInstanceFailed>` from constructor.

- [ ] **Step 5: Simplify retry logic**

`HandleMaterializationFailed` — remove `_createRequest is not null` check (config is always available via fields). Remove `_createRequester` notification:

```csharp
private void HandleMaterializationFailed(Exception ex)
{
    _lastError = ex;
    _retryAttempts++;

    _log.Warning("Stream materialization failed (attempt {0}/{1}): {2}",
        _retryAttempts, MaxRetryAttempts, ex.Message);

    if (_retryAttempts <= MaxRetryAttempts && !_shuttingDown && !IsSystemTerminating)
    {
        var backoff = CalculateBackoff(_retryAttempts - 1);
        _log.Info("Scheduling retry attempt {0} after {1}ms backoff",
            _retryAttempts, backoff.TotalMilliseconds);

        Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
    }
    else
    {
        _log.Error("Stream materialization failed after {0} attempts. Last error: {1}",
            _retryAttempts, _lastError?.Message);
    }
}
```

`ExecuteRetryCreate` — use field-based config:
```csharp
private void ExecuteRetryCreate()
{
    if (_shuttingDown)
    {
        return;
    }

    Tracing.For("Request").Debug(this, "Pipeline retry {0}/{1}", _retryAttempts, MaxRetryAttempts);
    _log.Info("Executing retry attempt {0}/{1}", _retryAttempts, MaxRetryAttempts);
    CleanupForRetry();
    MaterializeStream();
}
```

- [ ] **Step 6: Update CleanupResources**

Remove `_streamRunning` comment about external channel ownership (owner now owns nothing external). Clear `_pendingRegistrations`:

```csharp
// Add to CleanupResources:
_pendingRegistrations.Clear();
```

- [ ] **Step 7: Update ClientStreamOwnerSpec tests**

Update `CreateClientStreamOwner` helper:
```csharp
private IActorRef CreateClientStreamOwner(TurboClientOptions? options = null, PipelineDescriptor? pipeline = null)
    => Sys.ActorOf(Props.Create(() => new ClientStreamOwner(
        options ?? new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
        pipeline ?? PipelineDescriptor.Empty)));
```

Remove `CreateStreamInstanceMessage()` helper entirely.

Remove `ClientStreamOwner_should_respond_to_create_stream_instance_with_created` test (message no longer exists).

Update all tests that send `CreateStreamInstance` — the owner now materializes automatically in PreStart, so just create the actor and it's ready:

For consumer registration tests, change from:
```csharp
var actor = CreateClientStreamOwner();
var probe = CreateTestProbe();
probe.Send(actor, CreateStreamInstanceMessage());
_ = probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(...);
// then register consumer
```

To:
```csharp
var actor = CreateClientStreamOwner();
await Task.Delay(500, TestContext.Current.CancellationToken); // let PreStart materialize
// then register consumer
```

Remove `ClientStreamOwner_should_handle_failed_materialization` and `ClientStreamOwner_should_handle_multiple_failures_before_shutdown` tests (they send `StreamInstanceFailed` which no longer exists).

- [ ] **Step 8: Build and run stream tests**

Run: `cd src && dotnet build --configuration Release TurboHTTP.slnx && dotnet test --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`
Expected: BUILD SUCCEEDED. All stream tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/TurboHTTP/Streams/Lifecycle/ClientStreamOwner.cs src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamOwnerSpec.cs
git commit -m "refactor(owner): eager pipeline materialization in PreStart

Move config to constructor, materialize in PreStart instead of via
CreateStreamInstance message. Queue RegisterConsumer messages that
arrive before materialization completes. Remove CreateStreamInstance,
StreamInstanceCreated, StreamInstanceFailed messages."
```

---

### Task 2: Update ClientStreamManager to pass config via Props

Remove `CreateStreamInstance` Tell. Pass config directly to owner constructor. Simplify `OwnerState`.

**Files:**
- Modify: `src/TurboHTTP/Streams/Lifecycle/ClientStreamManager.cs`
- Modify: `src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamManagerSpec.cs`

- [ ] **Step 1: Update HandleRegisterConsumer in ClientStreamManager**

Change from:
```csharp
var owner = Context.ActorOf(
    Akka.Actor.Props.Create(() => new ClientStreamOwner()),
    sanitizedName);
owner.Tell(new ClientStreamOwner.CreateStreamInstance(
    message.ClientOptions,
    message.Pipeline));
state = new OwnerState(owner, requestChannel, responseChannel);
```

To:
```csharp
var owner = Context.ActorOf(
    Akka.Actor.Props.Create(() => new ClientStreamOwner(
        message.ClientOptions, message.Pipeline)),
    sanitizedName);
state = new OwnerState(owner);
```

- [ ] **Step 2: Simplify OwnerState and remove channels**

The manager no longer creates or owns channels — the owner is self-contained:
```csharp
private sealed record OwnerState(IActorRef Owner);
```

Update `HandleShutdown` — remove channel completion:
```csharp
private void HandleShutdown()
{
    foreach (var state in _owners.Values)
    {
        state.Owner.Tell(new ClientStreamOwner.Shutdown());
    }

    _owners.Clear();
    Context.Stop(Self);
}
```

- [ ] **Step 3: Remove Receive<StreamInstanceCreated> handler**

Delete this line from the constructor:
```csharp
Receive<ClientStreamOwner.StreamInstanceCreated>(_ => { /* stream ready, noop */ });
```

`StreamInstanceCreated` no longer exists.

- [ ] **Step 4: Build and run all tests**

Run: `cd src && dotnet build --configuration Release TurboHTTP.slnx && dotnet test --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj && dotnet test --project TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/TurboHTTP/Streams/Lifecycle/ClientStreamManager.cs src/TurboHTTP.StreamTests/Streams/Lifecycle/ClientStreamManagerSpec.cs
git commit -m "refactor(manager): pass config to owner via Props, remove channel ownership

Owner creates its own channels internally. Manager no longer sends
CreateStreamInstance or receives StreamInstanceCreated. OwnerState
simplified to just the IActorRef."
```

---

## Spec Coverage Checklist

- **Constructor takes config (Spec §1):** Task 1, Steps 1-2.
- **PreStart materialization (Spec §2):** Task 1, Step 1.
- **Remove messages (Spec §3):** Task 1, Step 4. Task 2, Step 3.
- **Registration queueing (Spec §5):** Task 1, Step 3.
- **Retry with field config (Spec §4):** Task 1, Step 5.
- **Manager update (Spec §6):** Task 2, Steps 1-3.
- **Tests (Spec §7):** Task 1, Step 7. Task 2, Step 4.

## Placeholder / Consistency Scan

- No `TODO` / `TBD` placeholders.
- `ClientStreamOwner(TurboClientOptions, PipelineDescriptor)` — consistent in owner constructor, manager Props creation, and test helpers.
- `MaterializeStream()` — no-arg, uses fields — consistent in PreStart and ExecuteRetryCreate.
- `ProcessPendingRegistrations()` — called after successful materialization and after retry success.
- `CreateConsumerChild(RegisterConsumer)` — extracted method used by both HandleRegisterConsumer and ProcessPendingRegistrations.
