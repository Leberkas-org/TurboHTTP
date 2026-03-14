# Plan: Fix 3 Failing Tests (OPT-002, HA-002, ETE-001)

## Introduction

Three tests are currently failing across two projects. The failures fall into two unrelated root causes: a wrong default value in `TurboClientOptions`, and a recent refactoring of `HostPoolActor.SpawnConnection()` that broke the assumptions of two actor-hierarchy tests.

---

## Goals

- Identify precisely why each test fails.
- Fix OPT-002 by removing the wrong default initializer in `TurboClientOptions`.
- Fix HA-002 and ETE-001 by adapting the tests to the new actor wiring (`Context.GetActor<ClientManager>()` instead of `Self`).
- Keep build green: 0 errors, 0 new failures after the changes.

---

## Failing Tests — Root Cause Analysis

### Failure 1 — OPT-002

| Property | Value |
|----------|-------|
| Test | `TurboHttp.Tests.Integration.TurboClientOptionsTests.All_Policy_Properties_Default_To_Null` |
| File | `src/TurboHttp.Tests/Integration/TurboClientOptionsTests.cs:18` |
| Error | `Assert.Null() Failure — Expected: null, Actual: RedirectPolicy { MaxRedirects = 10, AllowHttpsToHttpDowngrade = False }` |

**Root cause:**
`TurboClientOptions.cs` initializes policy properties to their `*.Default` singleton instead of `null`:

```csharp
// Current (broken)
public RedirectPolicy? RedirectPolicy { get; init; } = RedirectPolicy.Default;
public RetryPolicy?    RetryPolicy    { get; init; } = RetryPolicy.Default;
public CachePolicy?    CachePolicy    { get; init; } = CachePolicy.Default;
public ConnectionPolicy? ConnectionPolicy { get; init; } = ConnectionPolicy.Default;
```

The test `OPT-002` asserts that **all four properties default to `null`** — meaning "no policy configured yet." The intent (per the test's documentation comment referencing TASK-009) is that a freshly constructed `TurboClientOptions` must carry no policy objects; they must be set explicitly by the user.

**Fix:** Remove the `= *.Default` initializers so all four properties default to `null`.

---

### Failure 2 — HA-002

| Property | Value |
|----------|-------|
| Test | `TurboHttp.Tests.IO.HostPoolActorTests.HA_002_RoutingSelectsIdleConnectionQueue` |
| File | `src/TurboHttp.Tests/IO/HostPoolActorTests.cs:121` |
| Error | `Expected Akka.Event.UnhandledMessage, received HostStreamRefsReady { Key = http://test.local:8080 }` |

**Root cause:**
The test was written when `HostPoolActor.SpawnConnection()` used `Self` as the `clientManager` passed to `ConnectionActor`:

```csharp
// Old code (test was written for this)
var actor = Context.ActorOf(Props.Create(() => new ConnectionActor(_options, Self)));
```

The test expected that `ConnectionActor.PreStart()` would send `ClientManager.CreateTcpRunner` to `Self` (= `HostPoolActor`). Since `HostPoolActor` has no handler for `CreateTcpRunner`, that message would appear on the event stream as an `UnhandledMessage`, which the test captured to extract the `ConnectionActor` ref.

The recent change in `HostPoolActor.cs` (the only unstaged modification in `git status`) replaced `Self` with the real `ClientManager`:

```csharp
// New code (current state of the file — breaks the test)
var clientManager = Context.GetActor<ClientManager>();
var actor = Context.ActorOf(Props.Create(() => new ConnectionActor(_options, clientManager)));
```

In the test environment, **no `ClientManager` is registered** in the actor system. Therefore `Context.GetActor<ClientManager>()` throws (e.g., `ActorNotFoundException`). Akka's default supervision restarts the `HostPoolActor`. On restart, `PreStart()` runs again, materializes a new `MergeHub`, and sends a second `HostStreamRefsReady` to the parent (the proxy). The proxy forwards it to `TestActor`. The test, which is waiting for `UnhandledMessage`, receives `HostStreamRefsReady` instead — hence the failure.

**Fix options (choose one):**

| Option | Pros | Cons |
|--------|------|------|
| A — Register a `TestProbe` as `ClientManager` before creating the proxy, so `Context.GetActor<ClientManager>()` resolves to the probe | Tests remain close to reality; the probe naturally receives `CreateTcpRunner` as an `UnhandledMessage` on its mailbox (or the test can assert on the probe directly) | Requires the test to know how to register an actor under the `ClientManager` name |
| B — Revert `SpawnConnection()` to pass `Self` | Minimal change; tests pass again | Regression in production logic — `HostPoolActor` should not act as `ClientManager` |
| C — Update the test to use `TestProbe.ExpectMsg` on a registered `ClientManager` probe | Cleanest | Larger test change |

**Recommended**: Option A — register a `TestProbe` as the `ClientManager` in the test actor system before spawning the proxy. The `TestProbe` captures `CreateTcpRunner`, giving the test the `ConnectionActor` ref without relying on `UnhandledMessage`.

---

### Failure 3 — ETE-001

| Property | Value |
|----------|-------|
| Test | `TurboHttp.StreamTests.IO.ActorHierarchyStreamRefTests.ETE_001_FullHierarchy_ItemArrivesInTcpOutboundChannel` |
| File | `src/TurboHttp.StreamTests/IO/ActorHierarchyStreamRefTests.cs:56` |
| Error | `TimeoutException — Timeout 00:00:10 while waiting for UnhandledMessage` |

**Root cause:**
Same underlying change as HA-002. The test relies on `CreateTcpRunner` landing in `HostPoolActor` as an `UnhandledMessage` (because the actor was previously the `clientManager`). After the refactoring, `CreateTcpRunner` is sent to the real `ClientManager`, which is also not registered in this test project's actor system. The result is that either:
- `Context.GetActor<ClientManager>()` throws → `HostPoolActor` restarts → no `CreateTcpRunner` `UnhandledMessage` is published, only another `HostStreamRefsReady`.
- OR the message is delivered to `ActorRefs.Nobody` → silently dropped → no `UnhandledMessage` event at all.

Either way, the 10-second wait for `UnhandledMessage(CreateTcpRunner)` times out.

**Fix**: Same approach as HA-002 — register a `TestProbe` as `ClientManager` so `ConnectionActor` can resolve it and route `CreateTcpRunner` there. The test then intercepts it on the probe instead of via `UnhandledMessage`.

---

## User Stories

### TASK-001: Fix OPT-002 — Remove wrong default policy values

**Description:** As a developer using `TurboClientOptions`, I want all policy properties to default to `null` so that the options object starts in a "no policy configured" state, matching the contract documented in OPT-002.

**Acceptance Criteria:**
- [x] `TurboClientOptions.RedirectPolicy` property initializer changed from `= RedirectPolicy.Default` to no initializer (defaults to `null`)
- [x] Same for `RetryPolicy`, `CachePolicy`, `ConnectionPolicy`
- [x] `OPT-002` passes
- [x] `OPT-004` still passes (setting policies explicitly still works)
- [x] Build has 0 errors

---

### TASK-002: Fix HA-002 — Register ClientManager TestProbe before spawning proxy

**Description:** As a test author, I want the `HostPoolActorTests` to work correctly with the new `HostPoolActor.SpawnConnection()` logic that resolves `ClientManager` via `Context.GetActor<ClientManager>()`, so that the test captures the right actor ref without relying on the old `Self`-as-clientManager trick.

**Acceptance Criteria:**
- [ ] A `TestProbe` or minimal stub is registered in the actor system under the name that `Context.GetActor<ClientManager>()` resolves to, before the `HostPoolActorProxy` is created
- [ ] `HA-002` no longer receives a second `HostStreamRefsReady` where `UnhandledMessage` is expected
- [ ] The test correctly extracts the `ConnectionActor` ref from the `CreateTcpRunner` message
- [ ] `HA-001` still passes
- [ ] Build has 0 errors

---

### TASK-003: Fix ETE-001 — Register ClientManager TestProbe in ActorHierarchyStreamRefTests

**Description:** As a test author, I want the `ActorHierarchyStreamRefTests` to correctly intercept `CreateTcpRunner` after the `HostPoolActor` refactoring, so the end-to-end hierarchy test can extract the `ConnectionActor` ref and complete successfully.

**Acceptance Criteria:**
- [ ] A `TestProbe` or minimal stub is registered in the `StreamTestBase` actor system under the `ClientManager` actor name, before `PoolRouterActor` is created
- [ ] `ETE-001` completes within the 15-second timeout
- [ ] `DataItem` arrives in the TCP outbound channel with correct `Length = 8` and first byte `0xEE`
- [ ] Build has 0 errors

---

## Functional Requirements

- FR-1: `new TurboClientOptions()` must produce an object where `RedirectPolicy`, `RetryPolicy`, `CachePolicy`, and `ConnectionPolicy` are all `null`.
- FR-2: The fix must NOT change the behavior of production code — only `TurboClientOptions.cs` and the two test files may be changed.
- FR-3: After all fixes, `dotnet test ./src/TurboHttp.sln` must report 0 failing tests (excluding the already-crashing `TurboHttp.IntegrationTests` host, which is a separate pre-existing issue).
- FR-4: Actor-hierarchy tests must register a `TestProbe` as the `ClientManager` actor before any actor that calls `Context.GetActor<ClientManager>()` is created, to prevent the unresolved-actor exception that causes `HostPoolActor` to restart.

---

## Non-Goals

- Do NOT fix the `TurboHttp.IntegrationTests` test-host crash (all those timeouts are a separate, pre-existing issue with the unmaterialized client pipeline).
- Do NOT change `HostPoolActor.SpawnConnection()` — the new `Context.GetActor<ClientManager>()` approach is intentional.
- Do NOT change the public `TurboClientOptions` API beyond removing the wrong default initializers.

---

## Technical Considerations

- `Context.GetActor<ClientManager>()` (from `Servus.Akka`) resolves by actor type name in the actor system registry. In tests, you typically register a test actor via `Sys.ActorOf(Props.Create(...), actorName)` with the name that matches the resolver's expectation.
- Investigate the exact name that `Context.GetActor<ClientManager>()` looks up (likely `"ClientManager"` or a lowercase variant) before writing the fix. Use `Grep` on `Servus.Akka` sources or inspect how other actor tests register actors.
- `HostPoolActor` uses `ReceiveActor` with Akka's default supervision strategy. An exception thrown inside a `Receive<T>` handler causes the actor to restart (not stop), which re-runs `PreStart()` — explaining the second `HostStreamRefsReady`.

---

## Success Metrics

- `TurboHttp.Tests`: 2182/2182 passing (was 2180/2182).
- `TurboHttp.StreamTests`: 413/413 passing (was 412/413).
- `TurboHttp.IntegrationTests`: unchanged (pre-existing crash, not in scope).

---

## Open Questions

- What exact actor name does `Context.GetActor<ClientManager>()` (from `Servus.Akka`) look up? This determines what name to use when registering the `TestProbe` in the two failing tests.
- Should `TurboClientOptions` eventually expose a convenience factory like `TurboClientOptions.WithDefaults()` that populates all policies? (Out of scope for this plan — just a note for the backlog.)