# HTTP/2 Test Suite Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure H2/H10/H11 test folders with clear placement rules, move misplaced files, fix ~1700 missing RFC traits, and document the rules in CLAUDE.md.

**Architecture:** File moves via `git mv` + namespace updates. RFC trait additions use a mapping table (component → RFC section). Http10/Http11 flatten sub-subfolders into Client/ and Server/. CLAUDE.md gets placement rules.

**Tech Stack:** git, PowerShell, xUnit `[Trait]` attributes

---

## File Map

### H2 File Moves (Frames/ → Client/)

| File | From | To | Namespace change |
|------|------|----|-----------------|
| EncoderBaselineSpec.cs | `Frames/` | `Client/` | `...Frames` → `...Client` |
| EncoderPseudoHeaderSpec.cs | `Frames/` | `Client/` | `...Frames` → `...Client` |
| EncoderRfcTaggedSpec.cs | `Frames/` | `Client/` | `...Frames` → `...Client` |
| RequestEncoderFrameSpec.cs | `Frames/` | `Client/` | `...Frames` → `...Client` |
| HeadersValidationPart1Spec.cs | `Frames/` | `Client/Decoder/` | `...Frames` → `...Client.Decoder` |
| HeadersValidationPart2Spec.cs | `Frames/` | `Client/Decoder/` | `...Frames` → `...Client.Decoder` |

### Http10 Flattening

| File | From | To | Namespace change |
|------|------|----|-----------------|
| Http10ClientDecoderSpec.cs | `Client/Decoder/` | `Client/` | `...Client.Decoder` → `...Client` |
| Http10ClientDecoderOptionsSpec.cs | `Client/Decoder/` | `Client/` | `...Client.Decoder` → `...Client` |
| Http10ClientEncoderSpec.cs | `Client/Encoder/` | `Client/` | `...Client.Encoder` → `...Client` |
| Http10ClientEncoderOptionsSpec.cs | `Client/Encoder/` | `Client/` | `...Client.Encoder` → `...Client` |
| Http10ClientStateMachineSpec.cs | `Client/StateMachine/` | `Client/` | `...Client.StateMachine` → `...Client` |
| Http10ServerDecoderSpec.cs | `Server/Decoder/` | `Server/` | `...Server.Decoder` → `...Server` |
| Http10ServerDecoderOptionsSpec.cs | `Server/Decoder/` | `Server/` | `...Server.Decoder` → `...Server` |
| Http10ServerEncoderSpec.cs | `Server/Encoder/` | `Server/` | `...Server.Encoder` → `...Server` |
| Http10ServerEncoderOptionsSpec.cs | `Server/Encoder/` | `Server/` | `...Server.Encoder` → `...Server` |
| Http10ServerStateMachineSpec.cs | `Server/StateMachine/` | `Server/` | `...Server.StateMachine` → `...Server` |

### Http11 Flattening

| File | From | To | Namespace change |
|------|------|----|-----------------|
| Http11IncompleteMessageSpec.cs | `Client/Decoder/` | `Client/` | `...Client.Decoder` → `...Client` |
| Http11ClientDecoderSpec.cs | `Client/Decoder/` | `Client/` | `...Client.Decoder` → `...Client` |
| Http11ClientEncoderSpec.cs | `Client/Encoder/` | `Client/` | `...Client.Encoder` → `...Client` |
| Http11StateMachineSpec.cs | `Client/StateMachine/` | `Client/` | `...Client.StateMachine` → `...Client` |
| Http11StateMachineReconnectSpec.cs | `Client/StateMachine/` | `Client/` | `...Client.StateMachine` → `...Client` |
| Http11StateMachineDisconnectSpec.cs | `Client/StateMachine/` | `Client/` | `...Client.StateMachine` → `...Client` |
| Http11ServerDecoderSpec.cs | `Server/Decoder/` | `Server/` | `...Server.Decoder` → `...Server` |
| RequestValidatorSpec.cs | `Server/Decoder/` | `Server/` | `...Server.Decoder` → `...Server` |
| Http11ServerEncoderSpec.cs | `Server/Encoder/` | `Server/` | `...Server.Encoder` → `...Server` |
| ServerStateMachineSpec.cs | `Server/StateMachine/` | `Server/` | `...Server.StateMachine` → `...Server` |
| Http11ServerPipeliningSpec.cs | `Server/Pipelining/` | `Server/` | `...Server.Pipelining` → `...Server` |
| Http11ServerPipeliningLimitSpec.cs | `Server/Pipelining/` | `Server/` | `...Server.Pipelining` → `...Server` |
| Http11ServerConnectionPersistenceSpec.cs | `Server/Persistence/` | `Server/` | `...Server.Persistence` → `...Server` |

### RFC Trait Mapping Table

Used by Tasks 4–10. For each test method missing `[Trait("RFC", "...")]`, add the trait matching the component under test:

| Component / Topic | Trait Value |
|-------------------|-------------|
| Connection preface, PrefaceBuilder | `RFC9113-3.4` |
| Frame format, parsing, FrameDecoder | `RFC9113-4` |
| Stream states, lifecycle | `RFC9113-5.1` |
| Stream IDs, allocation | `RFC9113-5.1.1` |
| Concurrent stream limits | `RFC9113-5.1.2` |
| Flow control windows | `RFC9113-5.2` |
| Error handling (connection/stream errors) | `RFC9113-5.4` |
| DATA frame | `RFC9113-6.1` |
| HEADERS frame | `RFC9113-6.2` |
| PRIORITY frame | `RFC9113-6.3` |
| RST_STREAM frame | `RFC9113-6.4` |
| SETTINGS frame | `RFC9113-6.5` |
| PUSH_PROMISE frame | `RFC9113-6.6` |
| PING frame | `RFC9113-6.7` |
| GOAWAY frame | `RFC9113-6.8` |
| WINDOW_UPDATE frame | `RFC9113-6.9` |
| CONTINUATION frame | `RFC9113-6.10` |
| Error codes | `RFC9113-7` |
| HTTP message framing | `RFC9113-8.1` |
| HTTP fields, header validation | `RFC9113-8.2` |
| Cookie fields | `RFC9113-8.2.3` |
| Pseudo-headers, control data | `RFC9113-8.3` |
| Server push | `RFC9113-8.4` |
| CONNECT method | `RFC9113-8.5` |
| Request reliability, REFUSED_STREAM | `RFC9113-8.7` |
| Security, resource exhaustion | `RFC9113-10` |
| HPACK dynamic table | `RFC7541-4` |
| HPACK integer/string encoding | `RFC7541-5` |
| HPACK binary format, indexing | `RFC7541-6` |
| HPACK security | `RFC7541-7` |
| HPACK static table | `RFC7541-A` |
| HPACK Huffman | `RFC7541-B` |
| HPACK examples (Appendix C) | `RFC7541-C` |

**Rule**: If a test already has a `[Trait("RFC", "...")]`, do NOT change it. Only add traits to methods that have none. Use the most specific section that applies (e.g., a test about GOAWAY error codes gets `RFC9113-6.8`, not `RFC9113-7`).

---

## Task 1: Move H2 Encoder Files from Frames/ to Client/

**Files:**
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderBaselineSpec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderBaselineSpec.cs`
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderPseudoHeaderSpec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderPseudoHeaderSpec.cs`
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderRfcTaggedSpec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderRfcTaggedSpec.cs`
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/RequestEncoderFrameSpec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/RequestEncoderFrameSpec.cs`
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/HeadersValidationPart1Spec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/Decoder/HeadersValidationPart1Spec.cs`
- Move: `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/HeadersValidationPart2Spec.cs` → `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/Decoder/HeadersValidationPart2Spec.cs`

- [ ] **Step 1: Move the 4 encoder files to Client/**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderBaselineSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderBaselineSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderPseudoHeaderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderPseudoHeaderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/EncoderRfcTaggedSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/EncoderRfcTaggedSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/RequestEncoderFrameSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/RequestEncoderFrameSpec.cs
```

- [ ] **Step 2: Move the 2 header validation files to Client/Decoder/**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/HeadersValidationPart1Spec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/Decoder/HeadersValidationPart1Spec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/HeadersValidationPart2Spec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/Decoder/HeadersValidationPart2Spec.cs
```

- [ ] **Step 3: Update namespaces in all 6 moved files**

In each of the 4 encoder files, replace:
```
namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;
```
with:
```
namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client;
```

In each of the 2 header validation files, replace:
```
namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;
```
with:
```
namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;
```

- [ ] **Step 4: Build to verify no compile errors**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run moved tests to verify they still pass**

```powershell
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Http2EncoderBaselineSpec" -class "TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Http2EncoderPseudoHeaderSpec" -class "TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder.Http2ResponseHeaderValidationSpec" -class "TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder.Http2ForbiddenHeaderValidationSpec"
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor(tests): move H2 encoder/validation specs from Frames/ to Client/ (placement rules)"
```

---

## Task 2: Flatten Http10 Test Structure

**Files:** 10 files across `Client/Decoder/`, `Client/Encoder/`, `Client/StateMachine/`, `Server/Decoder/`, `Server/Encoder/`, `Server/StateMachine/` → all flatten to `Client/` and `Server/`.

- [ ] **Step 1: Move all Http10 Client/ subfolders up**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Decoder/Http10ClientDecoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Http10ClientDecoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Decoder/Http10ClientDecoderOptionsSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Http10ClientDecoderOptionsSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Encoder/Http10ClientEncoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Http10ClientEncoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Encoder/Http10ClientEncoderOptionsSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Http10ClientEncoderOptionsSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/StateMachine/Http10ClientStateMachineSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Http10ClientStateMachineSpec.cs
```

- [ ] **Step 2: Move all Http10 Server/ subfolders up**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Decoder/Http10ServerDecoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerDecoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Decoder/Http10ServerDecoderOptionsSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerDecoderOptionsSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Encoder/Http10ServerEncoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerEncoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Encoder/Http10ServerEncoderOptionsSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerEncoderOptionsSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/StateMachine/Http10ServerStateMachineSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Http10ServerStateMachineSpec.cs
```

- [ ] **Step 3: Update namespaces in all 10 files**

In each Client/ file, find the namespace line and replace the subfolder segment:
- `...Http10.Client.Decoder;` → `...Http10.Client;`
- `...Http10.Client.Encoder;` → `...Http10.Client;`
- `...Http10.Client.StateMachine;` → `...Http10.Client;`

In each Server/ file:
- `...Http10.Server.Decoder;` → `...Http10.Server;`
- `...Http10.Server.Encoder;` → `...Http10.Server;`
- `...Http10.Server.StateMachine;` → `...Http10.Server;`

- [ ] **Step 4: Remove empty subdirectories**

```powershell
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Decoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/Encoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Client/StateMachine -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Decoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/Encoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http10/Server/StateMachine -Confirm:$false
```

- [ ] **Step 5: Build and run Http10 tests**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Protocol.Syntax.Http10"
```

Expected: Build succeeds, all Http10 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor(tests): flatten Http10 Client/ and Server/ subfolders"
```

---

## Task 3: Flatten Http11 Test Structure

**Files:** 13 files across Client/ and Server/ subfolders → flatten to `Client/` and `Server/`.

- [ ] **Step 1: Move Http11 Client/ subfolders up**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Decoder/Http11IncompleteMessageSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11IncompleteMessageSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Decoder/Http11ClientDecoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11ClientDecoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Encoder/Http11ClientEncoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11ClientEncoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/StateMachine/Http11StateMachineSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11StateMachineSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/StateMachine/Http11StateMachineReconnectSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11StateMachineReconnectSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/StateMachine/Http11StateMachineDisconnectSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Http11StateMachineDisconnectSpec.cs
```

- [ ] **Step 2: Move Http11 Server/ subfolders up**

```powershell
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Decoder/Http11ServerDecoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerDecoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Decoder/RequestValidatorSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/RequestValidatorSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Encoder/Http11ServerEncoderSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerEncoderSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/StateMachine/ServerStateMachineSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/ServerStateMachineSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Pipelining/Http11ServerPipeliningSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerPipeliningSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Pipelining/Http11ServerPipeliningLimitSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerPipeliningLimitSpec.cs
git mv src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Persistence/Http11ServerConnectionPersistenceSpec.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11ServerConnectionPersistenceSpec.cs
```

- [ ] **Step 3: Update namespaces in all 13 files**

In each Client/ file, replace:
- `...Http11.Client.Decoder;` → `...Http11.Client;`
- `...Http11.Client.Encoder;` → `...Http11.Client;`
- `...Http11.Client.StateMachine;` → `...Http11.Client;`

In each Server/ file, replace:
- `...Http11.Server.Decoder;` → `...Http11.Server;`
- `...Http11.Server.Encoder;` → `...Http11.Server;`
- `...Http11.Server.StateMachine;` → `...Http11.Server;`
- `...Http11.Server.Pipelining;` → `...Http11.Server;`
- `...Http11.Server.Persistence;` → `...Http11.Server;`

- [ ] **Step 4: Remove empty subdirectories**

```powershell
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Decoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/Encoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Client/StateMachine -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Decoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Encoder -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/StateMachine -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Pipelining -Confirm:$false
Remove-Item -Recurse -Force src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Persistence -Confirm:$false
```

- [ ] **Step 5: Build and run Http11 tests**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Protocol.Syntax.Http11"
```

Expected: Build succeeds, all Http11 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor(tests): flatten Http11 Client/ and Server/ subfolders"
```

---

## Task 4: Add Missing RFC Traits — H2 Frames/

**Files:** All `*Spec.cs` in `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/`

After Task 1's moves, Frames/ contains ~14 files focused on FrameDecoder/frame serialization.

- [ ] **Step 1: Read each file in Frames/ and identify test methods missing `[Trait("RFC", "...")]`**

For each file, read it, find every `[Fact(` or `[Theory(` method. If there is no `[Trait("RFC",` attribute on that method, add one using the mapping table:

- `DecoderBasicFrameSpec.cs` — tests DATA/HEADERS/SETTINGS/RST_STREAM/GOAWAY/PING/WINDOW_UPDATE decoding → use the specific frame section (e.g., SETTINGS tests → `RFC9113-6.5`, PING tests → `RFC9113-6.7`)
- `DecoderPaddingSpec.cs` — padding validation for DATA/HEADERS → `RFC9113-6.1` or `RFC9113-6.2`
- `DecoderStreamValidationSpec.cs` — header block assembly → `RFC9113-6.2`
- `DecoderPushPromiseSpec.cs` — PUSH_PROMISE → `RFC9113-6.6`
- `DecoderUnknownErrorCodeSpec.cs` — error codes → `RFC9113-7`
- `DecoderErrorCodeSpec.cs` — error codes → `RFC9113-7`
- `ErrorHandlingSpec.cs` — error frames → `RFC9113-5.4`
- `FrameParsingPart1Spec.cs` — frame format → `RFC9113-4`
- `FrameParsingPart2Spec.cs` — frame format → `RFC9113-4`
- `Http2FrameSpec.cs` — frame serialization → `RFC9113-4`
- `PrefaceBuilderSpec.cs` — preface → `RFC9113-3.4`
- `StreamStateMachineSpec.cs` — stream states → `RFC9113-5.1`
- `ContinuationFramePart1Spec.cs` — CONTINUATION → `RFC9113-6.10`
- `ContinuationFramePart2Spec.cs` — CONTINUATION → `RFC9113-6.10`
- `EncoderStreamSettingsSpec.cs` — SETTINGS encoding → `RFC9113-6.5`

**Pattern to follow**: Add `[Trait("RFC", "RFC9113-X.Y")]` on the line before or after the existing `[Fact(Timeout = 5000)]` attribute. Do NOT modify test method names or logic.

- [ ] **Step 2: Build and run Frames/ tests**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
```

Expected: Build succeeds. Traits are metadata-only — no logic changes.

- [ ] **Step 3: Commit**

```powershell
git add src/TurboHTTP.Tests/Protocol/Syntax/Http2/Frames/
git commit -m "refactor(tests): add missing RFC traits to H2 Frames/ specs"
```

---

## Task 5: Add Missing RFC Traits — H2 Client/

**Files:** All `*Spec.cs` in `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/` and its subfolders.

- [ ] **Step 1: Read each file and add missing RFC traits**

Use the mapping table. Key assignments for Client/ files:

**Client/ (top-level, after Task 1 moves):**
- `EncoderBaselineSpec.cs` → `RFC9113-8.3` (pseudo-header encoding)
- `EncoderPseudoHeaderSpec.cs` → `RFC9113-8.3`
- `EncoderRfcTaggedSpec.cs` → `RFC9113-5.1` (end-stream flags)
- `RequestEncoderFrameSpec.cs` → `RFC9113-8.3`

**Client/Decoder/:**
- `Http2ResponseDecoderSpec.cs` → `RFC9113-8.3` (response pseudo-headers, status)
- `Http2StreamStateSpec.cs` → `RFC9113-5.1`
- `CookieHeaderSpec.cs` → `RFC9113-8.2.3`
- `ConnectTunnelSpec.cs` → `RFC9113-8.5`
- `ResponseRetentionSpec.cs` → `RFC9113-8.1`
- `HeadersValidationPart1Spec.cs` → `RFC9113-8.2`
- `HeadersValidationPart2Spec.cs` → `RFC9113-8.2`

**Client/FlowControl/:**
- `FlowControlSpec.cs` → `RFC9113-6.9`
- `DecoderStreamFlowControlSpec.cs` → `RFC9113-6.9`
- `HighConcurrencyPart1Spec.cs` → `RFC9113-5.1.2`
- `HighConcurrencyPart2Spec.cs` → `RFC9113-5.1.2`
- `ResourceExhaustionPart1Spec.cs` → `RFC9113-10`
- `ResourceExhaustionPart2Spec.cs` → `RFC9113-10`
- `WindowUpdateSettingsSpec.cs` → `RFC9113-6.9`

**Client/Settings/:**
- `SettingsSpec.cs` → `RFC9113-6.5`
- `SettingsLifecycleSpec.cs` → `RFC9113-6.5`
- `SettingsMaxConcurrentApiSpec.cs` → `RFC9113-6.5`
- `SettingsMaxConcurrentIntPart1Spec.cs` → `RFC9113-6.5`
- `SettingsMaxConcurrentIntPart2Spec.cs` → `RFC9113-6.5`

**Client/StateMachine/:**
- `Http2StateMachineSpec.cs` → varies per test (preface → `RFC9113-3.4`, request → `RFC9113-8.3`, settings → `RFC9113-6.5`, goaway → `RFC9113-6.8`)
- `Http2StateMachineKeepAliveSpec.cs` → `RFC9113-6.7`
- `Http2StateMachineReconnectSpec.cs` → `RFC9113-6.8`
- `GoAwaySpec.cs` → `RFC9113-6.8`
- `GoAwayComplianceSpec.cs` → `RFC9113-6.8`
- `RstStreamRestrictionSpec.cs` → `RFC9113-6.4`
- `CrossComponentValidationPart1Spec.cs` → `RFC9113-4`
- `CrossComponentValidationPart2Spec.cs` → `RFC9113-4`

**Rule**: Only add traits to methods that don't already have one. For `Http2StateMachineSpec.cs` which tests multiple RFC sections, read each test method name to determine the correct section.

- [ ] **Step 2: Build**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
```

- [ ] **Step 3: Commit**

```powershell
git add src/TurboHTTP.Tests/Protocol/Syntax/Http2/Client/
git commit -m "refactor(tests): add missing RFC traits to H2 Client/ specs"
```

---

## Task 6: Add Missing RFC Traits — H2 Server/

**Files:** All `*Spec.cs` in `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/` and subfolders.

- [ ] **Step 1: Read each file and add missing RFC traits**

**Server/Decoder/:**
- `Http2ServerRequestDecoderSpec.cs` → `RFC9113-8.3`
- `Http2ServerPseudoHeaderSpec.cs` → `RFC9113-8.3`
- `Http2ServerFieldValidationSpec.cs` → `RFC9113-8.2`
- `Http2ServerConnectSpec.cs` → `RFC9113-8.5`

**Server/Encoder/:**
- `Http2ServerResponseEncoderSpec.cs` → `RFC9113-8.3`
- `Http2ServerResponseFrameSpec.cs` → `RFC9113-6.2`
- `Http2ServerResponseBufferSpec.cs` → `RFC9113-6.1`

**Server/StateMachine/:**
- `Http2ServerStateMachineSpec.cs` → `RFC9113-5.1`
- `Http2ServerSettingsSpec.cs` → `RFC9113-6.5`
- `Http2ServerStreamCorrelationSpec.cs` → `RFC9113-5.1`

**Server/Streaming/:**
- `Http2ServerBodyStreamingSpec.cs` → `RFC9113-6.1`
- `Http2ServerFlowControlSpec.cs` → `RFC9113-6.9`
- `Http2ServerTimeoutSpec.cs` → `RFC9113-5.4`

- [ ] **Step 2: Build and commit**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
git add src/TurboHTTP.Tests/Protocol/Syntax/Http2/Server/
git commit -m "refactor(tests): add missing RFC traits to H2 Server/ specs"
```

---

## Task 7: Add Missing RFC Traits — H2 Hpack/

**Files:** All `*Spec.cs` in `src/TurboHTTP.Tests/Protocol/Syntax/Http2/Hpack/`

- [ ] **Step 1: Read each file and add missing RFC traits**

- `DynamicTableSpec.cs` → `RFC7541-4`
- `DynamicTableSyncSpec.cs` → `RFC7541-4`
- `HpackEncodingSpec.cs` → `RFC7541-6`
- `HpackDecoderEdgeCasesSpec.cs` → `RFC7541-6`
- `HpackEncoderEdgeCasesSpec.cs` → `RFC7541-6`
- `HpackHeaderBlockPrimitiveSpec.cs` → `RFC7541-5`
- `HpackHeaderBlockDecodingSpec.cs` → `RFC7541-6`
- `HpackHeaderListSizeSpec.cs` → `RFC7541-4`
- `HpackSensitiveHeaderSpec.cs` → `RFC7541-7`
- `HpackSensitiveHeaderVerificationSpec.cs` → `RFC7541-7`
- `HpackTableRepresentationSpec.cs` → `RFC7541-6`
- `HpackAppendixCSpec.cs` → `RFC7541-C`
- `StaticTableSpec.cs` → `RFC7541-A`
- `HuffmanSpec.cs` → `RFC7541-B`

- [ ] **Step 2: Build and commit**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
git add src/TurboHTTP.Tests/Protocol/Syntax/Http2/Hpack/
git commit -m "refactor(tests): add missing RFC traits to H2 Hpack/ specs"
```

---

## Task 8: Add Missing RFC Traits — H2 Security/, Stages/, Options/

**Files:** All remaining H2 spec files.

- [ ] **Step 1: Add traits to Security/ files**

- `HpackBombSpec.cs` → `RFC9113-10`
- `HpackFuzzSpec.cs` → `RFC7541-7`
- `Http2FrameFuzzSpec.cs` → `RFC9113-10`
- `SecuritySpec.cs` → `RFC9113-10`
- `FuzzHarnessPart1Spec.cs` → `RFC9113-10`
- `FuzzHarnessPart2Spec.cs` → `RFC9113-10`

- [ ] **Step 2: Add traits to Stages/ files**

- `Http20ConnectionStageSpec.cs` → `RFC9113-3.4`
- `Http20ConnectionStageReconnectSpec.cs` → `RFC9113-6.8`
- `Http2ConnectionFlowControlSpec.cs` → `RFC9113-6.9`
- `Http2ConnectionFlowControlBatchingSpec.cs` → `RFC9113-6.9`
- `Http2ConnectionBackpressureSpec.cs` → `RFC9113-5.1.2`
- `Http2ConnectionGoAwaySpec.cs` → `RFC9113-6.8`
- `Http2ConnectionPingSpec.cs` → `RFC9113-6.7`
- `Http2ConnectionStreamAcquireSpec.cs` → `RFC9113-8.1`

- [ ] **Step 3: Add traits to Options/ files**

- `Http2ClientDecoderOptionsSpec.cs` → `RFC9113-6.5`
- `Http2ClientEncoderOptionsSpec.cs` → `RFC9113-6.5`
- `Http2ServerDecoderOptionsSpec.cs` → `RFC9113-6.5`
- `Http2ServerEncoderOptionsSpec.cs` → `RFC9113-6.5`

- [ ] **Step 4: Build and commit**

```powershell
dotnet build src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
git add src/TurboHTTP.Tests/Protocol/Syntax/Http2/Security/ src/TurboHTTP.Tests/Protocol/Syntax/Http2/Stages/ src/TurboHTTP.Tests/Protocol/Syntax/Http2/Options/
git commit -m "refactor(tests): add missing RFC traits to H2 Security/, Stages/, Options/ specs"
```

---

## Task 9: Update CLAUDE.md with Folder Placement Rules

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Replace the Test Conventions section**

In the root `CLAUDE.md`, find the section `## Test Conventions (Quick Reference)` and replace it with the expanded version that includes the folder placement rules:

```markdown
## Test Conventions (Quick Reference)

New tests use **component-based folders** (`Http10/`, `Http11/`, `Http2/`, etc.) not RFC folders. Key rules:

- `Spec` suffix, `sealed` class, BDD method names: `Subject_should_behavior()`
- `[Trait("RFC", "RFC9113-4.1")]` for traceability, `[Fact(Timeout = 5000)]` required
- `[Fact(DisplayName = ...)]` is deprecated — method name IS the documentation
- Max 500 lines per test class

### H2 Folder Placement Rules

| Folder | What belongs here | Types under test |
|--------|------------------|-----------------|
| **Frames/** | Wire format: serialize, deserialize, parse, validate | `FrameDecoder`, `Http2Frame` subtypes, `PrefaceBuilder` |
| **Client/** | Client behavioral logic (subfolders at 5+ files) | `Http2ClientStateMachine`, `FlowController`, `StreamTracker`, `Http2ClientEncoder`, `Http2ClientDecoder` |
| **Server/** | Server behavioral logic (subfolders at 5+ files) | `Http2ServerStateMachine`, `Http2ServerEncoder`, `Http2ServerDecoder` |
| **Hpack/** | RFC 7541 compression | `HpackEncoder`, `HpackDecoder`, `DynamicTable`, `StaticTable` |
| **Security/** | Fuzz, adversarial, resource exhaustion | Any, from attacker perspective |
| **Stages/** | Akka Streams integration (GraphDsl) | `Http20ConnectionStage` |
| **Options/** | Configuration validation stubs | `*Options` types |

**Decision rule**: `FrameDecoder` + frame assertions → Frames/. `FlowController`/`StateMachine`/`Encoder`/`Decoder` → Client/ or Server/. Akka Streams graph → Stages/.

### Http10/Http11 Structure

Flat `Client/` and `Server/` (no subfolders — file count doesn't justify them). `Stages/`, `Security/`, `RoundTrip/` stay as top-level folders.
```

- [ ] **Step 2: Commit**

```powershell
git add CLAUDE.md
git commit -m "docs: add H2 test folder placement rules to CLAUDE.md"
```

---

## Task 10: Full Verification

- [ ] **Step 1: Run full test suite**

```powershell
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
```

Expected: ~4978 tests, 0 errors, 0 failures.

- [ ] **Step 2: Build Release**

```powershell
dotnet build --configuration Release src/TurboHTTP.slnx
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Verify no empty directories remain**

```powershell
Get-ChildItem -Path src/TurboHTTP.Tests/Protocol/Syntax -Directory -Recurse | Where-Object { (Get-ChildItem $_.FullName -File).Count -eq 0 -and (Get-ChildItem $_.FullName -Directory).Count -eq 0 } | ForEach-Object { $_.FullName }
```

Expected: No output (no empty directories).

- [ ] **Step 4: Verify RFC trait coverage**

```powershell
$files = Get-ChildItem -Path src/TurboHTTP.Tests/Protocol/Syntax/Http2 -Recurse -Filter "*Spec.cs"
foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw
    $facts = [regex]::Matches($content, '\[Fact\(')
    $theories = [regex]::Matches($content, '\[Theory\(')
    $traits = [regex]::Matches($content, '\[Trait\("RFC"')
    $testCount = $facts.Count + $theories.Count
    if ($traits.Count -lt $testCount) {
        Write-Output "$($f.Name): $($traits.Count)/$testCount traits"
    }
}
```

Expected: No output (all test methods have RFC traits).
