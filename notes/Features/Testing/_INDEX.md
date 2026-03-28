---
title: Testing Index
description: >-
  Index of testing feature notes — integration tests, fuzzing, security tests,
  and flakiness mitigation
tags:
  - features
  - testing
  - index
---
# Testing

Test infrastructure and test coverage features — integration tests, fuzzing, security, and flakiness mitigation.

## Notes

- [[Features/Testing/Feature005_H10_Flakiness_Mitigation|H10 Flakiness Mitigation]] — Three-phase mitigation of HTTP/1.0 test timeout failures caused by TCP connection churn
- [[Features/Testing/Feature006_Connection_Management_Tests|Connection Management Tests]] — Integration tests for HTTP/1.1 connection keep-alive, pipelining, and lifecycle
- [[Features/Testing/Feature007_Error_Handling_Tests|Error Handling Tests]] — Integration tests for HTTP/1.1 and HTTP/2 error handling and failure scenarios
- [[Features/Testing/Feature008_TLS_Integration_Tests|TLS Integration Tests]] — HTTPS/TLS connection testing using Kestrel TLS fixture
- [[Features/Testing/Feature013_Security_Tests|Security Tests]] — Adversarial security suite covering header injection, smuggling, cookie security, and HPACK attacks
- [[Features/Testing/Feature014_Decoder_Fuzzing|Decoder Fuzzing]] — Adversarial fuzzing for HTTP/1.0 and HTTP/1.1 decoders covering malformed input and boundary conditions
- [[Features/Testing/Feature015_H2_HPACK_Fuzzing|H2 HPACK Fuzzing]] — Adversarial fuzzing for HTTP/2 frame parser and HPACK decoder
