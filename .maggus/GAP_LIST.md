# Production-Readiness Gap List

**Date:** 2026-03-16
**Based on:** TASK-AUD-001 through AUD-005, TASK-DEC-001 (Option A recommended)
**Architecture:** Evolve current Actor Pool incrementally

---

## Summary

| Priority | Count |
|----------|-------|
| Critical | 3 |
| High | 5 |
| Medium | 4 |
| Low | 3 |
| **Total** | **15** |

---

## Critical Priority

### GAP-001: Connection Reuse — Wire `ConnectionReuseStage` into Engine

**Priority:** Critical
**Complexity:** S
**Dependencies:** None

`ConnectionReuseStage` exists and has 10 passing stream tests, but is **dead code** — never referenced in `Engine.cs`. Without it, HTTP/1.1 connections are never evaluated for keep-alive vs close, meaning every request potentially creates a new TCP connection.

**What to do:**
- Wire `ConnectionReuseStage` into `BuildConnectionFlowPublic` after the BidiFlow decode output
- Feed `ConnectionReuseDecision` back to `HostPoolActor` via a `MarkConnectionNoReuse` message
- Verify with existing `ConnectionReuseStageTests` + new integration test against `/conn/keep-alive`

**Source:** AUD-001, AUD-003

---

### GAP-002: Error Tolerance — Fix `ConnectionActor.Reconnect()` Failure Notification

**Priority:** Critical
**Complexity:** M
**Dependencies:** None

`ConnectionActor.Reconnect()` never sends `ConnectionFailed` to its parent `HostPoolActor`. This means:
- `HostPoolActor.HandleFailure` is dead code
- No exponential backoff on reconnect (immediate retry loop on server down)
- `MaxReconnectAttempts` config is never read
- In-flight requests are silently lost (NullReferenceException on stale `_outbound`)

**What to do:**
- Send `ConnectionFailed` to parent before calling `Connect()` in `Reconnect()`
- Add exponential backoff using `PoolConfig.ReconnectInterval` as base
- Track reconnect attempts against `MaxReconnectAttempts`
- Null-guard `_outbound` in `SinkRef` write lambda to prevent NRE

**Source:** AUD-004

---

### GAP-003: Error Tolerance — Stale Queue Cleanup in `HostPoolActor`

**Priority:** Critical
**Complexity:** S
**Dependencies:** GAP-002 (requires `ConnectionFailed` to be sent)

When a connection drops, the stale `_connectionQueues[conn.Actor]` entry persists in `HostPoolActor`. New requests arriving during the reconnect window are routed to the dead queue and silently lost.

**What to do:**
- In `HostPoolActor.HandleFailure`, remove the stale queue entry from `_connectionQueues`
- Mark the connection as `Active = false` in `_connections`
- Re-queue any pending items from the dead queue to another active connection (or buffer for reconnect)

**Source:** AUD-004

---

## High Priority

### GAP-004: Integration Tests — End-to-End `SendAsync()` Against Kestrel

**Priority:** High
**Complexity:** M
**Dependencies:** GAP-001, GAP-002, GAP-003 (fixes needed before meaningful E2E testing)

Zero integration test classes exist. Kestrel fixtures are defined with 60+ routes but no test classes consume them. This means the full pipeline (SendAsync → Engine → TCP → Kestrel → response) has never been exercised against a real server.

**What to do:**
- Create test classes that call `TurboHttpClient.SendAsync()` against `KestrelFixture`
- Cover: basic GET/POST, redirect chains, cookie round-trips, cache behavior, retry on 503, connection reuse
- Verify HTTP/1.0, HTTP/1.1, and HTTP/2 paths

**Source:** AUD-005

---

### GAP-005: Graceful Shutdown — `TurboHttpClient` / `TurboClientStreamManager` Disposal

**Priority:** High
**Complexity:** M
**Dependencies:** None

Neither `TurboHttpClient` nor `TurboClientStreamManager` implement `IDisposable` or `IAsyncDisposable`. There is no graceful shutdown path:
- No way to drain in-flight requests before closing
- No way to shut down the Akka ActorSystem or stream materialization
- No way to cancel pending `TaskCompletionSource` entries on dispose
- Potential for hanging TCP connections and leaked actors

**What to do:**
- Implement `IAsyncDisposable` on `TurboHttpClient` and `TurboClientStreamManager`
- Complete the request channel on dispose to stop accepting new requests
- Drain pending responses with a configurable timeout
- Cancel all outstanding `TaskCompletionSource` entries
- Stop the `PoolRouterActor` actor hierarchy
- Terminate the Akka stream materialization

---

### GAP-006: Per-Host Connection Limits — Wire `PerHostConnectionLimiter`

**Priority:** High
**Complexity:** S
**Dependencies:** None

`PerHostConnectionLimiter` exists and is unit-tested, but never integrated into the actor hierarchy. Without it, there is no limit on how many concurrent TCP connections are opened to a single host, which can cause resource exhaustion or server-side rejection.

**What to do:**
- Gate `SpawnConnection()` in `HostPoolActor` against `PerHostConnectionLimiter`
- Respect `PoolConfig.MaxConnectionsPerHost` (or equivalent)
- Queue excess requests until a connection slot becomes available

**Source:** AUD-005

---

### GAP-007: HTTPS/TLS — Verify End-to-End TLS Path

**Priority:** High
**Complexity:** S–M
**Dependencies:** GAP-004 (needs integration test infrastructure)

`TlsClientProvider` and `TlsOptions` exist in the I/O layer. `ClientManager` routes to `TlsClientProvider` when `TlsOptions` is detected. `KestrelTlsFixture` is defined. However, there are **no integration tests** verifying that HTTPS requests work end-to-end through the full pipeline.

**What to do:**
- Verify `TcpOptionsFactory` correctly produces `TlsOptions` for `https://` URIs
- Write integration tests against `KestrelTlsFixture`
- Test certificate validation, SNI, and TLS handshake through the actor pool path
- Verify HTTP/2 over TLS (ALPN negotiation)

---

### GAP-008: CLAUDE.md — Update Outdated "Current Limitations" Section

**Priority:** High
**Complexity:** S
**Dependencies:** GAP-001 through GAP-006 (update after fixes, or update now to reflect audit findings)

The audit (AUD-005) found that 4 out of 5 "Current Limitations" statements in CLAUDE.md are **outdated**:
- "Pipeline not fully wired" → All handlers ARE wired via `BuildExtendedPipeline`
- "Client graph not materialized" → Graph IS materialized
- "SendAsync does not work end-to-end" → SendAsync IS fully implemented
- "No business logic stages" → 9 stages exist and are wired

**What to do:**
- Rewrite "Current Limitations" to reflect the actual state discovered by the audit
- Keep accurate limitations: no integration tests, `ConnectionReuseStage` not wired, `PerHostConnectionLimiter` not wired
- Update as each gap is closed

**Source:** AUD-005

---

## Medium Priority

### GAP-009: HTTP/2 Multiplexing — Real TCP Integration Test

**Priority:** Medium
**Complexity:** M
**Dependencies:** GAP-004 (integration test infrastructure)

HTTP/2 multiplexing is verified at the stream/stage level with fake TCP, but there is no test that sends multiple concurrent requests to a real Kestrel HTTP/2 server and confirms they share one TCP connection.

**What to do:**
- Write integration test against `KestrelH2Fixture`
- Send N concurrent requests, verify all responses arrive
- Optionally verify connection count (e.g., via server-side connection tracking or stream IDs)

**Source:** AUD-003

---

### GAP-010: Observability — Logging and Metrics

**Priority:** Medium
**Complexity:** M
**Dependencies:** None

There are zero references to `ILogger`, `Meter`, `ActivitySource`, or OpenTelemetry anywhere in the `TurboHttp` production code. For production use, operators need:
- Structured logging for connection lifecycle events (connect, disconnect, reconnect, failure)
- Metrics: request count, latency histogram, connection pool size, cache hit rate
- Distributed tracing: `Activity`/`ActivitySource` for request spans

**What to do:**
- Add `ILogger` to key actors (ConnectionActor, HostPoolActor) and stages
- Add `Meter` with counters/histograms for request throughput and latency
- Add `ActivitySource` for distributed tracing through the pipeline
- Keep dependencies optional (no hard requirement on OpenTelemetry packages)

---

### GAP-011: Dead Code Cleanup — Remove Unused Stages

**Priority:** Medium
**Complexity:** S
**Dependencies:** GAP-001 (after `ConnectionReuseStage` is wired, reassess)

Four stages exist as dead code:
- `ExtractOptionsStage` — role superseded by `RequestEnricherStage`
- `GroupByHostKeyStage` — Engine uses built-in `.GroupBy()` DSL
- `MergeSubstreamsStage` — Engine uses built-in `.MergeSubstreams()` DSL
- `ConnectionReuseStage` — dead code (becomes live after GAP-001)

**What to do:**
- After GAP-001, delete `ExtractOptionsStage`, `GroupByHostKeyStage`, `MergeSubstreamsStage` and their tests
- Keep `ConnectionReuseStage` (wired after GAP-001)

**Source:** AUD-001

---

### GAP-012: HTTP/3 — Stub Engine

**Priority:** Medium
**Complexity:** L
**Dependencies:** None (informational — not blocking production for HTTP/1.x + HTTP/2)

`Http30Engine` exists as a stub with a TODO comment. Port 3 in the Engine partition routes to it. It returns an empty BidiFlow. HTTP/3 (QUIC) support is not implemented.

**What to do:**
- For now: document as a known limitation, not a production blocker
- Future: implement HTTP/3 over QUIC when .NET QUIC APIs stabilize

---

## Low Priority

### GAP-013: `IHttpClientFactory` Compatibility

**Priority:** Low
**Complexity:** M
**Dependencies:** GAP-005 (disposal pattern needed first)

`TurboHttpClient` does not implement `HttpClient`-compatible interfaces. Users cannot use it as a drop-in replacement via `IHttpClientFactory` or `HttpMessageHandler`.

**What to do:**
- Consider implementing `HttpMessageHandler` to allow `TurboHttpClient` to be used as a handler for `HttpClient`
- This would enable `IHttpClientFactory` registration and familiar API patterns

**Source:** Open question in plan_5.md

---

### GAP-014: Configuration Validation

**Priority:** Low
**Complexity:** S
**Dependencies:** None

`TurboClientOptions` and `PoolConfig` accept arbitrary values without validation. Invalid configs (e.g., `MaxConnectionsPerHost = 0`, `ConnectTimeout = TimeSpan.Zero`) could cause runtime failures.

**What to do:**
- Add `IValidateOptions<TurboClientOptions>` or constructor validation
- Validate at startup, fail fast with clear error messages

---

### GAP-015: Dead Configuration — `MaxReconnectAttempts` and `ReconnectInterval`

**Priority:** Low
**Complexity:** S
**Dependencies:** GAP-002 (becomes live after reconnect fix)

`PoolConfig.MaxReconnectAttempts` and `PoolConfig.ReconnectInterval` are defined but never read by `ConnectionActor`. They become relevant only after GAP-002 is implemented.

**What to do:**
- After GAP-002, verify these config values are actually consumed
- If GAP-002 introduces its own backoff logic, ensure it reads from `PoolConfig`

**Source:** AUD-004

---

## Dependency Graph

```
GAP-001 (ConnectionReuseStage)  ──────────────────────┐
GAP-002 (Reconnect fix)  → GAP-003 (Stale queue)      ├──→ GAP-004 (Integration tests)
GAP-005 (Graceful shutdown)                            │         ↓
GAP-006 (Per-host limits)  ────────────────────────────┘    GAP-007 (TLS E2E)
GAP-008 (CLAUDE.md update) ← depends on all above          GAP-009 (H2 multiplex E2E)

GAP-010 (Observability)     — independent
GAP-011 (Dead code cleanup) — after GAP-001
GAP-012 (HTTP/3)            — independent, future
GAP-013 (IHttpClientFactory) — after GAP-005
GAP-014 (Config validation)  — independent
GAP-015 (Dead config)        — after GAP-002
```

---

## Recommended Implementation Order

| Phase | Gaps | Goal |
|-------|------|------|
| **Phase 1: Core Fixes** | GAP-001, GAP-002, GAP-003, GAP-006 | Connection reuse + error tolerance + limits |
| **Phase 2: Client Completeness** | GAP-005, GAP-008 | Disposal + accurate documentation |
| **Phase 3: Validation** | GAP-004, GAP-007, GAP-009 | Integration tests prove everything works |
| **Phase 4: Polish** | GAP-010, GAP-011, GAP-014, GAP-015 | Observability, cleanup, validation |
| **Future** | GAP-012, GAP-013 | HTTP/3, HttpClientFactory |
