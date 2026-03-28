---
title: "Feature 015: HTTP/2 Frame and HPACK Adversarial Fuzzing Tests"
description: "Adversarial fuzzing for HTTP/2 frame parser and HPACK decoder covering malformed frames and compression attacks"
tags: [features, history, fuzzing, testing, http2, hpack, decoder]
status: completed
---

# Feature 015: HTTP/2 Frame and HPACK Adversarial Fuzzing Tests

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Testing / Robustness |
| **Scope** | 2 tasks (TASK-015-001 through TASK-015-002) |
| **Maggus Plan** | Not available |

## Description

Extended the fuzzing test coverage to HTTP/2 frame parsing and HPACK header compression, complementing the HTTP/1.x fuzzing from [[Features/Feature014_Decoder_Fuzzing\|Feature 014]].

- **TASK-015-001**: HTTP/2 frame parser adversarial fuzzing — invalid frame types, wrong payload lengths, frames on invalid stream IDs, reserved bit violations (RFC 9113 §4.1)
- **TASK-015-002**: HPACK decoder adversarial fuzzing — Huffman decoding errors, integer representation overflows, invalid index table references, header list size violations (RFC 7541)

Tests complemented the security tests from [[Features/Feature013_Security_Tests\|Feature 013]] (HPACK bomb), focusing more on parser correctness than attack-specific scenarios.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp.Tests/RFC9113/Http20FrameParserFuzzingTests.cs` | HTTP/2 frame parser fuzz cases |
| `src/TurboHttp.Tests/RFC9113/HpackDecoderFuzzingTests.cs` | HPACK decoder adversarial tests |

## See Also

- [[Features/Feature014_Decoder_Fuzzing\|Feature 014]] — HTTP/1.x decoder fuzzing
- [[Features/Feature013_Security_Tests\|Feature 013]] — security-focused adversarial tests
- [[Architecture/16-PROTOCOL_LAYER\|Protocol Layer]] — HPACK internals
