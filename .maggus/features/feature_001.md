# Feature 001: HTTP/3 Handler Integration — Wire Unused RFC 9114 Components into Pipeline

## Introduction

10 HTTP/3 protocol handlers (1,236 lines) were implemented with full unit test coverage but never integrated into the Akka.Streams pipeline. The HTTP/3 base pipeline (`Http30Engine` → 6 stages) works for basic request/response, but lacks security validation, idle timeout tracking, connection reuse evaluation, and push defense.

This feature wires all handlers into their respective stages, removes redundant code, and adds StreamTests for every integration point.

### Architecture Context

- **Components involved:**
  - `Http30StreamStage` (response header validation)
  - `Http30Request2FrameStage` (request origin validation)
  - `Http30ConnectionStage` (idle timeout, push defense)
  - `ConnectionReuseStage` (version-dispatch for HTTP/3)
  - `QuicClientProvider` (certificate validation for connection coalescing)
- **New patterns:** Version-dispatch in `ConnectionReuseStage`, defensive push rejection (MAX_PUSH_ID=0)
- **Removal:** `Http3SettingsExchange` if confirmed redundant to `Http3ControlStream`

## Goals

- Wire all 10 HTTP/3 protocol handlers into the production pipeline (zero dead code)
- Enforce RFC 9114 security requirements: field validation (§4.2), origin validation (§10.3), certificate validation (§3.3)
- Add idle timeout tracking (§5.1) to prevent stale connections
- Defensively reject server push via MAX_PUSH_ID=0 (§10.5)
- Extend `ConnectionReuseStage` with HTTP/3 version-dispatch
- Remove `Http3SettingsExchange` if redundant
- StreamTest coverage for every integration point

## Tasks

### TASK-001-001: Integrate Http3FieldValidator into Http30StreamStage
**Description:** As a client, I want response headers validated per RFC 9114 §4.2 so that malformed or forbidden headers are rejected before building HttpResponseMessage.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-002, TASK-001-003, TASK-001-004, TASK-001-005

**Acceptance Criteria:**
- [x] `Http3FieldValidator.Validate(headers)` called in `Http30StreamStage` after QPACK decode, before status/header extraction
- [x] `Http3ConnectionException` from validator propagates as stage failure
- [x] Uppercase header names rejected (e.g. `Content-Type` → error)
- [x] Connection-specific headers rejected (`Connection`, `Transfer-Encoding`, `Upgrade`, `Keep-Alive`)
- [x] `TE` header allowed only with value `trailers`
- [x] NUL, CR, LF in header values rejected
- [x] Existing unit tests still pass
- [x] Build succeeds with zero errors

---

### TASK-001-002: Integrate Http3OriginValidator into Http30Request2FrameStage
**Description:** As a client, I want request URIs validated per RFC 9114 §10.3 so that intermediary encapsulation attacks are prevented.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-003, TASK-001-004, TASK-001-005

**Acceptance Criteria:**
- [x] `Http3OriginValidator.Validate(request.RequestUri, isConnect)` called in `Http30Request2FrameStage.OnPush()` before `_encoder.Encode()`
- [x] CONNECT method detected via `request.Method == HttpMethod.Connect` and passed as `isConnect=true`
- [x] URIs with userinfo (`user:pass@host`) rejected
- [x] Empty scheme rejected
- [x] Empty path rejected (non-CONNECT)
- [x] Fragment in URI rejected
- [x] `Http3ConnectionException` propagates as stage failure
- [x] Existing unit tests still pass
- [x] Build succeeds with zero errors

---

### TASK-001-003: Integrate Http3CertificateValidator into QuicClientProvider
**Description:** As a client, I want TLS certificates validated for hostname coverage per RFC 9114 §3.3 so that connection coalescing is safe.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-002, TASK-001-004, TASK-001-005
**Model:** opus — needs careful analysis of QuicConnection certificate access API

**Acceptance Criteria:**
- [x] After `QuicConnection.ConnectAsync()`, retrieve server certificate via `_connection.RemoteCertificate`
- [x] Call `Http3CertificateValidator.CoversHostname(cert, hostname)` to validate SAN/CN match
- [x] If validation fails, close connection and throw descriptive exception
- [x] Wildcard matching works (`*.example.com` matches `api.example.com`)
- [x] CN fallback used when no SAN dNSName entries exist
- [x] Validation skipped if custom `ServerCertificateValidationCallback` is set in `QuicOptions`
- [x] Existing unit tests still pass
- [x] Build succeeds with zero errors

---

### TASK-001-004: Integrate Http3IdleTimeoutHandler into Http30ConnectionStage
**Description:** As a client, I want idle connections tracked per RFC 9114 §5.1 so that stale QUIC connections are detected and closed.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-002, TASK-001-003, TASK-001-005

**Acceptance Criteria:**
- [x] `Http3IdleTimeoutHandler` created in `Http30ConnectionStage` Logic constructor with configurable timeout (default 30s from `QuicOptions.IdleTimeout`)
- [x] `RecordActivity()` called on every frame received in `HandleServerFrame()`
- [x] `OnStreamOpened()` called when request frame sent outbound
- [x] `OnStreamClosed()` called when response fully assembled
- [x] `IsIdleTimeoutExpired()` checked periodically (via `ScheduleOnce` timer callback)
- [x] When timeout expires and `ActiveStreamCount == 0`: send GOAWAY and complete stage
- [x] `ComputeEffectiveTimeout()` used to reconcile local + remote idle timeout from SETTINGS
- [x] Existing unit tests still pass
- [x] Build succeeds with zero errors

---

### TASK-001-005: Add HTTP/3 Version-Dispatch to ConnectionReuseStage
**Description:** As a client, I want `ConnectionReuseStage` to use `Http3ConnectionReuseEvaluator` for HTTP/3 responses so that cross-origin connection reuse is properly evaluated.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-002, TASK-001-003, TASK-001-004

**Acceptance Criteria:**
- [x] In `ConnectionReuseStage.Logic.OnPush()`: check `response.Version.Major >= 3`
- [x] HTTP/3 responses evaluated via `Http3ConnectionReuseEvaluator.Evaluate()` instead of `ConnectionReuseEvaluator`
- [x] Parameters sourced from response: scheme, host, port from `RequestMessage.RequestUri`
- [x] `serverCertificate` passed from connection state (may need to thread through pipeline — document if not available)
- [x] `isGoingAway` from connection state (may need signal from `Http30ConnectionStage`)
- [x] HTTP/1.x and HTTP/2 behavior unchanged (regression-safe)
- [x] Existing unit tests still pass
- [x] Build succeeds with zero errors

---

### TASK-001-006: Defensive Push Rejection in Http30ConnectionStage (MAX_PUSH_ID=0)
**Description:** As a client, I want to defensively reject all server pushes by sending MAX_PUSH_ID=0 and enforcing push limits so that DoS via excessive pushes is prevented (RFC 9114 §10.5).

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-001 through TASK-001-005
**Model:** opus — complex multi-handler coordination

**Acceptance Criteria:**
- [ ] `Http3MaxPushIdHandler` created in Logic constructor
- [ ] `Http3PushLimiter` created with `maxPushCount: 0` (reject all pushes)
- [ ] `Http3CancelPushHandler` created with `maxPushIdHandler` dependency
- [ ] `Http3PushPromiseValidator` created with `maxPushIdHandler` dependency
- [ ] In `PreStart()`: send `MAX_PUSH_ID` frame with pushId=0 on control stream via `_maxPushIdHandler.CreateMaxPushId(0)`
- [ ] In `HandleServerFrame()`: if `Http3PushPromiseFrame` received, call `_pushLimiter.RecordPush()` → throws `Http3ConnectionException(ExcessiveLoad)`
- [ ] In `HandleServerFrame()`: if `Http3CancelPushFrame` received, call `_cancelPushHandler.HandleReceivedCancelPush(frame)`
- [ ] GOAWAY-state prevents new MAX_PUSH_ID frames
- [ ] Existing unit tests still pass
- [ ] Build succeeds with zero errors

---

### TASK-001-007: Evaluate and Remove Http3SettingsExchange
**Description:** As a developer, I want to remove `Http3SettingsExchange` if it's redundant to `Http3ControlStream` so that there's no dead/duplicate code.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-001-004, TASK-001-006 (need to see final ConnectionStage state)
**Successors:** TASK-001-008
**Parallel:** no — depends on ConnectionStage integration being done first

**Acceptance Criteria:**
- [ ] Analysis: compare `Http3SettingsExchange` methods vs `Http3ControlStream` — document which methods are unique
- [ ] If `ValidateFieldSectionSize()` is useful: move it to `Http3ControlStream` or a static helper, then remove `Http3SettingsExchange`
- [ ] If `RejectForbiddenH2Settings()` is useful: move it to `Http3Settings` or `Http3ControlStream`, then remove `Http3SettingsExchange`
- [ ] If unique methods exist that aren't covered elsewhere: integrate them into `Http30ConnectionStage` before removing
- [ ] Remove `Http3SettingsExchange.cs` from project
- [ ] Remove or migrate corresponding tests from `22_SettingsExchangeTests.cs`
- [ ] Build succeeds with zero errors
- [ ] All tests pass

---

### TASK-001-008: StreamTests for All Integrated Handlers
**Description:** As a developer, I want StreamTests verifying each handler integration in the Akka pipeline so that regressions are caught at the stage level.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-001-001, TASK-001-002, TASK-001-003, TASK-001-004, TASK-001-005, TASK-001-006, TASK-001-007
**Successors:** TASK-001-009
**Parallel:** no — needs all integrations complete

**Acceptance Criteria:**
- [ ] `TurboHttp.StreamTests/RFC9114/07_Http30FieldValidationStageTests.cs` — test that uppercase headers cause stage failure
- [ ] `TurboHttp.StreamTests/RFC9114/08_Http30OriginValidationStageTests.cs` — test that userinfo/empty-scheme URIs cause stage failure
- [ ] `TurboHttp.StreamTests/RFC9114/09_Http30CertificateValidationTests.cs` — test cert hostname mismatch causes connection failure
- [ ] `TurboHttp.StreamTests/RFC9114/10_Http30IdleTimeoutStageTests.cs` — test that idle timeout triggers GOAWAY + stage completion
- [ ] `TurboHttp.StreamTests/RFC9114/11_Http30ConnectionReuseTests.cs` — test version-dispatch routes HTTP/3 to correct evaluator
- [ ] `TurboHttp.StreamTests/RFC9114/12_Http30PushRejectionStageTests.cs` — test that PUSH_PROMISE with MAX_PUSH_ID=0 causes ExcessiveLoad error
- [ ] All tests follow `StreamTestBase` conventions (extend TestKit, use IMaterializer)
- [ ] RFC-tagged `DisplayName` attributes (e.g. `"RFC-9114-4.2-001: reject uppercase field names in stream stage"`)
- [ ] All existing tests still pass
- [ ] Build succeeds with zero errors

---

### TASK-001-009: Validation Gate — Full Build + Test + Port Naming
**Description:** As a developer, I want a final validation confirming zero regressions, correct port naming, and complete handler integration.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-001-008
**Successors:** none
**Parallel:** no — final gate

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings (except expected obsolete)
- [ ] `dotnet test src/TurboHttp.sln` — all tests pass
- [ ] Stage port naming validation passes (run `stage-port-validator` agent)
- [ ] Grep confirms zero remaining references to `Http3SettingsExchange` in production code
- [ ] Grep confirms all 10 handlers are referenced in production code (not just tests)
- [ ] No new `[Obsolete]` markers introduced

## Task Dependency Graph

```
TASK-001-001 (FieldValidator) ──────────────────────┐
TASK-001-002 (OriginValidator) ─────────────────────┤
TASK-001-003 (CertificateValidator) ────────────────┤
TASK-001-004 (IdleTimeout) ──────────┬──────────────┤
TASK-001-005 (ConnectionReuse) ──────┤──────────────┤
TASK-001-006 (PushRejection) ────────┤──────────────┤
                                     ↓              ↓
                              TASK-001-007 → TASK-001-008 → TASK-001-009
                             (SettingsExch)  (StreamTests)   (Validation)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~35k | none | yes (with 002–006) | — |
| TASK-001-002 | ~30k | none | yes (with 001, 003–006) | — |
| TASK-001-003 | ~40k | none | yes (with 001–002, 004–006) | opus |
| TASK-001-004 | ~45k | none | yes (with 001–003, 005–006) | — |
| TASK-001-005 | ~40k | none | yes (with 001–004, 006) | — |
| TASK-001-006 | ~50k | none | yes (with 001–005) | opus |
| TASK-001-007 | ~25k | 004, 006 | no | — |
| TASK-001-008 | ~80k | 001–007 | no | — |
| TASK-001-009 | ~15k | 008 | no | — |

**Total estimated tokens:** ~360k

## Functional Requirements

- FR-1: `Http30StreamStage` must reject response headers with uppercase field names, connection-specific headers, or invalid characters (RFC 9114 §4.2)
- FR-2: `Http30Request2FrameStage` must reject request URIs containing userinfo, empty scheme, empty path, or fragments (RFC 9114 §10.3)
- FR-3: `QuicClientProvider` must validate server certificate hostname coverage via SAN/CN after QUIC handshake (RFC 9114 §3.3)
- FR-4: `Http30ConnectionStage` must track idle timeout and send GOAWAY when expired with zero active streams (RFC 9114 §5.1)
- FR-5: `ConnectionReuseStage` must dispatch to `Http3ConnectionReuseEvaluator` when `response.Version.Major >= 3`
- FR-6: `Http30ConnectionStage` must send `MAX_PUSH_ID=0` on control stream at startup, rejecting all server pushes (RFC 9114 §10.5)
- FR-7: `Http30ConnectionStage` must respond to `PUSH_PROMISE` with `Http3ConnectionException(ExcessiveLoad)` when push limit is zero
- FR-8: `Http3SettingsExchange` must be removed if redundant; unique utility methods migrated to surviving classes
- FR-9: Every integration point must have a corresponding StreamTest in `TurboHttp.StreamTests/RFC9114/`

## Non-Goals

- No full server-push pipeline — pushes are defensively rejected, not processed
- No `Http3PushPromiseValidator` integration for push content validation (no pushes accepted)
- No new `QuicOptions` configuration surface — use existing `IdleTimeout`, `MaxBidirectionalStreams`
- No changes to HTTP/1.x or HTTP/2 pipeline behavior
- No integration tests (Kestrel HTTP/3 fixture not yet available)

## Technical Considerations

- **Certificate access**: `QuicConnection.RemoteCertificate` may return `X509Certificate` (not `X509Certificate2`) — verify API and cast/convert if needed
- **Timer in GraphStage**: `Http3IdleTimeoutHandler` timeout check requires `ScheduleOnce`/`ScheduleRepeatedly` in `GraphStageLogic` — Akka pattern for periodic callbacks
- **ConnectionReuseStage threading**: `serverCertificate` and `isGoingAway` may not be available in the response context — may need to add properties to `HttpResponseMessage.Options` or a custom header for metadata passing
- **Http3SettingsExchange removal**: If `ValidateFieldSectionSize()` is needed, consider moving to `Http3ControlStream` as a method or to `Http3FieldValidator` as a static helper
- **Port naming**: New stages must follow CLAUDE.md naming convention (`StageName.In` / `StageName.Out`)

## Success Metrics

- Zero dead HTTP/3 protocol handler code in production (all 10 handlers referenced)
- All RFC 9114 security validations enforced in the pipeline
- 6+ new StreamTest files with RFC-tagged assertions
- Full build + test suite green
- `Http3SettingsExchange` removed (if redundant confirmed)

## Open Questions

_None — all resolved via user answers (1D, 2B, 3B, 4A, 5A)._
