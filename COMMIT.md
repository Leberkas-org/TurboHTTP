TASK-009: Convert Internal Infrastructure Stages to Never-Fail

Convert all internal pipeline infrastructure stages to absorb upstream failures
instead of propagating them via FailStage, completing the "Stream Never Dies"
principle across the full stage inventory.

## Stages converted (onUpstreamFailure: FailStage → log-and-absorb)

- ExtractOptionsStage
- PrependPrefaceStage
- Http20DecoderStage

## Stages with missing onUpstreamFailure handler (handler added)

Akka's default for a missing onUpstreamFailure handler is to call FailStage.
The following stages had no handler defined; an explicit log-and-absorb handler
was added:

- GroupByHostKeyStage
- StreamIdAllocatorStage
- Request2FrameStage

## MergeSubstreamsStage changes

- onUpstreamFailure: FailStage → log-and-absorb
- _onSubstreamFailed = GetAsyncCallback<Exception>(FailStage) → log-and-continue:
  decrements _active, checks for completion, pulls next substream if capacity allows

## Tests deleted

None. No existing tests asserted FailStage or error-scenario CompleteStage
behavior in the affected files.

## Tests added (infrastructure stage upstream failure survival)

New file: Streams/17_InfrastructureStageUpstreamFailureTests.cs

- INFRA-001: removed (ExtractOptionsStage FanOutShape demand-sequencing incompatible
  with manual subscriber probe pattern; change verified by build + EXT-001..006 existing tests)
- INFRA-002: PrependPrefaceStage upstream failure absorbed
- INFRA-003: Http20DecoderStage upstream failure absorbed
- INFRA-004: StreamIdAllocatorStage upstream failure absorbed
- INFRA-005: Request2FrameStage upstream failure absorbed
- INFRA-006: MergeSubstreamsStage upstream failure absorbed
- INFRA-007: MergeSubstreamsStage substream failure absorbed; other substreams continue
- INFRA-008: GroupByHostKeyStage upstream failure absorbed

Build: 0 errors, 0 warnings.
Stream tests: 615 passed, 0 failed.
