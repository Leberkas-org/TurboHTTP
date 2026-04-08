---
title: Analysis Index
description: >-
  Index of technical analysis notes — investigations, audits, and migration
  plans
tags:
  - architecture
  - analysis
  - index
---
# Analysis

Technical investigations, audits, and migration plans for TurboHTTP.

## Notes

- [[Architecture/Analysis/07-HTTP10_RECONNECTION_LIMITATION|HTTP/1.0 Pipeline Reconnection Limitation]] — ExtractOptionsStage emits ConnectItem once — HTTP/1.0 redirect/retry cannot reconnect after connection-close
- [[Architecture/Analysis/08-HTTP2_DECODER_MIGRATION|Http2Decoder Migration Plan]] — Migration from monolithic Http2Decoder to stage-based testing via Http2ProtocolSession
- [[Architecture/Analysis/10-DEADLOCK_ANALYSIS|Deadlock Analysis Catalog]] — Catalog of deadlock patterns discovered and resolved in the Akka.Streams pipeline
- [[Architecture/Analysis/11-STAGE_COMPLETION_AUDIT|Stage Completion Propagation Audit]] — Systematic audit of 48 GraphStage implementations finding 20 completion propagation bugs
