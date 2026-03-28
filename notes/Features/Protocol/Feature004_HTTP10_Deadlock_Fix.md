---
title: "Feature 004: HTTP/1.0 Demand Propagation Deadlock Fix"
description: "Fixed a permanent demand stall in ConnectionReuseStage for HTTP/1.0 pipelines"
tags: [features, history, http10, deadlock, akka-streams, bugfix]
status: completed
---

# Feature 004: HTTP/1.0 Demand Propagation Deadlock Fix

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Bug Fix |
| **Scope** | 3 steps |

## Description

HTTP/1.0 integration tests exhibited a critical deadlock that did not occur in HTTP/1.1 or HTTP/2.0. The root cause was in `ConnectionReuseStage.TryPullIfReady()`: the stage intentionally skips the control signal outlet for HTTP/1.0 (connection reuse does not apply per RFC 9110 §9.2.1), but `TryPullIfReady()` required demand from **both** outlets (response + signal) before pulling upstream. For HTTP/1.0, the signal outlet demand (`_signalOutletDemand`) never became `true`, causing a permanent demand stall — no new responses were ever requested.

**Fix**: Gated the signal demand check on protocol version. For HTTP/1.0, `TryPullIfReady()` checks only `_responseOutletDemand` before pulling upstream (line 257: `if (!_isHttp10 && !_signalOutletDemand)`). This preserved the intentional signal-skip behaviour while unblocking upstream pulls.

### Verification

- All H10 integration tests completed without deadlock
- H11 (79/79) and H20 (72/72) showed zero regression
- 821/821 StreamTests passed

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp/Streams/Stages/Routing/ConnectionReuseStage.cs` | Fix applied here (lines 225–278) |

## See Also

- [[Features/Testing/Feature005_H10_Flakiness_Mitigation\|Feature 005]] — follow-on flakiness mitigation
- [[Architecture/Design/02-STAGE_PATTERNS\|Stage Patterns]] — demand propagation and FanOutShape semantics
