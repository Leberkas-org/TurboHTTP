---
title: HTTP/1.0 Pipeline Reconnection Limitation
description: >-
  ExtractOptionsStage emits ConnectItem once per client — HTTP/1.0
  redirect/retry cannot reconnect after connection-close
tags:
  - architecture
  - http10
  - pipeline
  - resolved
aliases:
  - HTTP/1.0 Reconnection Bug
  - ExtractOptionsStage Limitation
status: resolved
---
# HTTP/1.0 Pipeline Reconnection Limitation

**Discovered**: 2026-03-23 during HTTP/1.0 redirect integration testing
**Status**: ✅ Resolved (Feature 030, TASK-030-006 + TASK-030-007)

## Problem (Historical)

HTTP/1.0 redirect and retry integration tests were **BLOCKED** because the Akka.Streams pipeline could not reconnect after HTTP/1.0 connection-close.

## Root Cause

`ExtractOptionsStage` emitted a `ConnectItem` only once (via `_initialSent` flag). When HTTP/1.0 closed the connection after each response, follow-up requests had no `ConnectItem` — so `ConnectionStage` had no handle and dropped data.

## Resolution

Resolved by the IConnectionScope architecture (Feature 030):

1. **ConnectionStage** now takes `IConnectionScope` instead of `ConnectionPool` — auto-reconnects when `DataItem` arrives with `_handle == null` via `scope.AcquireAsync()`
2. **ConnectionReuseFlowStage** replaced `ConnectionReuseStage` — calls `scope.ReturnAsync(canReuse)` which triggers transport callback for cleanup
3. **ExtractOptionsStage simplified** — `InReuse` inlet removed, `_needsReconnect` field removed, feedback loop eliminated
4. **Linear topology** — `BuildConnectionFlow()` is cycle-free, no `Broadcast(eagerCancel)` needed

The entire feedback loop (Broadcast + 2× MergePreferred + ExtractOptionsStage.InReuse) has been eliminated. Per-host `IConnectionScope` instances (`SingleRequestConnectionScope` for HTTP/1.0, `PersistentConnectionScope` for HTTP/1.1+) mediate connection lifecycle through method calls, not graph edges.

## See Also

- [[Architecture/Analysis/10-DEADLOCK_ANALYSIS|Deadlock Analysis Catalog]] — DL-006, DL-009, DL-010 all marked Fixed
