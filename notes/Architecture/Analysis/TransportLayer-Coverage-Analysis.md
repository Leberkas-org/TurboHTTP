---
title: Transport Layer — Coverage Analysis
description: 'Code coverage gaps in TurboHTTP.Transport.* (unit + stream tests, 2026-04-22)'
tags:
  - coverage
  - transport
  - testing
date: '2026-04-22'
---
## Measurement

- **Branch:** `feature/seperate-transportlayer`
- **Date:** 2026-04-22
- **Test suites:** `TurboHTTP.Tests` + `TurboHTTP.StreamTests`
- **Tool:** Coverlet 10.0 (OpenCover) + ReportGenerator 5.5.6
- **Scope:** `[TurboHTTP]TurboHTTP.Transport.*`

| Metric | Value |
|---|---|
| Line coverage | **80%** (2025 / 2529) |
| Branch coverage | **75.5%** (604 / 800) |
| Method coverage | **89.9%** (269 / 299) |
| Fully covered methods | **73.9%** (221 / 299) |

---

## Per-Class Coverage

| Class | Line % | Priority |
|---|---|---|
| `QuicConnectionFactory` | 7.6% | Critical |
| `QuicConnectionStage` | 13.8% | Critical |
| `QuicClientProvider` | 48.5% | Critical |
| `QuicPumpManager` | 46.5% | Critical |
| `TcpTransportFactory` | 57.1% | High |
| `TcpPumpManager` | 66.1% | High |
| `QuicTransportStateMachine` | 73.5% | High |
| `TlsClientProvider` | 73.2% | High |
| `QuicTransportFactory` | 75.0% | High |
| `QuicConnectionHandle` | 77.2% | Medium |
| `QuicConnectionManagerActor` | 81.6% | Medium |
| `TcpConnectionManagerActor` | 82.8% | Medium |
| `TcpClientProvider` | 83.1% | Medium |
| `TcpConnectionFactory` | 81.8% | Medium |
| `TcpConnectionStage` | 85% | Medium (branch-only gap) |
| `ClientByteMover` | 89.6% | Low (branch-only gap) |
| `TcpTransportStateMachine` | 96.4% | Low |
| `QuicStreamRouter` | 97.4% | Low |
| `ClientState` | 97.9% | Low |
| `InboundStreamReady` | 0% | Structural zero |
| `ITransportOperations` | 0% | Structural zero (interface default) |

---

## Gap Details

### Critical (0–50%)

**`QuicConnectionFactory` — 7.6%**
- `EstablishAsync` body entirely untested — QUIC connection setup never exercised in tests

**`QuicConnectionStage` — 13.8%**
- `CreateLogic` + all stage callbacks (`OnPush`, `OnPull`, `OnUpstreamFinish`) untested
- The entire QUIC stage logic is unused in current test suite

**`QuicClientProvider` — 48.5%**
- `ConnectAsync` public wrapper not called
- Error path in `EnsureConnectedAsync` under contention not exercised

**`QuicPumpManager` — 46.5%**
- `AcceptLoopAsync`: null inbound-stream cancel path, unknown stream-type error path
- `PumpAsync`: `OperationCanceledException`, `AbruptCloseException`, `ChannelClosedException` catch blocks

---

### High (50–75%)

**`TcpTransportFactory` — 57.1%**
- `Flow.FromGraph(...)` factory call not directly unit-tested

**`TcpPumpManager` — 66.1%**
- Cancellation exit, batch-grow/flush, error catches (`ChannelClosedException`, generic `Exception`)

**`QuicTransportStateMachine` — 73.5%**
- Timer timeout when `_pendingConnect` exists
- Early-data rejection path
- Generation mismatch warnings
- `AllOutboundTypedStreamsReady` failure branch
- Missing typed-stream in `OnTypedLeaseAcquired`

**`TlsClientProvider` — 73.2%**
- Branch gaps in proxy CONNECT handling (no uncovered sequence points — branch-only)

**`QuicTransportFactory` — 75%**
- `Flow.FromGraph(new QuicConnectionStage(...))` factory call not covered

---

### Medium (76–90%)

**`QuicConnectionHandle` — 77.2%**
- `QuicStream.CompleteWrites()` callback + error handling path

**`QuicConnectionManagerActor` — 81.6%**
- Cancelled-TCS check in `OnAcquire`
- Stream-capacity scanning when no reusable streams found
- Cancellation during handoff after `Established`

**`TcpConnectionManagerActor` — 82.8%**
- Cancelled TCS in `OnAcquire`
- HTTP/1.1 reuse conflict on establishment
- Stale-lease eviction path
- Failed-establishment cascade to pending queue

**`TcpClientProvider` — 83.1%**
- `ObjectDisposedException` guard in `DisposeAsync` (race: socket already disposed)

**`TcpConnectionFactory` — 81.8%**
- Explicit interface delegation `IConnectionFactory.EstablishAsync` not exercised

---

### Low (90–99%)

**`TcpTransportStateMachine` — 96.4%**
- Instrumentation on `AcquisitionFailed` + TLS activity error logging
- `ReturnLeaseToPool` safeguard checks (double-return guard)

**`QuicStreamRouter` — 97.4%**
- Null-handle warning path: data arrives before handle assigned (protocol violation guard)

**`ClientState` — 97.9%**
- `StreamDirection` getter — likely `SkipAutoProps` not filtering this property variant

---

### Structural Zeros

**`InboundStreamReady` (0%)**
- Server-initiated QUIC inbound stream event — no test exercises this path at all

**`ITransportOperations` (0%)**
- Interface definition with default no-op `OnConnectionReadyForSetup` method
- Coverage tool registers the default implementation as an uncovered line

---

## Recommended Work Order

1. **QUIC stage + factory tests** — `QuicConnectionStage`, `QuicConnectionFactory`, `QuicTransportFactory` via Akka.Streams TestKit with a fake QUIC connection. Fixes ~300 uncovered lines at once.
2. **Pump error-path injection** — `TcpPumpManager` + `QuicPumpManager`: inject `ChannelClosedException`, `AbruptCloseException`, trigger cancellation mid-pump.
3. **`QuicTransportStateMachine` edge cases** — connection-timeout and generation-mismatch paths; small isolated additions to existing spec files.
4. **Connection manager actor concurrency** — `TcpConnectionManagerActor` + `QuicConnectionManagerActor`: stale-lease eviction, cancelled-TCS, and handoff-after-establish scenarios.
5. **`InboundStreamReady` path** — Add at least one test that routes a server-initiated QUIC stream through the pipeline.
