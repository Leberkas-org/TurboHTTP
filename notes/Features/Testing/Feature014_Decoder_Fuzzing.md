---
title: "Feature 014: HTTP/1.0 and HTTP/1.1 Decoder Fuzzing Tests"
description: "Adversarial fuzzing tests for HTTP/1.0 and HTTP/1.1 decoders covering malformed input, truncated frames, and boundary conditions"
tags: [features, history, fuzzing, testing, http10, http11, decoder]
status: completed
---

# Feature 014: HTTP/1.0 and HTTP/1.1 Decoder Fuzzing Tests

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Testing / Robustness |
| **Scope** | 2 steps |

## Description

Added adversarial fuzzing tests for the HTTP/1.x decoder stages, verifying correct handling of malformed and edge-case inputs without panics, hangs, or incorrect output.

- HTTP/1.0 decoder fuzzing — malformed status lines, missing headers, truncated bodies, invalid content-length values, non-UTF8 header values
- HTTP/1.1 decoder fuzzing — invalid chunk encoding, invalid transfer-encoding combinations, header field limit violations, pipeline request boundary errors

All fuzz inputs were crafted as deterministic test cases (not property-based) following the RFC 9112 §11 security considerations section.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHTTP.Tests/RFC1945/Http10DecoderFuzzingTests.cs` | HTTP/1.0 decoder fuzz cases |
| `src/TurboHTTP.Tests/RFC9112/Http11DecoderFuzzingTests.cs` | HTTP/1.1 decoder fuzz cases |

## See Also

- [[Features/Testing/Feature015_H2_HPACK_Fuzzing\|Feature 015]] — companion HTTP/2 and HPACK fuzzing
- [[Architecture/Layers/16-PROTOCOL_LAYER\|Protocol Layer]] — decoder pipeline architecture
- [[Architecture/Design/06-DECODER_PIPELINE_ARCHITECTURE\|Decoder Pipeline Architecture]] — three-layer decoder design
