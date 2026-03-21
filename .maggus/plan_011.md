# Plan: RFC MUST Compliance — Master Gap Closure

## Introduction

Full-sweep MUST/MUST NOT extraction across RFC 1945, 9110, 9111, 9112, and 9114 revealed real gaps in TurboHttp's client-side compliance. This master plan breaks every gap into atomic tasks with exact file references and test patterns that integrate seamlessly into the existing project structure.

**Scope**: Client-side MUST requirements only. Strict mode — every RFC MUST must be implemented in TurboHttp itself.

## Goals

- Close all MISSING MUSTs in RFC 9110, 9111, 9112
- Upgrade all PARTIALLY implemented MUSTs to FULLY
- Establish RFC 9114 (HTTP/3) foundation
- Close RFC 9110 remaining gaps for 99%
- Target: >=98% MUST compliance for RFC 1945/9110/9111/9112

## Compliance Status Before This Plan

| RFC | Client MUSTs | Impl | Partial | Missing | Score |
|-----|-------------|------|---------|---------|-------|
| RFC 1945 | 24 | 11 | 2 | 1 | 86% |
| RFC 9110 | 99 | 27 | 3 | 18 | 73% |
| RFC 9111 | 96 | 64 | 23 | 8 | 90.6% |
| RFC 9112 | 82 | 75 | 7 | 0 | 91.5% |
| RFC 9114 | 145 | 2 | 0 | 140 | 1.4% |

---

## TIER 1 — Security-Critical (RFC 9110 §4, §10)

### TASK-001: Userinfo Stripping — Http2RequestEncoder `:authority` ✅

**RFC**: 9110 §4.2.4 — "A sender MUST NOT generate the userinfo subcomponent"

**Problem**: `Http2RequestEncoder.BuildHeaderList()` uses `uri.Authority` which includes userinfo if present.

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9113/Http2RequestEncoder.cs`
- Change: `uri.Authority` → `UriSanitizer.FormatAuthority(uri)` (uses `uri.Host` + `uri.Port`)

**Tests** (new file: `src/TurboHttp.Tests/RFC9110/03_UserinfoStrippingTests.cs`):
- Namespace: `TurboHttp.Tests.RFC9110`
- Class: `public sealed class UserinfoStrippingTests`

| DisplayName | Method | Description |
|-------------|--------|-------------|
| `RFC9110-4.2.4-UI-001: H2 authority strips userinfo from http URI` | `H2_Should_StripUserinfo_When_HttpUri` | `http://user:pass@host/` → `:authority` = `host` |
| `RFC9110-4.2.4-UI-002: H2 authority strips userinfo from https URI` | `H2_Should_StripUserinfo_When_HttpsUri` | |
| `RFC9110-4.2.4-UI-003: H2 authority preserves port after stripping` | `H2_Should_PreservePort_When_UserinfoPresent` | `http://u:p@host:8080/` → `host:8080` |
| `RFC9110-4.2.4-UI-004: H2 authority unchanged when no userinfo` | `H2_Should_NotChange_When_NoUserinfo` | |

**Effort**: 1h

---

### TASK-002: Userinfo Stripping — Http11Encoder absolute-form

**RFC**: 9110 §4.2.4

**Problem**: `Http11Encoder.WriteRequestLine()` with `absoluteForm=true` uses `uri.GetLeftPart(UriPartial.Query)` which includes userinfo.

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9112/Http11Encoder.cs`
- Change: In `WriteRequestLine()` with `absoluteForm`: rebuild URI without userinfo

**Tests** (extends `src/TurboHttp.Tests/RFC9110/03_UserinfoStrippingTests.cs`):

| DisplayName | Method | Description |
|-------------|--------|-------------|
| `RFC9110-4.2.4-UI-005: H11 absolute-form strips userinfo` | `H11_Should_StripUserinfo_When_AbsoluteForm` | |
| `RFC9110-4.2.4-UI-006: H11 origin-form unaffected by userinfo` | `H11_Should_NotContainUserinfo_When_OriginForm` | |

**Effort**: 1h

---

### TASK-003: Userinfo Stripping — Http10Encoder absolute-form

**RFC**: 9110 §4.2.4

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC1945/Http10Encoder.cs`
- Change: Same sanitization as TASK-002

**Tests** (extends `src/TurboHttp.Tests/RFC9110/03_UserinfoStrippingTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-4.2.4-UI-007: H10 absolute-form strips userinfo` | `H10_Should_StripUserinfo_When_AbsoluteForm` |
| `RFC9110-4.2.4-UI-008: H10 origin-form unaffected` | `H10_Should_NotContainUserinfo_When_OriginForm` |

**Effort**: 30min

---

### TASK-004: URI Sanitizer — Shared Helper

**Implementation**:
- New file: `src/TurboHttp/Protocol/RFC9110/UriSanitizer.cs`
- Namespace: `TurboHttp.Protocol.RFC9110`
- Class: `internal static class UriSanitizer`
- Methods: `FormatAuthority(Uri)`, `StripUserInfo(Uri)`, `FormatAbsoluteWithoutUserInfo(Uri)`

**Tests** (extends `src/TurboHttp.Tests/RFC9110/03_UserinfoStrippingTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-4.2.4-UI-009: FormatAuthority excludes userinfo` | `FormatAuthority_Should_ExcludeUserinfo` |
| `RFC9110-4.2.4-UI-010: FormatAuthority includes non-default port` | `FormatAuthority_Should_IncludePort` |
| `RFC9110-4.2.4-UI-011: FormatAuthority brackets IPv6` | `FormatAuthority_Should_BracketIPv6` |
| `RFC9110-4.2.4-UI-012: StripUserInfo preserves query and fragment` | `StripUserInfo_Should_PreservePath` |

**Effort**: 1h

---

### TASK-005: Referer Header Sanitization — RequestEnricherStage

**RFC**: 9110 §10.5 — "MUST NOT include fragment", "MUST NOT include userinfo", "MUST NOT send in unsecured HTTP if from secured protocol"

**Implementation**:
- File: `src/TurboHttp/Streams/Stages/Routing/RequestEnricherStage.cs`
- Change: Add `SanitizeReferer()` method in `Enrich()` after header merge

**Tests** (new file: `src/TurboHttp.StreamTests/Streams/21_RefererSanitizationTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `REF-001: Fragment stripped from Referer` | `Should_StripFragment_When_RefererHasFragment` |
| `REF-002: Userinfo stripped from Referer` | `Should_StripUserinfo_When_RefererHasUserinfo` |
| `REF-003: Referer removed on HTTPS to HTTP downgrade` | `Should_RemoveReferer_When_HttpsToHttp` |
| `REF-004: Referer preserved on same-scheme` | `Should_PreserveReferer_When_SameScheme` |
| `REF-005: Referer preserved when no downgrade` | `Should_PreserveReferer_When_HttpToHttp` |
| `REF-006: No Referer passes through unchanged` | `Should_NotAdd_When_NoRefererPresent` |

**Effort**: 2-3h

---

### TASK-006: Certificate Validation Callbacks — TurboClientOptions

**RFC**: 9110 §4.3.4 — "MUST verify service identity", "CN-ID MUST NOT be used", "MUST provide setting"

**Implementation**:
- File: `src/TurboHttp/Client/TurboClientOptions.cs` — already has `ServerCertificateValidationCallback` and `EnabledSslProtocols`
- Change: Add `bool DangerousAcceptAnyServerCertificate { get; init; }` (default: false)
- Verify default .NET validation uses DNS-ID/IP-ID (not CN-ID)

**Tests** (new file: `src/TurboHttp.Tests/RFC9110/04_CertificateValidationTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-4.3.4-CERT-001: Default options enable certificate validation` | `DefaultOptions_Should_EnableValidation` |
| `RFC9110-4.3.4-CERT-002: Custom callback is invoked` | `CustomCallback_Should_BeInvoked` |
| `RFC9110-4.3.4-CERT-003: DangerousAcceptAny overrides validation` | `DangerousAcceptAny_Should_DisableValidation` |

**Effort**: 3-4h

---

## TIER 2 — Protocol Compliance (RFC 9110)

### TASK-007: If-Range Validation

**RFC**: 9110 §13.1.5

**Implementation**:
- New file: `src/TurboHttp/Protocol/RFC9110/IfRangeValidator.cs`
- Call from: `RequestEnricherStage.Enrich()` after header merge

**Tests** (new file: `src/TurboHttp.Tests/RFC9110/05_IfRangeValidatorTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-13.1.5-IR-001: If-Range without Range throws` | `Should_Throw_When_IfRangeWithoutRange` |
| `RFC9110-13.1.5-IR-002: If-Range with weak ETag throws` | `Should_Throw_When_IfRangeWithWeakETag` |
| `RFC9110-13.1.5-IR-003: If-Range with HTTP-date when ETag available throws` | `Should_Throw_When_IfRangeDateAndETagAvailable` |
| `RFC9110-13.1.5-IR-004: If-Range with strong ETag and Range passes` | `Should_NotThrow_When_StrongETagAndRange` |
| `RFC9110-13.1.5-IR-005: If-Range with HTTP-date without ETag passes` | `Should_NotThrow_When_DateAndNoETag` |
| `RFC9110-13.1.5-IR-006: No If-Range passes` | `Should_NotThrow_When_NoIfRange` |

**Effort**: 2h

---

### TASK-008: CONNECT Response Body Handling

**RFC**: 9110 §9.3.6 — "MUST ignore Content-Length or Transfer-Encoding in successful CONNECT response"

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9112/Http11Decoder.cs`
- Change: When method == CONNECT and status 2xx → body length = 0

**Tests** (new file: `src/TurboHttp.Tests/RFC9110/06_ConnectResponseTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-9.3.6-CON-001: CONNECT 200 ignores Content-Length` | `Should_IgnoreContentLength_When_Connect200` |
| `RFC9110-9.3.6-CON-002: CONNECT 200 ignores Transfer-Encoding` | `Should_IgnoreTE_When_Connect200` |
| `RFC9110-9.3.6-CON-003: CONNECT 407 processes body normally` | `Should_ParseBody_When_Connect407` |
| `RFC9110-9.3.6-CON-004: Non-CONNECT 200 respects Content-Length` | `Should_RespectCL_When_NonConnect200` |

**Effort**: 3-4h

---

### TASK-009: 206 Partial Content Inspection

**RFC**: 9110 §15.3.7

**Implementation**:
- New file: `src/TurboHttp/Protocol/RFC9110/PartialContentValidator.cs`

**Tests** (new file: `src/TurboHttp.Tests/RFC9110/07_PartialContentTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9110-15.3.7-PR-001: 206 with Content-Range bytes is valid` | `Should_BeValid_When_ContentRangePresent` |
| `RFC9110-15.3.7-PR-002: 206 without Content-Range is invalid` | `Should_BeInvalid_When_NoContentRange` |
| `RFC9110-15.3.7-PR-003: 206 multipart/byteranges detected` | `Should_Detect_When_MultipartByteranges` |
| `RFC9110-15.3.7-PR-004: 200 response skips validation` | `Should_Skip_When_Not206` |

**Effort**: 2-3h

---

## TIER 3 — RFC 9112 Partial Gaps

### TASK-010: TE + Connection Header Auto-Addition Tests

**RFC**: 9112 §7.4

**Tests** (extends `src/TurboHttp.Tests/RFC9112/04_EncoderConnectionTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9112-7.4-TE-001: TE header auto-adds TE to Connection` | `Should_AddTEToConnection_When_TEHeaderPresent` |
| `RFC9112-7.4-TE-002: Connection already has TE — no duplicate` | `Should_NotDuplicate_When_ConnectionAlreadyHasTE` |
| `RFC9112-7.4-TE-003: Chunked excluded from TE field` | `Should_ExcludeChunked_When_TEContainsChunked` |

**Effort**: 1h

---

### TASK-011: No-Pipelining-After-Reconnect Guard

**RFC**: 9112 §9.3.2

**Implementation**:
- File: `src/TurboHttp/Streams/Stages/Routing/Http1XCorrelationStage.cs`
- Change: `_isFirstRequestAfterReconnect` flag — wait for response before pipelining

**Tests** (new file: `src/TurboHttp.StreamTests/RFC9112/14_Http11PipelineReconnectTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9112-9.3.2-PL-001: First request after connect is not pipelined` | `Should_WaitForResponse_When_FirstRequestAfterConnect` |
| `RFC9112-9.3.2-PL-002: Second request may pipeline after first response` | `Should_Pipeline_When_FirstResponseReceived` |
| `RFC9112-9.3.2-PL-003: Reconnect resets pipeline guard` | `Should_ResetGuard_When_ConnectionReestablished` |

**Effort**: 3h

---

### TASK-012: TLS Closure Alert Detection

**RFC**: 9112 §9.8

**Implementation**:
- File: `src/TurboHttp/Transport/ClientByteMover.cs`
- Change: Distinguish clean TLS close from abrupt TCP close

**Tests** (new file: `src/TurboHttp.StreamTests/RFC9112/15_TlsClosureTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9112-9.8-TLS-001: Clean TLS close completes response without CL` | `Should_CompleteResponse_When_CleanTlsClosure` |
| `RFC9112-9.8-TLS-002: Abrupt TCP close marks response incomplete` | `Should_MarkIncomplete_When_AbruptClose` |

**Effort**: 4h

---

### TASK-013: Incomplete Close Recovery Tests

**RFC**: 9112 §8 + §9.8

**Tests** (new file: `src/TurboHttp.StreamTests/RFC9112/16_IncompleteCloseRecoveryTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9112-8-IC-001: Chunked without zero-chunk is incomplete` | `Should_BeIncomplete_When_NoZeroChunk` |
| `RFC9112-8-IC-002: CL body truncated is incomplete` | `Should_BeIncomplete_When_BodyTruncated` |
| `RFC9112-8-IC-003: No CL + clean close is complete` | `Should_Complete_When_NoCLAndCleanClose` |
| `RFC9112-8-IC-004: Engine retries idempotent after incomplete` | `Should_Retry_When_IdempotentAndIncomplete` |

**Effort**: 2-3h

---

## TIER 4 — Caching Compliance (RFC 9111)

### TASK-014: Age Header Generation

**RFC**: 9111 §5.1 — "A cache MUST generate an Age header field"

**Implementation**:
- File: `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs`
- Change: On cache hit, inject Age header before push

**Tests** (extends `src/TurboHttp.Tests/RFC9111/02_CacheFreshnessTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-5.1-AGE-001: Age header added to cached response` | `Should_AddAgeHeader_When_ServingFromCache` |
| `RFC9111-5.1-AGE-002: Age value matches current age` | `Should_MatchCurrentAge_When_AgeHeaderGenerated` |
| `RFC9111-5.1-AGE-003: Existing Age header overwritten` | `Should_OverwriteAge_When_AlreadyPresent` |

**Effort**: 2h

---

### TASK-015: Qualified no-cache Directive Parsing

**RFC**: 9111 §5.2.2.3

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9111/CacheControlParser.cs` — parse `no-cache="field"`
- File: `src/TurboHttp/Protocol/RFC9111/CacheControl.cs` — new `IReadOnlyList<string>? NoCacheFields`

**Tests** (extends `src/TurboHttp.Tests/RFC9111/01_CacheControlParserTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-5.2.2.3-CC-030: no-cache="Set-Cookie" parses field list` | `Should_ParseFieldList_When_NoCacheQualified` |
| `RFC9111-5.2.2.3-CC-031: no-cache="A, B" parses multiple fields` | `Should_ParseMultipleFields_When_NoCacheQualified` |
| `RFC9111-5.2.2.3-CC-032: Unqualified no-cache sets flag, no fields` | `Should_SetFlag_When_UnqualifiedNoCache` |
| `RFC9111-5.2.2.3-CC-033: no-cache with empty quotes treated as unqualified` | `Should_TreatAsUnqualified_When_EmptyQuotes` |

**Effort**: 2h

---

### TASK-016: Qualified private Directive Parsing

**RFC**: 9111 §5.2.2.7

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9111/CacheControlParser.cs`
- File: `src/TurboHttp/Protocol/RFC9111/CacheControl.cs` — new `IReadOnlyList<string>? PrivateFields`

**Tests** (extends `src/TurboHttp.Tests/RFC9111/01_CacheControlParserTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-5.2.2.7-CC-034: private="Authorization" parses field` | `Should_ParseField_When_PrivateQualified` |
| `RFC9111-5.2.2.7-CC-035: Unqualified private sets flag only` | `Should_SetFlag_When_UnqualifiedPrivate` |

**Effort**: 1h

---

### TASK-017: Qualified no-cache Enforcement in CacheBidiStage

**RFC**: 9111 §5.2.2.3

**Implementation**:
- File: `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs`
- Change: Strip named fields from response when serving without revalidation

**Tests** (new file: `src/TurboHttp.Tests/RFC9111/05_QualifiedDirectiveTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-5.2.2.3-QD-001: no-cache="Set-Cookie" strips field on reuse` | `Should_StripField_When_NoCacheQualified` |
| `RFC9111-5.2.2.3-QD-002: no-cache="A, B" strips both fields` | `Should_StripMultipleFields_When_NoCacheQualified` |
| `RFC9111-5.2.2.3-QD-003: Unqualified no-cache requires full revalidation` | `Should_Revalidate_When_UnqualifiedNoCache` |
| `RFC9111-5.2.2.7-QD-004: private="Set-Cookie" excludes field from shared cache` | `Should_ExcludeField_When_PrivateQualified` |

**Effort**: 3h

---

### TASK-018: must-understand Directive

**RFC**: 9111 §5.2.2.3

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9111/CacheControlParser.cs` — parse `must-understand`
- File: `src/TurboHttp/Protocol/RFC9111/CacheControl.cs` — new `bool MustUnderstand`
- File: `src/TurboHttp/Protocol/RFC9111/HttpCacheStore.cs` — reject unknown status codes

**Tests** (extends `src/TurboHttp.Tests/RFC9111/04_CacheStoreTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-5.2.2.3-CS-030: must-understand + 200 allows storage` | `Should_Store_When_MustUnderstandAnd200` |
| `RFC9111-5.2.2.3-CS-031: must-understand + 299 prevents storage` | `Should_NotStore_When_MustUnderstandAndUnknownStatus` |
| `RFC9111-5.2.2.3-CS-032: must-understand absent allows any cacheable status` | `Should_Store_When_NoMustUnderstand` |

**Effort**: 1.5h

---

### TASK-019: Incomplete Response Cache Guard

**RFC**: 9111 §3.3

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9111/HttpCacheStore.cs` — reject 206 and Content-Range

**Tests** (extends `src/TurboHttp.Tests/RFC9111/04_CacheStoreTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-3.3-CS-033: 206 response not cached` | `Should_NotStore_When_206PartialContent` |
| `RFC9111-3.3-CS-034: Response with Content-Range not cached` | `Should_NotStore_When_ContentRangePresent` |
| `RFC9111-3.3-CS-035: 200 without Content-Range cached normally` | `Should_Store_When_200WithoutContentRange` |

**Effort**: 1h

---

### TASK-020: Cache Invalidation via Location/Content-Location

**RFC**: 9111 §4.4

**Implementation**:
- File: `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs`
- Change: On unsafe method (POST/PUT/DELETE) + 2xx, invalidate URIs from Location and Content-Location headers (same-origin only)

**Tests** (new file: `src/TurboHttp.Tests/RFC9111/06_CacheInvalidationTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-4.4-INV-001: POST 201 Location invalidates that URI` | `Should_Invalidate_When_PostWithLocation` |
| `RFC9111-4.4-INV-002: PUT 200 Content-Location invalidates that URI` | `Should_Invalidate_When_PutWithContentLocation` |
| `RFC9111-4.4-INV-003: Cross-origin Location not invalidated` | `Should_NotInvalidate_When_CrossOriginLocation` |
| `RFC9111-4.4-INV-004: GET 200 does not invalidate Location` | `Should_NotInvalidate_When_SafeMethod` |
| `RFC9111-4.4-INV-005: POST 500 does not invalidate` | `Should_NotInvalidate_When_ErrorResponse` |
| `RFC9111-4.4-INV-006: DELETE 204 invalidates request URI + Location` | `Should_Invalidate_When_Delete204WithLocation` |

**Effort**: 3h

---

### TASK-021: HEAD Response Cache Freshening

**RFC**: 9111 §4.3.5

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC9111/CacheValidationRequestBuilder.cs` — new `BuildHeadValidation()`
- File: `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs` — HEAD 304 → freshen stored GET

**Tests** (extends `src/TurboHttp.Tests/RFC9111/03_ConditionalRequestTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC9111-4.3.5-CR-020: HEAD validation request built correctly` | `Should_BuildHeadRequest_When_StaleEntry` |
| `RFC9111-4.3.5-CR-021: HEAD 304 freshens stored GET` | `Should_Freshen_When_Head304WithMatchingETag` |
| `RFC9111-4.3.5-CR-022: HEAD with mismatched ETag does not freshen` | `Should_NotFreshen_When_ETagMismatch` |

**Effort**: 3h

---

## TIER 5 — RFC 1945 Remaining Gap

### TASK-022: HTTP/1.0 Decompression Pipeline Verification

**RFC**: 1945 §10.3

**Implementation**:
- File: `src/TurboHttp/Streams/Http10Engine.cs` — verify DecompressionBidiStage is in pipeline

**Tests** (new file: `src/TurboHttp.StreamTests/RFC1945/09_Http10DecompressionPipelineTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC1945-10.3-DC-001: Http10Engine decompresses gzip responses` | `Should_Decompress_When_GzipContentEncoding` |
| `RFC1945-10.3-DC-002: Http10Engine decompresses x-gzip responses` | `Should_Decompress_When_XGzipContentEncoding` |
| `RFC1945-10.3-DC-003: Http10Engine passes identity unchanged` | `Should_PassThrough_When_IdentityEncoding` |

**Effort**: 1-2h

---

## TIER 6 — HTTP/3 Foundation

### TASK-023: HTTP/3 Error Code Enum

**RFC**: 9114 §8.1. **Detail**: See plan_016_rfc9114.md TASK-003.
**Effort**: 1h

### TASK-024: HTTP/3 Frame Type Hierarchy

**RFC**: 9114 §7. **Detail**: See plan_016_rfc9114.md TASK-004.
**Effort**: 6-8h

### TASK-025: QUIC Variable-Length Integer Codec

**RFC**: 9000 §16. **Detail**: See plan_016_rfc9114.md TASK-002.
**Effort**: 3-4h

### TASK-026: HTTP/3 Settings Parameter Registry

**RFC**: 9114 §7.2.4. **Detail**: See plan_016_rfc9114.md TASK-005.
**Effort**: 3-4h

### TASK-027: HTTP/3 Frame Decoder

**RFC**: 9114 §7.1. **Detail**: See plan_016_rfc9114.md TASK-007.
**Effort**: 8-10h

### TASK-028: HTTP/3 Frame Encoder

**RFC**: 9114 §7.1. **Detail**: See plan_016_rfc9114.md TASK-006.
**Effort**: 4-6h

---

## TIER 7 — RFC 9110 Extra (for 99%)

### TASK-029: Expect 100-Continue BidiStage

**RFC**: 9110 §10.1.1. **Detail**: See plan_013_rfc9110.md TASK-001.
**Effort**: 6-8h

### TASK-030: Auth Challenge Parser + Authorization Builder

**RFC**: 9110 §11.2.1, §11.2.2. **Detail**: See plan_013_rfc9110.md TASK-002.
**Effort**: 4-6h

### TASK-031: Date Header Auto-Generation

**RFC**: 9110 §5.6.7. **Detail**: See plan_013_rfc9110.md TASK-003.
**Effort**: 1h

### TASK-032: Range Header Large Decimal Parsing

**RFC**: 9110 §14.1.1. **Detail**: See plan_013_rfc9110.md TASK-004.
**Effort**: 1-2h

### TASK-033: Request Body Compression

**RFC**: 9110 §8.4. **Detail**: See plan_013_rfc9110.md TASK-005.
**Effort**: 4-6h

### TASK-034: CONNECT Port Enforcement

**RFC**: 9110 §9.3.6. **Detail**: See plan_013_rfc9110.md TASK-006.
**Effort**: 1h

---

## Non-Goals

- Server-side HTTP (client only)
- HTTP/3 server push acceptance (framing only, no push handling)
- 0-RTT QUIC (post-HTTP/3-MVP)
- WebSocket upgrade over HTTP/3
- QPACK codec (separate plan — plan_017, ~59-84h)

## Effort Summary

| Tier | Tasks | New Files | New Tests | Hours |
|------|-------|----------|----------|-------|
| 1: Security | TASK-001-006 | 4 prod, 2 test | ~24 | 8-12h |
| 2: Protocol | TASK-007-009 | 3 prod, 3 test | ~16 | 7-9h |
| 3: RFC 9112 | TASK-010-013 | 1 prod, 3 test | ~13 | 10-12h |
| 4: Caching | TASK-014-021 | 2 prod, 2 test | ~28 | 16-19h |
| 5: RFC 1945 | TASK-022 | 0 prod, 1 test | ~3 | 1-2h |
| 6: HTTP/3 Foundation | TASK-023-028 | 7 prod, 6 test | ~28 | 25-33h |
| 7: RFC 9110 Extra | TASK-029-034 | 5 prod, 5 test | ~24 | 17-24h |
| **Total Tier 1-5** | **22** | **10 prod, 11 test** | **~84** | **42-54h** |
| **Total Tier 1-7** | **34** | **22 prod, 22 test** | **~136** | **84-111h** |

## Compliance Target After Plan

| RFC | Before | After Tier 1-5 | After Tier 1-7 |
|-----|--------|---------------|---------------|
| RFC 1945 | 86% | 96% | 96% |
| RFC 9110 | 73% | 93% | **99%** |
| RFC 9111 | 90.6% | 99% | 99% |
| RFC 9112 | 91.5% | 98% | 98% |
| RFC 9114 | 1.4% | 1.4% | ~25% |

### Full 99% Roadmap (all plans)

| RFC | Plan | Tasks | Hours | Target |
|-----|------|-------|-------|--------|
| RFC 1945 | plan_012 | 1 | 3-4h | 100% |
| RFC 9110 | plan_011 + plan_013 | 20 | 59-78h | 99% |
| RFC 9111 | plan_011 + plan_014 | 10 | 19h | 99% |
| RFC 9112 | plan_011 + plan_015 | 7 | 11.25h | 99% |
| RFC 9114 | plan_016 | 40 | 154-211h | 99% |
| QPACK | plan_017 | 13 | 59-84h | Enabler |
| **Total** | **7 plans** | **91 tasks** | **305-409h** | **99%** |

## Open Questions

1. Should HTTP/3 push support be included in the MVP, or deferred entirely?
2. Is System.Net.Quic stable enough on Windows for production use in .NET 10?
3. Should we implement a `ReferrerPolicy` enum or just sanitize per RFC minimum?
4. For certificate validation: integrate via existing Servus.Akka TLS layer or separate stage?
