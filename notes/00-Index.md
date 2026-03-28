# TurboHttp Knowledge Base

This is the central hub for all TurboHttp project knowledge — connecting session logs, architecture decisions, RFC compliance notes, and feature planning.

## Architecture & Design Decisions

- [[Architecture/00-ONBOARDING|Developer Onboarding Guide]] — Start here: project purpose, tech stack, build commands, AI & human workflows, key code patterns
- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Client → Handlers → Streams → Protocol → Transport
- [[Architecture/Design/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming, conventions, stage lifecycle
- [[Architecture/Status/03-KNOWN_GAPS_AND_LIMITATIONS|Known Gaps & Limitations]] — Critical issues, workarounds, priority roadmap
- [[Architecture/Status/04-CURRENT_STATE_SUMMARY|Current State Summary]] — Implementation completeness, status, next milestones
- [[Architecture/Guides/05-BENCHMARK_PATTERNS|Benchmark Patterns]] — BDN conventions, port assignments, TCP TIME_WAIT workarounds
- [[Architecture/Design/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]] — Three-layer Pipeline/EventAggregator/CompletionDecoder pattern
- [[Architecture/Analysis/07-HTTP10_RECONNECTION_LIMITATION|HTTP/1.0 Reconnection Limitation]] — ExtractOptionsStage single-emit bug
- [[Architecture/Analysis/08-HTTP2_DECODER_MIGRATION|Http2Decoder Migration]] — Phases 39-62, ProtocolSession migration mapping
- [[Architecture/Guides/09-CLAUDE_PREFERENCES|Claude Preferences]] — Language, knowledge capture, response style
- [[Architecture/Analysis/11-STAGE_COMPLETION_AUDIT|Stage Completion Audit]] — 48-stage audit, 20 completion propagation bugs found and fixed
- [[Architecture/Guides/12-TEST_ORGANIZATION|Test Organization]] — Test projects, base classes, fixtures, conventions, completed phases
- [[Architecture/Layers/13-CLIENT_LAYER|Client Layer]] — ITurboHttpClient, factory, DI integration, request lifecycle
- [[Architecture/Layers/14-TRANSPORT_LAYER|Transport Layer]] — Actor-free connection pool, Channels I/O, TCP/QUIC, backpressure
- [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] — GraphStage categories, BidiFlow composition, pipeline data flow
- [[Architecture/Layers/16-PROTOCOL_LAYER|Protocol Layer]] — Encoder/decoder patterns, HPACK/QPACK, RFC subfolder structure
- [[Architecture/Guides/17-DIAGNOSTICS_INTEGRATION|Diagnostics Integration]] — DiagnosticListener, ETW EventSource, OTel Metrics

See [Architecture Notes](./Architecture/) for full decision records.

## RFC Compliance & Coverage

**Overall Compliance**: 86/100 — Production-Ready for HTTP/1.0, 1.1, 2.0

- [[RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] — Detailed compliance scores, gaps, and priorities (⭐ START HERE)
- All RFC reference documents are in the [rfc/](./rfc/) folder

## Features

### Protocol
- [[Features/Protocol/Feature003_Decompression_Stage|Feature 003: Decompression Stage]] — Initial standalone DecompressionStage (superseded by Feature 020)
- [[Features/Protocol/Feature004_HTTP10_Deadlock_Fix|Feature 004: HTTP/1.0 Deadlock Fix]] — Demand propagation deadlock fix via DequeueSignalStage
- [[Features/Protocol/Feature017_ConnectionStage_Race|Feature 017: ConnectionStage Race Fix]] — Race condition fixes in connection establishment
- [[Features/Protocol/Feature020_ContentEncoding_Consolidation|Feature 020: ContentEncoding Consolidation]] — Consolidation into ContentEncodingBidiStage

### Testing
- [[Features/Testing/Feature005_H10_Flakiness_Mitigation|Feature 005: H10 Flakiness Mitigation]] — Integration test flakiness mitigation for HTTP/1.0 suite
- [[Features/Testing/Feature006_Connection_Management_Tests|Feature 006: Connection Management Tests]] — HTTP/1.1 connection management integration tests
- [[Features/Testing/Feature007_Error_Handling_Tests|Feature 007: Error Handling Tests]] — HTTP error handling and resilience integration tests
- [[Features/Testing/Feature008_TLS_Integration_Tests|Feature 008: TLS Integration Tests]] — TLS/HTTPS integration test suite
- [[Features/Testing/Feature013_Security_Tests|Feature 013: Security Tests]] — Security-focused integration tests (certificate validation, auth headers)
- [[Features/Testing/Feature014_Decoder_Fuzzing|Feature 014: Decoder Fuzzing]] — HTTP/1.x response decoder fuzz tests
- [[Features/Testing/Feature015_H2_HPACK_Fuzzing|Feature 015: H2 HPACK Fuzzing]] — HTTP/2 HPACK header compression fuzz tests

### Diagnostics
- [[Features/Diagnostics/Feature009_Akka_Logging_Bridge|Feature 009: Akka Logging Bridge]] — Akka.NET → Microsoft.Extensions.Logging bridge
- [[Features/Diagnostics/Feature010_Tracing_Infrastructure|Feature 010: Tracing Infrastructure]] — Distributed tracing with ActivitySource and W3C trace context
- [[Features/Diagnostics/Feature011_OTel_Metrics|Feature 011: OTel Metrics]] — OpenTelemetry metrics integration
- [[Features/Diagnostics/Feature012_Diagnostic_EventSource|Feature 012: Diagnostic EventSource]] — ETW EventSource for high-performance diagnostics

### Infrastructure
- [[Features/Infrastructure/Feature016_TracingBidi_Consolidation|Feature 016: TracingBidi Consolidation]] — Consolidation of tracing/diagnostics into TracingBidiStage
- [[Features/Infrastructure/Feature018_Docs_Site_Revision|Feature 018: Docs Site Revision]] — VitePress documentation site revision and content update
- [[Features/Infrastructure/Feature019_Stream_Survival|Feature 019: Stream Survival]] — Stream error absorption and survival hardening

### Performance
- [[Features/Performance/Feature024_Benchmark_Comparison|Feature 024: Benchmark Comparison]] — TurboHttp vs HttpClient performance comparison

## Active Debugging

See [Debugging Notes](./Debugging/) for active investigations.

## Templates

- [[Templates/Session-Log|Session-Log]] — Daily work capture
- [[Templates/ADR|ADR]] — Architecture Decision Records
- [[Templates/RFC-Note|RFC-Note]] — RFC compliance gap tracking (distinct from RFC-Index)
- [[Templates/Bug-Investigation|Bug-Investigation]] — Structured debugging

## Getting Started

- [[VAULT_STYLE_GUIDE|Vault Style Guide]] — Structure, frontmatter, formatting conventions
- [[OBSIDIAN_CSS_SETUP|Obsidian CSS Setup]] — Visual consistency, theme selection, CSS snippets
- **Sessions folder**: `notes/Sessions/` — Optional session logs (use Session-Log template)
