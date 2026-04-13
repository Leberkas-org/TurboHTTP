---
title: Stage Inlet/Outlet Port Naming
tags: [architecture, conventions, akka-streams]
created: 2026-04-13
updated: 2026-04-13
---

# Stage Inlet/Outlet Port Naming

All `GraphStage` inlet/outlet string names follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). C# field names mirror the same pattern.

## Patterns by Shape Type

| Shape Type | Inlet pattern | Outlet pattern | Example |
|-----------|--------------|----------------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` / `"Http20Correlation.Out"` |
| Custom multi-port | `StageName.In.Role` | `StageName.Out.Role` | `"Http20Connection.In.Server"` / `"Http20Connection.Out.Stream"` |

## C# Field Naming

- Simple shapes: `_in` / `_out`
- Multi-port shapes: `_inRole` / `_outRole`

## Rules

- **PascalCase** for all name segments
- **No protocol prefix** (not `Http11.Http11Encoder.In`)
- **Drop `Stage` suffix** (use `Http11Encoder`, not `Http11EncoderStage`)
- **Semantic role names**: `Request`, `Response`, `Final`, `Retry`, `Redirect`, `Signal`, `Miss`, `Hit`, `Server`, `Stream`, `App`
- **Globally unique port names** across the entire codebase

## Validation

Use the `stage-port-validator` agent (`.claude/agents/stage-port-validator`) to scan all stages for naming violations.
