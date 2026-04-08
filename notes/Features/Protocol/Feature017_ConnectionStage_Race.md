---
title: "Feature 017: ConnectionStage Race Condition Fix"
description: "Fixed ConnectionStage premature completion race and replaced Assert.Same with content equivalence in redirect tests"
tags: [features, history, bugfix, connection, race-condition, akka-streams]
status: completed
---

# Feature 017: ConnectionStage Race Condition Fix

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Bug Fix |
| **Scope** | 2 steps |

## Description

Fixed a race condition in `ConnectionStage` where stage completion could be triggered before the inbound pump had fully drained, and fixed a test fragility in redirect handler tests.

- Replaced `Assert.Same` (reference equality) with content equivalence assertions in `RedirectHandler` tests. The original assertions were fragile because response objects could be recreated during redirect processing, causing false test failures even when content was identical.

- Fixed the `ConnectionStage` race condition — deferred stage completion until the inbound response pump had fully drained. Previously, if the upstream completed while the inbound pump still had buffered data, the stage could complete prematurely and drop the final response bytes. Fix: tracked pump drain state explicitly and only called `CompleteStage()` once both conditions were satisfied.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP/Streams/Stages/Routing/ConnectionStage.cs` | Race condition fix |
| `src/TurboHTTP.Tests/Features/RedirectHandlerTests.cs` | Test assertion fix |

## See Also

- [[Features/Infrastructure/Feature019_Stream_Survival\|Feature 019]] — related stream error absorption work
- [[Architecture/Layers/14-TRANSPORT_LAYER\|Transport Layer]] — connection lifecycle design
