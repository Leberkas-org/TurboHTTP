---
title: Design Index
description: >-
  Index of core architectural design notes — layered architecture, stage
  patterns, decoder pipeline
tags:
  - architecture
  - design
  - index
---
# Design

Core architectural patterns and design decisions for TurboHTTP.

## Notes

- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — 7-layer design with strict separation of concerns from client API to TCP/QUIC transport
- [[Architecture/Design/02-STAGE_PATTERNS|Stage Patterns]] — GraphStage patterns, port naming conventions, and lifecycle management for Akka.Streams
- [[Architecture/Design/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]] — Three-layer decoder architecture for HTTP/1.0, HTTP/1.1, and HTTP/2
