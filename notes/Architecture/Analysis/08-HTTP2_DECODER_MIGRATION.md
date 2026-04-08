---
title: Http2Decoder Migration Plan
description: >-
  Migration from monolithic Http2Decoder to stage-based testing via
  Http2ProtocolSession and Http2StageTestHelper (Phases 39-62)
tags:
  - architecture
  - refactoring
  - http2
  - testing
  - migration
aliases:
  - Http2Decoder Removal
  - Stage Testing Migration
---
# Http2Decoder Migration Plan

**Last Updated**: 2026-03-26
**Plan File**: `IMPLEMENTATION_PLAN.md` (repo root)

## Problem

- `Http2Decoder` (55KB): monolithic test helper, NOT used in production
- 500+ test references create maintenance debt
- Gap between test code (Http2Decoder) and production code (Stages)
- RFC compliance: 185 HTTP/2 tests need architecture improvement

## Phase Status

### Phases 39-43: Stage Testing Foundation

| Phase | Description | Status | Effort |
|-------|-------------|--------|--------|
| 39 | Deprecate Http2Decoder, organize files | ✅ COMPLETE | 2-3h |
| 40 | Create Http2StageTestHelper framework | Ready | 8-10h |
| 41 | Migrate RFC9113 sections 1-5 (73 tests) | Ready | 12-16h |
| 42 | Migrate RFC9113 sections 6-9 (78 tests) | Ready | 12-16h |
| 43 | Validation gate + regression testing | Ready | 4-6h |

**Phase 39 commits**: `85309d5` & `6586949`

### Phases 44-62: Http2Decoder Removal

**Goal**: Remove `Http2Decoder`, `Http2DecodeResult`, `Http2StreamLifecycleState` from production

**Key**: Phase 44 creates `Http2ProtocolSession` (test helper in `src/TurboHTTP.Tests/Http2ProtocolSession.cs`) — lightweight stateful wrapper over `Http2FrameDecoder`.

### Migration Mapping

| Old (Http2Decoder) | New (Http2ProtocolSession) |
|---------------------|----------------------------|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `TryDecode(bytes, out _)` | `session.Process(bytes)` |
| `GetStreamLifecycleState(id)` | `session.GetStreamState(id)` |
| `GetActiveStreamCount()` | `session.ActiveStreamCount` |
| `GetMaxConcurrentStreams()` | `session.MaxConcurrentStreams` |
| `IsGoingAway` / `GetGoAwayLastStreamId()` | `session.IsGoingAway` / `session.GoAwayLastStreamId` |
| `result.Responses` | `session.Responses` |
| `Reset()` | `new Http2ProtocolSession()` |
| `ValidateServerPreface()` | `Http2StageTestHelper.ValidateServerPreface()` |

**Scope**: 22 files, ~428 Http2Decoder references
