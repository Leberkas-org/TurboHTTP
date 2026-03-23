# Feature 016: Update LikeC4 Diagrams & VitePress Documentation

## Introduction

The LikeC4 architecture diagrams are out of sync with the codebase. Several implemented stages, protocol components, and pipeline paths are missing from the model and views. Most notably, the **Protocol Layer View** is completely absent — although layers.md describes four architectural layers, only three have dedicated diagrams. HTTP/3 is intentionally excluded from this update.

Additionally, there are minor VitePress gaps: Request Compression is missing from the home page, the Why page feature comparison is incomplete, and the Performance sidebar link leads to a 404.

### Architecture Context

- **Components involved:** All 7 LikeC4 files (`model.c4`, `model-pipeline.c4`, `specification.c4`, `views-architecture.c4`, `views-engines.c4`, `views-pipeline.c4`, `views-scenarios.c4`) + 4 VitePress pages
- **No VISION.md/ARCHITECTURE.md found** — context derived from CLAUDE.md and codebase analysis

## Goals

- Extend the LikeC4 model with all missing non-HTTP/3 components (feature stages, routing stages, protocol policies)
- Create the missing **Protocol Layer View** + add corresponding section to layers.md
- Complete the HTTP/2 Engine View (missing CorrelationStage, PrependPrefaceStage)
- Add missing stages to pipeline flows
- Extend pipeline view with missing includes
- Add HandlerBidiStage to all end-to-end scenarios
- Update VitePress pages: Request Compression feature card, Why page update, remove Performance sidebar link

## Tasks

### TASK-016-001: model.c4 — Add Missing Streams Components
**Description:** As a documentation reader, I want to see all implemented stages in the architecture model so that diagrams accurately reflect the actual codebase.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-016-003, TASK-016-004, TASK-016-005, TASK-016-006, TASK-016-007
**Parallel:** yes — can run alongside TASK-016-002

**Changes in `docs/likec4/model.c4`:**

**Add feature BidiStages (Streams Layer, after ~Line 201):**
```
HandlerBidiStage = component 'HandlerBidiStage' {
  #graphstage
  technology 'BidiStage'
  description 'Delegating handler bridge — outermost ring of the pipeline, enables custom DelegatingHandler middleware'
}

RequestCompressionBidiStage = component 'RequestCompressionBidiStage' {
  #graphstage
  technology 'BidiStage'
  description 'RFC 9110: compresses request bodies using gzip/deflate/brotli based on RequestCompressionPolicy'
}

ExpectContinueBidiStage = component 'ExpectContinueBidiStage' {
  #graphstage
  technology 'BidiStage'
  description 'RFC 9110 §10.1: handles Expect: 100-continue for conditional request bodies'
}
```

**Add routing stages (Streams Layer, after ExtractOptionsStage ~Line 79):**
```
GroupByHostKeyStage = component 'GroupByHostKeyStage' {
  #graphstage
  technology 'GraphStage'
  description 'Partitions requests into per-host sub-streams based on HostKey (host:port:scheme)'
}

HostKeyMergeBack = component 'HostKeyMergeBack' {
  #graphstage
  technology 'GraphStage'
  description 'Merges per-host sub-stream responses back into the main response stream'
}

MergeSubstreamsStage = component 'MergeSubstreamsStage' {
  #graphstage
  technology 'GraphStage'
  description 'Final merge of all sub-streams after per-host processing'
}
```

**Add structural relationships:**
```
// Feature BidiStages → Pipeline
turbohttp.streams.Engine -> turbohttp.streams.HandlerBidiStage 'Outermost pipeline ring'
turbohttp.streams.Engine -> turbohttp.streams.RequestCompressionBidiStage 'Compresses request bodies'
turbohttp.streams.Engine -> turbohttp.streams.ExpectContinueBidiStage 'Handles 100-continue'

// Routing
turbohttp.streams.Engine -> turbohttp.streams.GroupByHostKeyStage 'Partitions by host'
turbohttp.streams.GroupByHostKeyStage -> turbohttp.streams.ExtractOptionsStage 'Per-host sub-stream'

// Stage → Protocol delegates
turbohttp.streams.RequestCompressionBidiStage -> turbohttp.protocol.ContentEncodingEncoder 'Compresses request body'
turbohttp.streams.CookieBidiStage -> turbohttp.protocol.CookieParser 'Parses Set-Cookie headers'
```

**Acceptance Criteria:**
- [ ] `HandlerBidiStage`, `RequestCompressionBidiStage`, `ExpectContinueBidiStage` defined as components
- [ ] `GroupByHostKeyStage`, `HostKeyMergeBack`, `MergeSubstreamsStage` defined as components
- [ ] All new relationships correctly linked
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-002: model.c4 — Add Missing Protocol Components
**Description:** As a documentation reader, I want to see all protocol layer components in the model, especially policies and parsers.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-016-003, TASK-016-005
**Parallel:** yes — can run alongside TASK-016-001

**Changes in `docs/likec4/model.c4`:**

**Extend Protocol Layer (after ~Line 328):**
```
CookieParser = component 'CookieParser' {
  #protocol #cookie
  technology 'Class'
  description 'RFC 6265: parses Set-Cookie response headers into structured cookie attributes'
}

ContentEncodingEncoder = component 'ContentEncodingEncoder' {
  #staticClass #protocol
  technology 'Internal static class'
  description 'RFC 9110: gzip/deflate/brotli request body compression'
}

RedirectPolicy = component 'RedirectPolicy' {
  #protocol
  technology 'Record'
  description 'Configuration: MaxRedirects, AllowAutoRedirect, DangerousAcceptAnyServerCertificateValidator'
}

RetryPolicy = component 'RetryPolicy' {
  #protocol
  technology 'Record'
  description 'Configuration: MaxRetries, RetryableStatusCodes, delay strategy'
}

Expect100Policy = component 'Expect100Policy' {
  #protocol
  technology 'Record'
  description 'Configuration: when to send Expect: 100-continue header'
}

RequestCompressionPolicy = component 'RequestCompressionPolicy' {
  #protocol
  technology 'Record'
  description 'Configuration: compression algorithm, minimum body size threshold'
}

ConnectionPolicy = component 'ConnectionPolicy' {
  #protocol
  technology 'Record'
  description 'Configuration: max connections per host, idle timeout, keep-alive settings'
}
```

**Relationships:**
```
turbohttp.protocol.CookieJar -> turbohttp.protocol.CookieParser 'Parses Set-Cookie'
turbohttp.streams.RedirectBidiStage -> turbohttp.protocol.RedirectPolicy 'Reads redirect config'
turbohttp.streams.RetryBidiStage -> turbohttp.protocol.RetryPolicy 'Reads retry config'
turbohttp.streams.ExpectContinueBidiStage -> turbohttp.protocol.Expect100Policy 'Reads expect config'
turbohttp.streams.RequestCompressionBidiStage -> turbohttp.protocol.RequestCompressionPolicy 'Reads compression config'
turbohttp.streams.ConnectionReuseStage -> turbohttp.protocol.ConnectionPolicy 'Reads connection config'
```

**Acceptance Criteria:**
- [ ] All 7 protocol components defined (CookieParser, ContentEncodingEncoder, 5 policies)
- [ ] Relationships to BidiStages correct
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-003: views-architecture.c4 — Create Protocol Layer View + Update layers.md
**Description:** As a documentation reader, I want to see the Protocol Layer as its own diagram, since it is the only one of the four layers without a dedicated view.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-016-001, TASK-016-002
**Successors:** none
**Parallel:** no — requires new components from 001 + 002

**Changes in `docs/likec4/views-architecture.c4`:**

Add new view after `ioLayer` (~Line 46):
```
view protocolLayer of turbohttp.protocol {
  title 'TurboHttp — Protocol Layer'
  description 'Pure protocol logic: encoders, decoders, HPACK, cookie jar, cache store, and RFC business rules'
  include turbohttp.streams.CacheBidiStage
  include turbohttp.streams.CookieBidiStage
  include turbohttp.streams.RedirectBidiStage
  include turbohttp.streams.RetryBidiStage
  include turbohttp.streams.DecompressionBidiStage
  include turbohttp.streams.RequestCompressionBidiStage
  include turbohttp.protocol.*
}
```

**Changes in `docs/architecture/layers.md`:**

Add new `## Protocol Layer` section between Streams Layer and I/O Layer (~after Line 67):
```markdown
## Protocol Layer

<ClientOnly>
  <LikeC4Diagram viewId="protocolLayer" :height="560" />
</ClientOnly>

The Protocol layer contains pure, stateless (or self-contained stateful) protocol logic. Stages in the Streams layer delegate to these components for encoding, decoding, and RFC rule evaluation.

**Encoders & Decoders:**

| Component | Role |
|-----------|------|
| `Http10Encoder` / `Http10Decoder` | HTTP/1.0 request serialization and response parsing |
| `Http11Encoder` / `Http11Decoder` | HTTP/1.1 with chunked transfer, Host header, keep-alive |
| `Http2RequestEncoder` / `Http2FrameDecoder` | HTTP/2 frame serialization with HPACK header compression |

**HPACK (RFC 7541):**

`HpackEncoder` and `HpackDecoder` maintain synchronised dynamic tables. Sensitive headers (`Authorization`, `Cookie`) use `NeverIndex` automatically. `HuffmanCodec` provides static Huffman encoding/decoding.

**Business Logic:**

| Component | RFC | Purpose |
|-----------|-----|---------|
| `RedirectHandler` | RFC 9110 §15.4 | Method rewriting, HTTPS→HTTP protection, loop detection |
| `RetryEvaluator` | RFC 9110 §9.2 | Idempotency-based retry, Retry-After parsing |
| `ConnectionReuseEvaluator` | RFC 9112 §9 | Keep-alive/close decision |
| `CookieJar` + `CookieParser` | RFC 6265 | Domain/path matching, Secure/HttpOnly/SameSite |
| `ContentEncodingDecoder` | RFC 9110 §8.4 | gzip/deflate/brotli response decompression |
| `ContentEncodingEncoder` | RFC 9110 | gzip/deflate/brotli request body compression |

**Caching (RFC 9111):**

| Component | Purpose |
|-----------|---------|
| `HttpCacheStore` | Thread-safe in-memory LRU cache with Vary support |
| `CacheFreshnessEvaluator` | Freshness lifetime (s-maxage/max-age/Expires/heuristic) |
| `CacheValidationRequestBuilder` | Conditional requests (If-None-Match, If-Modified-Since) |
| `CacheControlParser` | Parses Cache-Control directive tokens |
```

**Acceptance Criteria:**
- [ ] `protocolLayer` view defined in views-architecture.c4
- [ ] `## Protocol Layer` section inserted in layers.md between Streams and I/O
- [ ] `<LikeC4Diagram viewId="protocolLayer">` referenced
- [ ] Tables listing all protocol components
- [ ] `npx likec4 export svg` compiles without errors
- [ ] `npm run docs:build` in docs directory passes

### TASK-016-004: views-engines.c4 — Complete HTTP/2 Engine View
**Description:** As a documentation reader, I want the HTTP/2 engine diagram to show all participating stages, not just a subset.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-016-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-016-005, TASK-016-006
**Model:** haiku

**Changes in `docs/likec4/views-engines.c4`:**

Add two missing includes to the `http2Engine` view (~Line 36):
```
include turbohttp.streams.Http20CorrelationStage
include turbohttp.streams.PrependPrefaceStage
```

**Acceptance Criteria:**
- [ ] `Http20CorrelationStage` visible in http2Engine view
- [ ] `PrependPrefaceStage` visible in http2Engine view
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-005: model-pipeline.c4 — Add Missing Pipeline Flows
**Description:** As a documentation reader, I want to see the complete request/response pipeline, including HandlerBidiStage, ExpectContinueBidiStage, RequestCompressionBidiStage, and per-host routing.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-016-001, TASK-016-002
**Successors:** TASK-016-006
**Parallel:** no — requires new components

**Changes in `docs/likec4/model-pipeline.c4`:**

**Insert HandlerBidiStage as outermost ring (before RequestEnricherStage):**
```
// Handler bridge — outermost pipeline ring
turbohttp.client.ITurboHttpClient -[flows]-> turbohttp.streams.HandlerBidiStage 'HttpRequestMessage (through DelegatingHandler chain)'
turbohttp.streams.HandlerBidiStage -[flows]-> turbohttp.streams.RequestEnricherStage 'HttpRequestMessage'
```
Remove/replace existing line `ITurboHttpClient -[flows]-> RequestEnricherStage`.

**Insert ExpectContinueBidiStage in request chain (between Cache and Engine):**
```
turbohttp.streams.CacheBidiStage -[flows]-> turbohttp.streams.ExpectContinueBidiStage 'HttpRequestMessage (cache miss)'
turbohttp.streams.ExpectContinueBidiStage -[flows]-> turbohttp.streams.Engine 'HttpRequestMessage (100-continue handled)'
```
Replace existing line `CacheBidiStage -[flows]-> Engine`.

**Insert RequestCompressionBidiStage in request chain (between Cookie and Cache):**
```
turbohttp.streams.CookieBidiStage -[flows]-> turbohttp.streams.RequestCompressionBidiStage 'HttpRequestMessage (after retry merge)'
turbohttp.streams.RequestCompressionBidiStage -[flows]-> turbohttp.streams.CacheBidiStage 'HttpRequestMessage (compressed if applicable)'
```
Replace existing line `CookieBidiStage -[flows]-> CacheBidiStage` (request direction).

**Add per-host routing before ExtractOptionsStage:**
```
turbohttp.streams.Engine -[flows]-> turbohttp.streams.GroupByHostKeyStage 'HttpRequestMessage'
turbohttp.streams.GroupByHostKeyStage -[flows]-> turbohttp.streams.ExtractOptionsStage 'HttpRequestMessage (per-host substream)'
```
Replace existing line `Engine -[flows]-> ExtractOptionsStage`.

**Add HandlerBidiStage as last stage before client in response chain:**
```
turbohttp.streams.RedirectBidiStage -[flows]-> turbohttp.streams.HandlerBidiStage 'HttpResponseMessage (final)'
turbohttp.streams.HandlerBidiStage -[flows]-> turbohttp.client.ITurboHttpClient 'HttpResponseMessage (final)'
```
Replace existing line `RedirectBidiStage -[flows]-> ITurboHttpClient`.

**Note:** The exact position of ExpectContinue and RequestCompression in the chain must be verified against `ProtocolCoreGraphBuilder.cs` — that is the single source of truth for stage ordering.

**Acceptance Criteria:**
- [ ] HandlerBidiStage as outermost ring (request + response direction)
- [ ] ExpectContinueBidiStage in request chain
- [ ] RequestCompressionBidiStage in request chain
- [ ] GroupByHostKeyStage before ExtractOptionsStage
- [ ] Pipeline ordering matches `ProtocolCoreGraphBuilder.cs`
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-006: views-pipeline.c4 — Include New Stages
**Description:** As a documentation reader, I want the pipeline flow diagram to show all stages participating in the pipeline.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-016-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-016-007
**Model:** haiku

**Changes in `docs/likec4/views-pipeline.c4`:**

Add missing includes:
```
// Feature stages
include turbohttp.streams.HandlerBidiStage
include turbohttp.streams.RequestCompressionBidiStage
include turbohttp.streams.ExpectContinueBidiStage

// Routing
include turbohttp.streams.GroupByHostKeyStage
include turbohttp.streams.ExtractOptionsStage
```

Add styles for new stages:
```
style turbohttp.streams.HandlerBidiStage { color secondary }
style turbohttp.streams.RequestCompressionBidiStage { color green }
style turbohttp.streams.ExpectContinueBidiStage { color green }
style turbohttp.streams.GroupByHostKeyStage { color blue }
style turbohttp.streams.ExtractOptionsStage { color blue }
```

**Acceptance Criteria:**
- [ ] All 5 new stages included in pipelineFlow view
- [ ] Styles defined (green for feature stages, blue for routing, secondary for handler)
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-007: views-scenarios.c4 — Add HandlerBidiStage to Scenarios
**Description:** As a documentation reader, I want the end-to-end scenarios to show the complete path, including the outermost HandlerBidiStage ring.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-016-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-016-006

**Changes in `docs/likec4/views-scenarios.c4`:**

In all three scenarios (scenarioHttp10, scenarioHttp11, scenarioHttp2), insert HandlerBidiStage as the outermost ring:

**Request side (after `user -> ITurboHttpClient`):**
```
turbohttp.client.ITurboHttpClient -> turbohttp.streams.HandlerBidiStage 'DelegatingHandler chain'
turbohttp.streams.HandlerBidiStage -> turbohttp.streams.RequestEnricherStage 'apply BaseAddress + default headers'
```
Replace existing line `ITurboHttpClient -> RequestEnricherStage`.

**Response side (before `ITurboHttpClient -> user`):**
```
turbohttp.streams.RedirectBidiStage -> turbohttp.streams.HandlerBidiStage 'final response'
turbohttp.streams.HandlerBidiStage -> turbohttp.client.ITurboHttpClient 'HttpResponseMessage'
```
Replace existing line `RedirectBidiStage -> ITurboHttpClient`.

**Acceptance Criteria:**
- [ ] HandlerBidiStage in scenarioHttp10 (request + response)
- [ ] HandlerBidiStage in scenarioHttp11 (request + response)
- [ ] HandlerBidiStage in scenarioHttp2 (request + response)
- [ ] `npx likec4 export svg` compiles without errors

### TASK-016-008: Update VitePress Pages
**Description:** As a website visitor, I want up-to-date feature information and no dead links.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other tasks
**Model:** haiku

**Changes:**

**`docs/index.md` — Extend Content Encoding feature card:**
```yaml
- icon: 🗜️
  title: Content Encoding
  details: Automatic gzip, deflate, and Brotli decompression. Server-driven content negotiation via Accept-Encoding. Optional request body compression.
```
(Extend existing Content Encoding card with "Optional request body compression")

**`docs/why/index.md` — Update feature comparison table:**
Add new row:
```
| Request Compression | ❌ | ❌ | ❌ | ✅ Built-in |
```

**`docs/.vitepress/config.ts` — Remove Performance sidebar:**
Remove lines 85-92 (entire `/performance/` sidebar block):
```typescript
// REMOVE:
'/performance/': [
    {
        text: 'Performance',
        items: [
            { text: 'Release 1.0 Benchmarks', link: '/performance/release-1.0' },
        ],
    },
],
```

**Acceptance Criteria:**
- [ ] Content Encoding feature card extended with request compression
- [ ] Request Compression row added to Why page comparison table
- [ ] Performance sidebar block removed from config.ts
- [ ] `npm run docs:build` passes

## Task Dependency Graph

```
TASK-016-001 ──→ TASK-016-003 (protocolLayer view)
TASK-016-002 ──┘
TASK-016-001 ──→ TASK-016-004 (HTTP/2 engine fix)
TASK-016-001 ──→ TASK-016-005 ──→ TASK-016-006 (pipeline view)
TASK-016-002 ──┘
TASK-016-001 ──→ TASK-016-007 (scenarios)
TASK-016-008 (VitePress — independent)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-016-001 | ~40k | none | yes (with 002, 008) | — |
| TASK-016-002 | ~30k | none | yes (with 001, 008) | — |
| TASK-016-003 | ~35k | 001, 002 | no | — |
| TASK-016-004 | ~10k | 001 | yes (with 005, 006, 007) | haiku |
| TASK-016-005 | ~40k | 001, 002 | no | — |
| TASK-016-006 | ~15k | 005 | yes (with 007) | haiku |
| TASK-016-007 | ~25k | 001 | yes (with 006) | — |
| TASK-016-008 | ~20k | none | yes (with all) | haiku |

**Total estimated tokens:** ~215k

## Functional Requirements

- FR-1: All implemented non-HTTP/3 GraphStages must be defined as components in model.c4
- FR-2: All protocol layer policies (Redirect, Retry, Expect100, RequestCompression, Connection) must be defined as components in model.c4
- FR-3: A `protocolLayer` view must exist and be embedded in layers.md
- FR-4: The HTTP/2 Engine View must show `Http20CorrelationStage` and `PrependPrefaceStage`
- FR-5: Pipeline flows in model-pipeline.c4 must include `HandlerBidiStage`, `ExpectContinueBidiStage`, `RequestCompressionBidiStage`, and `GroupByHostKeyStage`
- FR-6: All three end-to-end scenarios must show `HandlerBidiStage` as the outermost ring
- FR-7: The Performance sidebar link must no longer exist
- FR-8: Pipeline ordering in model-pipeline.c4 must match `ProtocolCoreGraphBuilder.cs`

## Non-Goals

- No HTTP/3 components (RFC 9114, RFC 9204, RFC 9000) in the LikeC4 model
- No new guide pages (HTTP/3 guide, Request Compression guide, etc.)
- No API reference expansion
- No new dynamic scenarios (retry loop, redirect loop, cache-hit shortcut)
- No benchmark page creation

## Technical Considerations

- **Pipeline ordering:** The exact stage ordering must be read from `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` — that is the single source of truth
- **LikeC4 compilation:** Run `npx likec4 export svg --output docs/public/diagrams docs/likec4` after each change
- **VitePress build:** `cd docs && npm run docs:build` must pass
- **Node.js 20+ required** for LikeC4 and VitePress
- **`ignoreDeadLinks: true`** is set in config.ts — dead links don't cause build failures, but the Performance link should still be removed

## Success Metrics

- `npx likec4 export svg` compiles without errors
- `npm run docs:build` compiles without errors
- All 4 layers have dedicated views and sections in layers.md
- HTTP/2 Engine View shows all 9 participating stages (previously 7)
- Pipeline View shows all feature and routing stages
- No 404 links in the sidebar

## Open Questions

*None — all questions have been resolved.*
