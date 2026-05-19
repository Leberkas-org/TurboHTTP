# VitePress Documentation Redesign

Full overhaul of the TurboHTTP documentation site: visual refresh, content restructuring, accuracy fixes, and new real-world scenario pages.

## Goals

1. Fix 10+ content inaccuracies where docs diverge from source code
2. Correct the server narrative — TurboHTTP Server is standalone, not Kestrel-based
3. Restructure navigation so users find content in fewer clicks
4. Add real-world scenario pages showing feature combinations
5. Redesign the homepage with a custom Vue component
6. Refresh visual design while keeping the Emerald/Violet brand

## Non-Goals

- No full custom VitePress theme (DefaultTheme stays for content pages)
- No new features or code changes to TurboHTTP itself
- No auto-generated API docs from source (manual reference pages)

---

## Visual Design

**Direction**: Hybrid — clean/modern base + code-first hero + bold performance stats.

**Color palette**: Emerald (#10b981) brand, Violet (#8b5cf6) accent. Same as current, refined application.

**Homepage**: Custom `HomePage.vue` component replacing VitePress `layout: home` frontmatter.

**Content pages**: VitePress DefaultTheme with enhanced custom CSS — refined typography, better code block styling, improved callout boxes.

### Homepage Layout

Top to bottom:

1. **Hero section** (dark background)
   - Gradient title "TurboHTTP" (emerald → violet)
   - Tagline: "High-Performance HTTP Client & Server for .NET"
   - Three stat badges: `Zero Alloc` | `HTTP/1–3 + QUIC` | `Backpressure`
   - Tabbed code block: Client tab / Server tab showing minimal usage
   - Two CTA buttons: "Get Started" (brand) + "View on GitHub" (alt)

2. **Features section** (light background)
   - 6 curated cards in 3×2 grid:
     - Multi-Protocol — HTTP/1.0, 1.1, 2 & 3 (QUIC), automatic version negotiation
     - Zero Allocation — Span\<T\>, Memory\<byte\>, pooled buffers, zero GC on hot path
     - Smart Retry & Cache — idempotency-aware retries + LRU cache with ETag, built in
     - Middleware & Routing — ASP.NET Core-style pipeline + entity gateway to Akka.NET actors
     - Connection Pooling — per-host pools, idle eviction, automatic reconnect
     - Standalone Server — actor-based HTTP server with TCP/QUIC transport, supervisor hierarchy, graceful shutdown

3. **Comparison section**
   - Condensed table from current "Why TurboHTTP?" page
   - Columns: Feature | HttpClient | Refit | Flurl | TurboHTTP
   - 8 rows: HTTP/2 Multiplexing, HTTP/3 (QUIC), Automatic Retries, HTTP Caching, Cookie Management, Backpressure, Zero-alloc Internals, Channel-based API

4. **Install section**
   - `dotnet add package TurboHTTP` code block
   - Three link buttons: Getting Started | Client Docs | Server Docs

5. **Footer**
   - MIT License | © TurboHTTP Contributors | GitHub

---

## Navigation

### Top nav (4 items + GitHub icon)

```
[logo] Getting Started | Client | Server | API    [GitHub]
```

Removed: Home (logo links home), Quick Guide (merged into Getting Started), Why TurboHTTP (comparison moved to homepage), Architecture (moved under Getting Started + deep-dive section).

### Sidebar structure

**Getting Started**
```
Getting Started
├── Overview               /getting-started/
├── Client Quick Start     /getting-started/client
├── Server Quick Start     /getting-started/server
├── Architecture Overview  /getting-started/architecture
└── Migration from HttpClient  /getting-started/migration
```

**Client**
```
Client
├── Overview               /client/
├── Installation & Setup   /client/installation
├── Configuration          /client/configuration
├── Connection Pooling     /client/connection-pooling
├── Automatic Retries      /client/retries
├── HTTP Caching           /client/caching
├── Cookie Management      /client/cookies
├── Redirects              /client/redirects
├── Content Encoding       /client/content-encoding
├── HTTP/2 & Multiplexing  /client/http2
├── HTTP/3 & QUIC          /client/http3
├── Real-World Scenarios   /client/scenarios        ← NEW
└── Troubleshooting        /client/troubleshooting
```

**Server**
```
Server
├── Overview               /server/
├── Installation & Setup   /server/installation
├── Configuration          /server/configuration
├── Hosting & Lifecycle    /server/hosting
├── Middleware Pipeline    /server/middleware
├── Routing                /server/routing
├── Parameter Binding      /server/binding
├── Validation             /server/validation
├── Entity Gateway         /server/entity-gateway
├── Real-World Scenarios   /server/scenarios         ← NEW
└── Troubleshooting        /server/troubleshooting
```

**API**
```
API Reference
├── Overview               /api/
├── Client API             /api/client
├── Client Options         /api/client-options
├── Feature Options        /api/feature-options
├── Server API             /api/server
└── Entity Gateway API     /api/entity-gateway
```

**Architecture (deep-dive, linked from Getting Started)**
```
Architecture
├── Request Pipeline       /architecture/pipeline
├── Protocol Engines       /architecture/engines
├── Handler Design         /architecture/handlers
├── E2E Scenarios          /architecture/scenarios
└── Extending the Pipeline /architecture/extending
```

Architecture pages are not in the top nav. They appear in the sidebar only when the user navigates to an `/architecture/` URL (via links from Getting Started > Architecture Overview or cross-links in feature pages). The sidebar config uses the `/architecture/` prefix to show the architecture sidebar contextually.

---

## Content Changes

### Critical: Server Narrative Correction

The TurboHTTP Server is a fully standalone HTTP server built on Akka.Streams with its own transport layer (Servus.Akka.Transport). It does NOT use or depend on Kestrel.

**Architecture:**
- `ServerSupervisorActor` → `ListenerActor` (one per endpoint) → `ConnectionActor` (one per client)
- `TcpListenerFactory` and `QuicListenerFactory` from Servus.Akka.Transport
- Protocol engines (`Http10/11/20/30ServerEngine`) selected via ALPN negotiation
- `AddTurboKestrel` is named for configuration convention familiarity, not because Kestrel is involved

**Pages affected:**
- Homepage: feature card #6 changes from "Kestrel Integration" to "Standalone Server"
- `server/index.md`: reframe from "configure Kestrel" to "configure TurboHTTP Server"
- `server/installation.md`: remove Kestrel framing, explain AddTurboKestrel naming
- `server/configuration.md`: clarify these are TurboHTTP Server options, not Kestrel options
- `server/hosting.md`: explain actor-based lifecycle (Supervisor → Listener → Connection)
- `getting-started/architecture.md`: describe the standalone server architecture
- `api/server.md`: clarify server registration is for TurboHTTP's own server

### Content Accuracy Fixes

All API reference pages must be verified against source code. Known discrepancies:

| Doc claim | Reality | Fix |
|-----------|---------|-----|
| `ITurboHttpClient.MaxResponseContentBufferSize` | Does not exist | Remove; document `MaxBufferedBodySize` + `MaxStreamedBodySize` on options |
| `Http2Options.MaxFrameSize` default 16 KiB | Actually 64 KiB | Fix default |
| `Http2Options.HeaderTableSize` default 4 KiB | Actually 64 KiB | Fix default |
| `Http2Options.InitialStreamWindowSize` default 65535 | Actually 2 MiB | Fix default |
| `Http3Options.AllowEarlyData` | Does not exist | Remove |
| `Http3Options.AllowServerPush` | Does not exist | Remove |
| `Http3Options.QpackMaxTableCapacity` default 4 KiB | Actually 16 KiB | Fix default |
| `Http1Options.MaxBatchWeight` | Does not exist | Remove |
| `TurboEntityBuilder.WithEntityKey()` | Does not exist | Remove |
| `MapTurboEntity<TKey>` signature | Two distinct overloads exist | Document both correctly |
| Undocumented: `MaxBufferedBodySize` | Exists on TurboClientOptions | Add |
| Undocumented: `MaxStreamedBodySize` | Exists on TurboClientOptions | Add |
| Undocumented: `SocketSendBufferSize` | Exists on TurboClientOptions | Add |
| Undocumented: `SocketReceiveBufferSize` | Exists on TurboClientOptions | Add |
| Undocumented: `Http3Options.MaxReconnectBufferSize` | Exists | Add |

### New Content: Real-World Scenarios

**`client/scenarios.md`** — combined feature examples:
- Authenticated REST API client: BaseAddress + Bearer token + retry + cache
- Web scraper: cookies + redirects + content encoding + connection pooling
- High-throughput batch processor: channel API + HTTP/2 multiplexing + backpressure
- Microservice communication: retry + timeout + HTTP/2

Each scenario shows the full DI registration, request code, and explains which features interact.

**`server/scenarios.md`** — combined feature examples:
- REST API: routing + parameter binding + validation + entity gateway
- Middleware pipeline: logging + auth + CORS composition
- Actor-based CQRS: entity gateway + message factories + response mapping
- Multi-protocol endpoint: HTTP/1.1 + HTTP/2 on same server

Each scenario shows full `Program.cs`, explains the actor hierarchy, and includes curl test commands.

### Structural Fixes

**Eliminate duplication:**
- Remove duplicate quickstart code from `client/index.md` and `server/index.md`
- Getting Started section becomes the single source for "hello world" examples
- Client/Server index pages become overviews linking to features, not repeating setup code

**Fix dead ends:**
- Getting Started has 5 sidebar entries (was: Quick Guide with 1)
- API Reference has 6 sidebar entries (was: 1 monolithic page)
- "Why TurboHTTP?" standalone page removed (comparison on homepage)

**Add cross-links:**
- Every feature page gets a callout: "How it works → [Architecture: Pipeline](/architecture/pipeline)"
- Architecture pages get reverse links: "Configure this → [Client: Caching](/client/caching)"
- Getting Started pages have "Next steps" linking to Client/Server guides

**Rename server sidebar:**
- "Advanced" section removed; Binding and Validation move to main feature list
- These are core routing concerns, not advanced topics

---

## Pages: Create, Move, Delete

### New files (7)
- `docs/getting-started/index.md`
- `docs/getting-started/client.md`
- `docs/getting-started/server.md`
- `docs/getting-started/architecture.md`
- `docs/getting-started/migration.md`
- `docs/client/scenarios.md`
- `docs/server/scenarios.md`

### Split files (1 → 5)
- `docs/api/index.md` (482 lines) → split into:
  - `docs/api/index.md` (overview + links)
  - `docs/api/client.md`
  - `docs/api/client-options.md`
  - `docs/api/feature-options.md`
  - `docs/api/server.md`
  - `docs/api/entity-gateway.md`

### Moved files (1)
- `docs/client/migration.md` → `docs/getting-started/migration.md`

### Deleted files/directories (2)
- `docs/quickstart/` (content absorbed into getting-started/)
- `docs/why/` (content absorbed into homepage)

### Modified files (significant rewrite)
- `docs/index.md` — new custom homepage layout
- `docs/.vitepress/config.ts` — new nav + sidebar structure
- `docs/.vitepress/theme/index.ts` — register HomePage.vue
- `docs/.vitepress/theme/custom.css` — enhanced styles
- `docs/server/index.md` — reframe without Kestrel narrative
- `docs/server/installation.md` — reframe without Kestrel narrative
- `docs/server/configuration.md` — verify defaults, reframe
- `docs/server/hosting.md` — add actor hierarchy description
- `docs/client/index.md` — remove duplicate quickstart, add overview
- All feature pages — add cross-link callouts

### New Vue components (2)
- `docs/.vitepress/components/HomePage.vue` — custom landing page
- `docs/.vitepress/components/CodeTabs.vue` — tabbed code block (Client/Server)

### Unchanged
- `docs/.vitepress/components/LikeC4Diagram.vue` — keep as-is
- `docs/likec4/` — all C4 model files unchanged
- `docs/logo/` and `docs/public/logo/` — unchanged
- Architecture deep-dive pages — keep content, add cross-links only

---

## Phases

### Phase 1: Foundation
- Create new directory structure (`getting-started/`, split `api/`)
- Update `config.ts` with new nav and sidebar
- Register new theme components in `index.ts`

### Phase 2: Content Accuracy
- Fix all 15 documented discrepancies in API reference pages
- Verify every option default against source code
- Reframe all server pages to remove Kestrel narrative
- Document undocumented properties

### Phase 3: Content Restructuring
- Create Getting Started section (absorb quickstart + architecture overview + migration)
- Split API monolith into sub-pages
- Remove duplicate quickstart code from client/server index pages
- Add cross-links between feature and architecture pages
- Delete quickstart/ and why/ directories

### Phase 4: New Content
- Write `client/scenarios.md` with 4 real-world examples
- Write `server/scenarios.md` with 4 real-world examples
- Verify all code examples compile conceptually against actual API

### Phase 5: Visual Redesign
- Build `HomePage.vue` with hero, features, comparison, install sections
- Build `CodeTabs.vue` for tabbed code blocks
- Update `custom.css` with enhanced typography, code blocks, callouts
- Update `index.md` to use custom layout
- Remove emoji feature cards, replace with designed card components

### Phase 6: Polish
- Test all internal links (no broken references)
- Verify sidebar navigation works for all sections
- Test light/dark mode
- Test mobile responsiveness
- Run VitePress build to verify no errors
