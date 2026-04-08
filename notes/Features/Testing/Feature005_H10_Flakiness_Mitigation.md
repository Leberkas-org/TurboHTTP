---
title: "Feature 005: HTTP/1.0 Integration Test Flakiness Mitigation"
description: "Three-phase mitigation of HTTP/1.0 test timeout failures caused by TCP connection churn and fixture contention"
tags: [features, history, http10, testing, flakiness, infrastructure]
status: in-progress
---

# Feature 005: HTTP/1.0 Integration Test Flakiness Mitigation

## Summary

| Field | Value |
|-------|-------|
| **Status** | 🔶 In Progress (Phase 1 partially complete) |
| **Category** | Test Infrastructure |
| **Scope** | 9 steps across 3 phases |

## Description

After the [[Features/Protocol/Feature004_HTTP10_Deadlock_Fix\|Feature 004 deadlock fix]], the H10 integration suite still showed 6–9 timeout failures per 88-test run (~10% failure rate). These were **not deadlocks** but resource contention timeouts caused by:

- **TCP connection churn** — HTTP/1.0 closes connections per response; 192 TCP connections across the suite with 100ms overhead each
- **Shared fixture bottleneck** — Single `ServerFixture` + `ActorSystemFixture` for all H10 tests
- **Timeout mismatch** — 10s inner timeout vs 30s outer timeout left thin margins under GC pauses
- **Actor system thread pool starvation** — Cleanup messages draining the pool between tests
- **Blocking routes** — `/delay/10000` (10-second block) in `ErrorHandlingIntegrationTests` monopolizing Kestrel

### Three-Phase Mitigation Plan

| Phase | Changes | Target |
|-------|---------|--------|
| Phase 1 | Timeout 10s→15s, explicit `DisposeAsync()`, isolate `ErrorHandlingIntegrationTests` | <2 timeouts/run |
| Phase 2 | Parallelise collections, tune ActorSystem thread pool (8→16 threads) | <1 timeout/run |
| Phase 3 | Dedicated fixtures for `RedirectIntegrationTests`, `RetryIntegrationTests` | 0 timeouts/run |

**Phase 1 status**: Timeout increase and explicit cleanup steps completed.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP.IntegrationTests/H10/` | 10 affected test classes (88 tests total) |
| `src/TurboHTTP.IntegrationTests/Shared/` | `ActorSystemFixture`, `ServerFixture` |

## See Also

- [[Features/Protocol/Feature004_HTTP10_Deadlock_Fix\|Feature 004]] — prerequisite deadlock fix
- [[Architecture/Guides/12-TEST_ORGANIZATION\|Test Organization]] — collection structure and fixture patterns
