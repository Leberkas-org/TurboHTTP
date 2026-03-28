---
title: Protocol Index
description: >-
  Index of protocol-level feature notes — bug fixes, stage implementations, and
  architectural changes
tags:
  - features
  - protocol
  - index
---
# Protocol

Protocol-level features — bug fixes, stage implementations, and architectural changes in the HTTP pipeline.

## Notes

- [[Features/Protocol/Feature003_Decompression_Stage|Decompression Stage]] — Initial HTTP response body decompression stage (superseded by Feature 020)
- [[Features/Protocol/Feature004_HTTP10_Deadlock_Fix|HTTP/1.0 Deadlock Fix]] — Fixed permanent demand stall in ConnectionReuseStage for HTTP/1.0 pipelines
- [[Features/Protocol/Feature017_ConnectionStage_Race|ConnectionStage Race Fix]] — Fixed premature completion race in ConnectionStage and redirect test fragility
- [[Features/Protocol/Feature020_ContentEncoding_Consolidation|ContentEncoding Consolidation]] — Consolidated scattered decompression logic into a single ContentEncodingBidiStage
