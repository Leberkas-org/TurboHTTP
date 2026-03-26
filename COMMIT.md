TASK-028-004: Simplify Owner Shutdown — KillSwitch-Based Drain

Simplify ClientStreamOwnerActor shutdown to use KillSwitch directly instead of
PendingWorkTracker-based OnDrained callbacks.

- HandleShutdown() fires KillSwitch.Shutdown() directly; BidiStages drain via
  their internal TryCompleteIfDone() — no external tracker coordination needed
- Remove RequestInstanceShutdown() — logic consolidated into HandleShutdown()
- Add 5s safety timeout: if StreamSinkCompleted never arrives after KillSwitch,
  force-cleanup resources and stop the actor
- HandleStreamSinkCompleted() cancels safety timer on successful pipeline drain
- HandleShutdownTimeout() performs CleanupResources() + Context.Stop(Self)
- Update feature_001.md acceptance criteria for TASK-028-004
