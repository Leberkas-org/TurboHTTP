---
title: HTTP/1.0 Pipeline Reconnection Limitation
description: >-
  ExtractOptionsStage emits ConnectItem once per client — HTTP/1.0 redirect/retry
  cannot reconnect after connection-close
tags:
  - architecture
  - limitation
  - http10
  - pipeline
  - bug
aliases:
  - HTTP/1.0 Reconnection Bug
  - ExtractOptionsStage Limitation
---
# HTTP/1.0 Pipeline Reconnection Limitation

**Discovered**: 2026-03-23 during TASK-021-003
**Status**: 🔶 Known Limitation (not yet fixed)

## Problem

HTTP/1.0 redirect and retry integration tests are **BLOCKED** because the Akka.Streams pipeline cannot reconnect after HTTP/1.0 connection-close.

## Root Cause

`ExtractOptionsStage` (in `Streams/Stages/Routing/`) emits a `ConnectItem` only once (via `_initialSent` flag) and then completes the signal outlet. When HTTP/1.0 closes the connection after each response, `RedirectBidiStage` and `RetryBidiStage` recirculate follow-up requests, but these flow through the encoder without a new `ConnectItem` — so `ConnectionStage` has no handle and drops the data.

## Why This Matters

The pipeline architecture assumes one connection per client lifetime. HTTP/1.1 keep-alive and HTTP/2 multiplexing hide this assumption. HTTP/1.0's connection-close breaks it for multi-request features (redirect/retry).

## Possible Fixes

1. Make `ExtractOptionsStage` emit `ConnectItem` for reconnection scenarios
2. Have `ConnectionStage` auto-reconnect when receiving data without a handle
3. Add a reconnection signal path from `ConnectionReuseStage` back to `ExtractOptionsStage`

## Workarounds

- **Cookie tests work** because `CookieH10IntegrationTests` creates a fresh client per request with a shared `CookieJar`
- **Compression tests work** because they're single-request (no follow-up needed)
