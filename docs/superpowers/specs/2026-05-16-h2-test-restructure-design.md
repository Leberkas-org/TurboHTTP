# HTTP/2 Test Suite Restructure — Design Spec

**Date**: 2026-05-16
**Goal**: Clear folder placement rules, move misplaced files, fix missing RFC traits, apply consistent structure across all protocol versions
**Scope**: Http2 test suite (primary), Http10/Http11 (alignment)

---

## 1. Problem

- 15 nested directories under `Http2/` — unclear where to place new tests
- No documented rules for folder placement
- ~1700 test methods missing `[Trait("RFC", "...")]` attributes (required for Protocol/ tests per CLAUDE.md)
- 6 files in `Frames/` that actually test client-side encoder/decoder logic
- Http10/Http11 have unnecessary sub-subfolders for small file counts

---

## 2. Folder Placement Rules

The core distinction is **what the test exercises**:

| Folder | What belongs here | Key types under test |
|--------|------------------|---------------------|
| **Frames/** | Wire format: serialize, deserialize, parse, validate frames | `FrameDecoder`, `Http2Frame` subtypes, `PrefaceBuilder` |
| **Client/** | Client behavioral logic (subfolders allowed at 5+ files) | `Http2ClientStateMachine`, `FlowController`, `StreamTracker`, `Http2ClientEncoder`, `Http2ClientDecoder` |
| **Server/** | Server behavioral logic (subfolders allowed at 5+ files) | `Http2ServerStateMachine`, `Http2ServerEncoder`, `Http2ServerDecoder` |
| **Hpack/** | RFC 7541 compression | `HpackEncoder`, `HpackDecoder`, `DynamicTable`, `StaticTable` |
| **Security/** | Fuzz, adversarial, resource exhaustion | Any, from attacker perspective |
| **Stages/** | Akka Streams integration (GraphDsl, Sources/Sinks) | `Http20ConnectionStage` |
| **Options/** | Configuration validation stubs | `*Options` types |

### Decision Rule

1. Test instantiates `FrameDecoder` and asserts on frame properties → **Frames/**
2. Test uses `FlowController` / `StateMachine` / `SessionManager` / `Http2ClientEncoder` / `Http2ClientDecoder` → **Client/** or **Server/**
3. Test wires an Akka Streams graph → **Stages/**
4. Test exercises HPACK encoding/decoding → **Hpack/**
5. Test is adversarial/fuzzing → **Security/**

### Subfolder Rule

Subfolders within `Client/` and `Server/` are allowed when a group has **5+ files**. Never more than 1 level deep. Current `Client/` subfolders stay:

- `Client/Decoder/` — response decoding, cookies, CONNECT, response retention
- `Client/FlowControl/` — window management, concurrency, resource exhaustion
- `Client/Settings/` — SETTINGS frame lifecycle, max concurrent streams
- `Client/StateMachine/` — session state, GOAWAY, RST_STREAM, reconnection, keep-alive

---

## 3. File Moves

### Frames/ → Client/ (6 files)

These files use `Http2ClientEncoder` or `Http2ClientDecoder`, not just `FrameDecoder`:

| File | Current path | New path | Reason |
|------|-------------|----------|--------|
| EncoderBaselineSpec.cs | `Frames/` | `Client/` | Uses `Http2ClientEncoder` |
| EncoderPseudoHeaderSpec.cs | `Frames/` | `Client/` | Uses `Http2ClientEncoder.ValidatePseudoHeaders()` |
| EncoderRfcTaggedSpec.cs | `Frames/` | `Client/` | Uses `Http2ClientEncoder` |
| RequestEncoderFrameSpec.cs | `Frames/` | `Client/` | Uses `Http2ClientEncoder` |
| HeadersValidationPart1Spec.cs | `Frames/` | `Client/Decoder/` | Uses `Http2ClientDecoder.ValidateResponseHeaders()` |
| HeadersValidationPart2Spec.cs | `Frames/` | `Client/Decoder/` | Uses `Http2ClientDecoder.ValidateResponseHeaders()` |

### Files that stay in Frames/ (confirmed correct)

- StreamStateMachineSpec.cs — uses `FrameDecoder` only
- EncoderStreamSettingsSpec.cs — uses `SettingsFrame` + `FrameDecoder` only
- ErrorHandlingSpec.cs — uses `FrameDecoder` only
- DecoderStreamValidationSpec.cs — uses `FrameDecoder` only
- All Decoder*Spec.cs, FrameParsing*Spec.cs, ContinuationFrame*Spec.cs, PrefaceBuilderSpec.cs, Http2FrameSpec.cs

---

## 4. RFC Trait Fixes

### Rule (already in CLAUDE.md)

Every test method in `Protocol/` folders MUST have `[Trait("RFC", "RFCXXXX-Y.Z")]`.

### Current State

- **0 files** with complete RFC trait coverage
- **33 files** with partial coverage
- **58 files** with zero RFC traits
- **~1700 test methods** missing traits total

### Trait Assignment

Each test method gets the RFC section for the feature it tests:

| Component | RFC Trait |
|-----------|----------|
| Frame format, parsing, size | RFC9113-4.1 |
| Stream states, lifecycle | RFC9113-5.1 |
| Stream IDs | RFC9113-5.1.1 |
| Concurrency limits | RFC9113-5.1.2 |
| Flow control | RFC9113-5.2 / RFC9113-6.9 |
| Error handling | RFC9113-5.4 |
| DATA frame | RFC9113-6.1 |
| HEADERS frame | RFC9113-6.2 |
| PRIORITY frame | RFC9113-6.3 |
| RST_STREAM frame | RFC9113-6.4 |
| SETTINGS frame | RFC9113-6.5 |
| PUSH_PROMISE frame | RFC9113-6.6 |
| PING frame | RFC9113-6.7 |
| GOAWAY frame | RFC9113-6.8 |
| WINDOW_UPDATE frame | RFC9113-6.9 |
| CONTINUATION frame | RFC9113-6.10 |
| Error codes | RFC9113-7 |
| HTTP message framing | RFC9113-8.1 |
| HTTP fields | RFC9113-8.2 |
| Cookie fields | RFC9113-8.2.3 |
| Control data (pseudo-headers) | RFC9113-8.3 |
| Server push | RFC9113-8.4 |
| CONNECT method | RFC9113-8.5 |
| Request reliability | RFC9113-8.7 |
| Connection preface | RFC9113-3.4 |
| Security considerations | RFC9113-10 |
| HPACK encoding/decoding | RFC7541-6 |
| HPACK integer/string | RFC7541-5 |
| HPACK dynamic table | RFC7541-4 |
| HPACK static table | RFC7541-A |
| HPACK Huffman | RFC7541-B |
| HPACK examples | RFC7541-C |
| HPACK security | RFC7541-7 |

### Execution Strategy

Dispatch parallel agents per folder to add missing traits. Each agent:
1. Reads each spec file
2. For each test method without a `[Trait("RFC", "...")]`, adds the correct trait based on the table above
3. Validates that existing traits are correct (don't change them)
4. Commits per folder

---

## 5. Http10/Http11 Alignment

### Http10 (10 specs total)

Current structure has sub-subfolders with 1-2 files each — unnecessary.

| Current | New | Files |
|---------|-----|-------|
| `Client/Decoder/` (2) | `Client/` | Merge up |
| `Client/Encoder/` (2) | `Client/` | Merge up |
| `Client/StateMachine/` (1) | `Client/` | Merge up |
| `Server/Decoder/` (2) | `Server/` | Merge up |
| `Server/Encoder/` (2) | `Server/` | Merge up |
| `Server/StateMachine/` (1) | `Server/` | Merge up |
| `Stages/` (4) | `Stages/` | Keep as-is |

### Http11 (31 specs total)

Same pattern — flatten Client/ and Server/ subfolders that have < 5 files:

| Current | New | Files |
|---------|-----|-------|
| `Client/Decoder/` (2) | `Client/` | Merge up |
| `Client/Encoder/` (1) | `Client/` | Merge up |
| `Client/StateMachine/` (3) | `Client/` | Merge up |
| `Server/Decoder/` (2) | `Server/` | Merge up |
| `Server/Encoder/` (1) | `Server/` | Merge up |
| `Server/Persistence/` (1) | `Server/` | Merge up |
| `Server/Pipelining/` (2) | `Server/` | Merge up |
| `Server/StateMachine/` (1) | `Server/` | Merge up |
| `RoundTrip/` (5) | `RoundTrip/` | Keep |
| `Security/` (5) | `Security/` | Keep |
| `Stages/` (5) | `Stages/` | Keep |

---

## 6. CLAUDE.md Update

Add the folder placement rules (Section 2 above) to the Test Conventions section in CLAUDE.md, replacing the current brief reference.

---

## 7. Result Structure

After restructure:

```
Http2/
├── Frames/        (14 specs — pure codec)
├── Client/        (6 specs — encoder logic, directly in folder)
│   ├── Decoder/   (6 specs — response decoding, cookies, CONNECT)
│   ├── FlowControl/ (7 specs — windows, concurrency, exhaustion)
│   ├── Settings/  (5 specs — SETTINGS lifecycle)
│   └── StateMachine/ (8 specs — session, GOAWAY, RST, reconnect)
├── Server/        (flat, 13 specs)
│   ├── Decoder/   (4 specs)
│   ├── Encoder/   (3 specs)
│   ├── StateMachine/ (3 specs)
│   └── Streaming/ (3 specs)
├── Hpack/         (14 specs)
├── Security/      (6 specs)
├── Stages/        (8 specs)
└── Options/       (4 specs)
```

Http10/ and Http11/ get flat `Client/` and `Server/` (no subfolders).

---

## 8. Out of Scope

- Renaming test files or classes (just moving)
- Changing test logic or assertions
- Adding new tests (already done in the RFC compliance plan)
- Integration test restructure (`TurboHTTP.IntegrationTests/`)
