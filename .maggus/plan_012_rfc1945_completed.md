# Plan: RFC 1945 (HTTP/1.0) — 99% Client Compliance

## Introduction

RFC 1945 has 24 client-side MUSTs. After plan_011, 23 are covered (96%). This plan closes the last gap to reach 100%.

## Goals

- RFC 1945 client compliance from 96% to 100% (24/24)
- Remaining gap: HTTP/0.9 Simple-Response compatibility
- Verify decompression pipeline for HTTP/1.0

## Preconditions

- **Decompression pipeline**: `DecompressionBidiStage` exists in `src/TurboHttp/Streams/Stages/Features/` and handles gzip/deflate/brotli
- **Userinfo stripping**: `UriSanitizer.cs` exists in `src/TurboHttp/Protocol/RFC9110/` with `FormatAuthority()` and `StripUserInfo()` methods

## User Stories

### TASK-001: HTTP/0.9 Simple-Response Compatibility

**Description:** As a library user, I want TurboHttp to recognize HTTP/0.9 Simple-Responses so that even the oldest servers are handled correctly.

**RFC**: 1945 §3.1 — "HTTP/1.0 clients must understand any valid response in the format of HTTP/0.9 or HTTP/1.0"

**Problem**: `Http10Decoder` expects `HTTP/1.0` status-line. HTTP/0.9 Simple-Responses have no status-line — just body until connection close.

**Implementation**:
- File: `src/TurboHttp/Protocol/RFC1945/Http10Decoder.cs`
- Change: In `TryDecode()` when first bytes do NOT start with `HTTP/` → treat as HTTP/0.9 Simple-Response
- HTTP/0.9 response: status 200, no headers, body = all data until EOF
- `TryDecodeEof()` completes HTTP/0.9 body

**Acceptance Criteria:**
- [x] Http10Decoder recognizes responses without `HTTP/` prefix as HTTP/0.9
- [x] HTTP/0.9 response has StatusCode 200
- [x] HTTP/0.9 response has empty Headers
- [x] HTTP/0.9 body is read until EOF
- [x] HTTP/1.0 responses remain unchanged
- [x] Unit tests written and passing

**Tests** (new file: `src/TurboHttp.Tests/RFC1945/18_Http09SimpleResponseTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC1945-3.1-H09-001: Simple-Response without status-line is HTTP/0.9` | `Should_ParseAsHttp09_When_NoStatusLine` |
| `RFC1945-3.1-H09-002: HTTP/0.9 response has empty headers` | `Should_HaveEmptyHeaders_When_Http09` |
| `RFC1945-3.1-H09-003: HTTP/0.9 body read until EOF` | `Should_ReadBodyUntilEof_When_Http09` |
| `RFC1945-3.1-H09-004: HTTP/1.0 response still parsed normally` | `Should_ParseNormally_When_Http10StatusLine` |
| `RFC1945-3.1-H09-005: Empty response treated as HTTP/0.9` | `Should_HandleEmpty_When_ZeroBytesBeforeEof` |

**Stream-Test** (new file: `src/TurboHttp.StreamTests/RFC1945/10_Http09DecoderStageTests.cs`):

| DisplayName | Method |
|-------------|--------|
| `RFC1945-3.1-H09S-001: Http10DecoderStage handles HTTP/0.9 response` | `Should_DecodeHttp09_When_NoStatusLine` |
| `RFC1945-3.1-H09S-002: Http10DecoderStage handles mixed 0.9 and 1.0` | `Should_HandleMixed_When_Http09ThenHttp10` |

**Effort**: 3-4h

---

## Functional Requirements

- FR-1: Http10Decoder MUST recognize responses without `HTTP/` status-line as HTTP/0.9
- FR-2: HTTP/0.9 Simple-Response MUST have StatusCode 200
- FR-3: HTTP/0.9 body MUST be read until connection close
- FR-4: Existing HTTP/1.0 responses MUST remain unchanged

## Non-Goals

- HTTP/0.9 request sending (response parsing only)
- HTTP/0.9 over HTTP/2 or HTTP/3

## Success Metrics

- RFC 1945 compliance: 96% → 100% (24/24 client MUSTs)
- Zero regressions in existing 233 unit tests + 41 stream tests

## Compliance Table

| Metric | Before | After |
|--------|--------|-------|
| Implemented | 23/24 | **24/24** |
| Score | 96% | **100%** |
| New tests | — | ~7 |
