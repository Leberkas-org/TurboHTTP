# Plan Progress — Test Audit & Restructuring

## TASK-ANA-001: Create the Audit Report
**Status:** COMPLETE | **Date:** 2026-03-12

**Deliverable:** `docs/test-audit-report.md`

**Findings Summary:**
- 21 test files (521 tests) in RFC9113/ use `Http2ProtocolSession`
- `Http2ProtocolSession` covers RFC 9113 §3.4, §4.3, §5.1, §6.2, §6.5, §6.7, §6.8, §6.9, §6.10, §8.2, §8.3, §8.3.2 + security protections
- 819 tests across all 3 projects are missing RFC references in DisplayName:
  - TurboHttp.Tests: 182 bare + 262 without RFC = 444
  - TurboHttp.StreamTests: 175 without RFC
  - TurboHttp.IntegrationTests: 200 without RFC
- Integration folder mapping: 10 files -> RFC6265/ (1), RFC9110/ (6), RFC9112/ (3)
- StreamTests folder mapping: Http10/ -> RFC1945/, Http11/ -> RFC9112/, Http20/ -> RFC9113/
- Build: 0 errors (1 pre-existing CS0169 warning)

---

## TASK-PSS-001: Replace Http2ProtocolSession — Stream State Tests
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/03_StreamStateMachineTests.cs`
- Removed `Http2ProtocolSession` and `Http2StreamLifecycleState` (test-only classes)
- Now uses only `Http2FrameDecoder`, `Http2Frame` subclasses, `HpackDecoder`, `HpackEncoder`
- Class renamed from `Http2StreamLifecycleTests` to `Http2StreamStateMachineTests` (matches filter `FullyQualifiedName~StreamStateMachine`)
- 25 old tests → 12 new tests; all DisplayNames contain `RFC-9113-§5.1`
- All 4 required scenarios covered: Idle→Open, Open→Closed, HEADERS on stream 0 → Exception, DATA on idle stream → Exception
- Test count: 2123 (pre-existing RH-015 failure unchanged)

---

## Remaining Tasks

| Task | Status | Description |
|------|--------|-------------|
| TASK-PSS-001 | COMPLETE | Replace Http2ProtocolSession — Stream State Tests (§5.1) |
| TASK-PSS-002 | PENDING | Replace Http2ProtocolSession — Settings Tests (§6.5) |
| TASK-PSS-003 | PENDING | Replace Http2ProtocolSession — Flow Control Tests (§6.9) |
| TASK-PSS-004 | PENDING | Replace Http2ProtocolSession — GoAway/Ping/RST (§6.4/§6.7/§6.8) |
| TASK-PSS-005 | PENDING | Replace Http2ProtocolSession — Header/Pseudo-Header Tests (§8.2/§8.3) |
| TASK-PSS-006 | PENDING | Replace Http2ProtocolSession — Security/Fuzz/Concurrency |
| TASK-PSS-007 | PENDING | Delete Http2ProtocolSession (blocked by PSS-001..006) |
| TASK-DISP-001 | PENDING | Add RFC References to Integration Test DisplayNames |
| TASK-DISP-002 | PENDING | Add RFC References to RFC9113 Tests Without Prefix |
| TASK-DISP-003 | PENDING | Add RFC References to StreamTests DisplayNames |
| TASK-SORT-001 | PENDING | Move Integration Test Files into RFC Folders |
| TASK-SORT-002 | PENDING | Restructure StreamTests into RFC Folders |
| TASK-SORT-003 | PENDING | Clean Up Loose Helper Files |
