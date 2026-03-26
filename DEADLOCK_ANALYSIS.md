# GroupByHostKeyStage Deadlock Analysis (TASK-002-001)

## Current Flow

`TryFinish()` (line 141) is called when upstream finishes. It checks whether all subflows
are drained: no pending items and no active `OfferAsync` calls. If drained, it calls
`Queue.Complete()` on each live substream queue, then `CompleteStage()` immediately (line 159).

## Race Condition

`CompleteStage()` kills the stage actor scope. However, downstream BidiStages
(RetryBidiStage, CacheBidiStage) may still hold items to re-inject. The substream
queues are empty *from GroupByHostKeyStage's perspective*, but the substream actors
are alive — processing items through BidiStages that want to push retry/revalidation
requests back upstream.

**Concrete path:** RetryBidiStage maintains `_readyRetries` queue. When a 503 response
arrives, `TryEmitRetry()` checks `_requestDemand && _readyRetries.Count > 0`, then calls
`Push(_outRequest, request)`. If GroupByHostKeyStage already called `CompleteStage()`,
the outlet is dead — the push fails silently, causing a hang with no error message.

## Fix Location

Replace `CompleteStage()` (line 159) with a deferred `TryCompleteStage()` that checks
`_subflows.Values.All(state => state.IsDead)` — only completing when all substream
WatchTasks report completed. This lets BidiStages finish their re-injection cycles
before the stage actor scope dies.
