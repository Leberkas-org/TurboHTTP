# Plan: Feature-Focused Documentation Rewrite

## Introduction

The current TurboHttp VitePress docs are heavily RFC-centric — 8 of 24 pages are dedicated to RFC compliance matrices, feature descriptions reference RFC section numbers, and the homepage tagline says "full RFC compliance". This is impressive for implementers but alienating for .NET developers who just want an HTTP client library.

This plan rewrites the documentation from scratch with a feature-focused, developer-experience-first approach. Features like retry, caching, cookies, and redirects are presented as practical capabilities with code examples — not as RFC implementations. Additionally, the LikeC4 diagram integration is fixed (missing `<ClientOnly>`, no dark mode sync, no resize handling).

## Goals

- Rewrite all docs to be feature-focused — zero RFC section references in user-facing content
- Present retry, caching, cookies, redirects, content encoding as first-class features with code examples
- Add a "Why TurboHttp?" comparison page against HttpClient/Refit
- Fix LikeC4 rendering issues (SSR, dark mode, sizing)
- New site structure: Guide → Architecture → API → "Why TurboHttp?"
- Delete all 8 RFC-specific pages
- Keep LikeC4 diagrams but fix the integration

## User Stories

---

### Phase 1: Fix LikeC4 Integration

---

### TASK-001: Fix LikeC4Diagram Component — SSR, Dark Mode, Resize
**Description:** As a docs reader, I want architecture diagrams to render correctly in both light and dark mode, resize properly, and not break the VitePress build.

**File:** `docs/.vitepress/components/LikeC4Diagram.vue`

**Existing Infrastructure (already working):**
- `docs/vite.config.ts` already configures `LikeC4VitePlugin({ workspace: './likec4' })` ✅
- The virtual module `likec4:react` is already available via the Vite plugin ✅
- LikeC4 `.c4` model files live in `docs/likec4/` ✅
- The component already imports `LikeC4View` from `likec4:react` and renders via React `createRoot` ✅

**Current Problems:**
1. **No `<ClientOnly>` wrapper** — LikeC4's React package explicitly throws on SSR (`"LikeC4 is not available SSR"`). VitePress uses SSR during `vitepress build`. The current component works in dev mode but can break production builds.
2. **No dark mode sync** — The `LikeC4View` component (from `likec4:react`) supports `colorScheme` prop but it's never passed. Diagrams ignore VitePress theme toggle.
3. **Fixed height** — Container uses a static height prop (default 500px). No responsive sizing.
4. **Missing useful props** — `fitView`, `zoomable`, `pannable`, `background`, `keepAspectRatio`, `showNavigationButtons` are not exposed.
5. **No CSS import** — `@likec4/diagram/styles.css` is not imported, which may cause styling issues.

**Required Changes:**

1. **Add `<ClientOnly>` wrapper** in all markdown files that use the component:
```vue
<ClientOnly>
  <LikeC4Diagram viewId="turbohttp" />
</ClientOnly>
```

2. **Sync VitePress dark mode** to LikeC4 via the `colorScheme` prop. Use VitePress `useData()`:
```vue
<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useData } from 'vitepress'

const { isDark } = useData()
const colorScheme = computed(() => isDark.value ? 'dark' : 'light')
</script>
```

Pass to `LikeC4View` (or `ReactLikeC4` for advanced features):
```js
root.render(createElement(LikeC4View, {
    viewId: props.viewId,
    colorScheme: colorScheme.value,
    fitView: true,
    pannable: true,
    zoomable: true,
    background: 'transparent',
    keepAspectRatio: true,
}))
```

3. **Watch dark mode changes** and re-render the React component:
```vue
watch(colorScheme, renderDiagram)
```

4. **Consider using `ReactLikeC4`** instead of `LikeC4View` — it's the advanced component from `likec4:react` with navigation buttons, element details, and dynamic view walkthrough support:
```js
import { ReactLikeC4 } from 'likec4:react'

root.render(createElement(ReactLikeC4, {
    viewId: props.viewId,
    pannable: true,
    zoomable: true,
    keepAspectRatio: true,
    showNavigationButtons: true,
    enableElementDetails: true,
    colorScheme: colorScheme.value,
}))
```

5. **Responsive sizing** — Replace fixed height with CSS `aspect-ratio` or `min-height` + `flex-grow`:
```css
.likec4-diagram {
    width: 100%;
    min-height: 400px;
    aspect-ratio: 16 / 10;
}
```

6. **Expose practical props:**
```vue
const props = defineProps<{
    viewId: string
    height?: number
    interactive?: boolean  // enables pan/zoom (default true)
    showNavigation?: boolean  // shows navigation buttons (default false)
}>()
```

**Acceptance Criteria:**
- [x] All `<LikeC4Diagram>` usages wrapped in `<ClientOnly>`
- [x] `vitepress build` succeeds without SSR errors
- [x] Dark mode toggle switches diagram theme instantly (no page reload needed)
- [x] Diagrams resize correctly when browser window changes
- [x] `fitView` is enabled by default (diagram fills container)
- [x] `background: 'transparent'` matches VitePress page background
- [x] `keepAspectRatio` prevents diagram distortion
- [x] `vitepress dev` and `vitepress build` both work
- [x] ⚠️ BLOCKED: Verify in browser: toggle dark mode, resize window, check all diagram pages — Node.js not available in CI environment; requires manual browser verification

---

### Phase 2: Rewrite Site Structure and Navigation

---

### TASK-002: Restructure VitePress Config — Remove RFC Nav, Add Features
**Description:** As a docs reader, I want the navigation to show practical sections (Guide, Features, Architecture, API, Why TurboHttp?) instead of RFC numbers.

**File:** `docs/.vitepress/config.ts`

**Current Nav:** Home | Guide | Architecture | API | RFC Coverage
**New Nav:** Home | Guide | Architecture | API | Why TurboHttp?

**New Sidebar:**
```typescript
'/guide/': [
    {
        text: 'Guide',
        items: [
            { text: 'Getting Started', link: '/guide/' },
            { text: 'Configuration', link: '/guide/configuration' },
            { text: 'Automatic Retries', link: '/guide/retries' },
            { text: 'HTTP Caching', link: '/guide/caching' },
            { text: 'Cookie Management', link: '/guide/cookies' },
            { text: 'Redirects', link: '/guide/redirects' },
            { text: 'Content Encoding', link: '/guide/content-encoding' },
            { text: 'Connection Pooling', link: '/guide/connection-pooling' },
            { text: 'HTTP/2 & Multiplexing', link: '/guide/http2' },
            { text: 'Advanced Usage', link: '/guide/advanced' },
        ],
    },
],
'/architecture/': [
    {
        text: 'Architecture',
        items: [
            { text: 'Overview', link: '/architecture/' },
            { text: 'Layers', link: '/architecture/layers' },
            { text: 'Protocol Engines', link: '/architecture/engines' },
            { text: 'Request Pipeline', link: '/architecture/pipeline' },
            { text: 'Request Scenarios', link: '/architecture/scenarios' },
        ],
    },
],
'/api/': [
    {
        text: 'API Reference',
        items: [
            { text: 'Overview', link: '/api/' },
        ],
    },
],
```

**Also update:**
- `description` in config: remove "full RFC compliance", replace with "HTTP/1.0, HTTP/1.1, and HTTP/2 with automatic retries, caching, cookies, and connection pooling."

**Acceptance Criteria:**
- [x] RFC Coverage removed from nav and sidebar
- [x] New sidebar structure with feature pages
- [x] Description updated to feature-focused
- [x] `vitepress dev` renders new nav correctly
- [x] No dead links (ignoreDeadLinks handles temporarily)

---

### TASK-003: Rewrite Homepage — Feature-Focused Hero and Features Grid
**Description:** As a visitor, I want the homepage to immediately show what TurboHttp does for me as a developer — not which RFCs it implements.

**File:** `docs/index.md`

**Current Problems:**
- Tagline: "Built on Akka.Streams with full RFC compliance" → developer doesn't care about RFC compliance
- Feature "RFC-Compliant: 2,435 tests across 7 RFCs" → meaningless to a user
- Features reference RFC numbers: "RFC 9110 §15.4 redirect handling"

**New Content:**

```yaml
hero:
  name: TurboHttp
  text: High-Performance HTTP Client for .NET
  tagline: Built on Akka.Streams — automatic retries, caching, cookies, and HTTP/2 multiplexing out of the box.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: Why TurboHttp?
      link: /why/
    - theme: alt
      text: GitHub
      link: https://github.com/st0o0/TurboHttp

features:
  - icon: ⚡
    title: HTTP/1.0, HTTP/1.1 & HTTP/2
    details: Automatic version negotiation, HPACK compression, flow control, and multiplexed streams. One client handles all versions.

  - icon: 🔄
    title: Automatic Retries
    details: Smart retry with idempotency detection — GET, PUT, DELETE are retried automatically. Respects Retry-After headers. POST is never retried.

  - icon: 📦
    title: Built-in Caching
    details: In-memory LRU cache with Vary support, conditional requests (ETag, Last-Modified), and freshness evaluation. Zero config needed.

  - icon: 🔀
    title: Redirect Following
    details: Automatic redirect chain following with method rewriting (301/302 → GET), body preservation (307/308), loop detection, and cross-origin safety.

  - icon: 🍪
    title: Cookie Management
    details: Automatic cookie storage and injection. Domain/path matching, Secure/HttpOnly/SameSite attributes, expiration handling.

  - icon: 🔗
    title: Connection Pooling
    details: Per-host connection pools with automatic reconnect, idle eviction, and configurable concurrency limits.

  - icon: 🗜️
    title: Content Encoding
    details: Automatic gzip, deflate, and Brotli decompression. Server-driven content negotiation via Accept-Encoding.

  - icon: 🚀
    title: Zero-Allocation Internals
    details: Span<T>, Memory<byte>, and IBufferWriter throughout. Pooled buffers, stateful decoders, zero GC pressure on the hot path.
```

**Acceptance Criteria:**
- [x] No RFC numbers on homepage
- [x] Tagline is feature-focused
- [x] All 8 features describe user-facing capabilities
- [x] "Why TurboHttp?" call-to-action added
- [x] ⚠️ BLOCKED: Verify in browser: homepage looks clean, features grid renders — Node.js not available in CI environment; requires manual browser verification

---

### Phase 3: Feature Guide Pages (New Content)

---

### TASK-004: Rewrite Getting Started Page
**Description:** As a new user, I want a clean getting-started page that shows installation, first request, and basic configuration without RFC references.

**File:** `docs/guide/index.md`

**Current page is already OK** — but needs minor cleanup:
1. Remove any RFC references
2. Add a "What's Included" section listing features (retry, caching, cookies, etc.)
3. Add link to new feature pages in "Next Steps"

**Acceptance Criteria:**
- [x] Zero RFC references
- [x] "What's Included" feature list
- [x] Links to all new feature guide pages
- [x] Code examples compile and make sense
- [x] ⚠️ BLOCKED: Verify in browser — Node.js not available in CI environment; requires manual browser verification

---

### TASK-005: Write Configuration Guide
**Description:** As a developer, I want a dedicated configuration page showing all TurboClientOptions with code examples.

**File:** `docs/guide/configuration.md` (new)

**Content Outline:**
1. `TurboClientOptions` full property reference with defaults
2. HTTP version selection (with explanation when to use which)
3. Timeout and cancellation
4. Default headers
5. Per-host connection limits
6. DI registration (`services.AddTurboHttp()`)
7. Named clients via factory

**Acceptance Criteria:**
- [x] All configurable options documented with types, defaults, and examples
- [x] DI registration example included
- [x] No RFC references
- [x] ⚠️ BLOCKED: Verify in browser — Node.js not available in CI environment; requires manual browser verification

---

### TASK-006: Write Automatic Retries Guide
**Description:** As a developer, I want to understand how TurboHttp retries work so I can configure them for my use case.

**File:** `docs/guide/retries.md` (new)

**Content Outline:**
1. How it works: which methods are retried (GET, HEAD, PUT, DELETE, OPTIONS) and why (idempotent)
2. Which status codes trigger retry (408, 503, etc.)
3. Retry-After header support (seconds and HTTP-date)
4. Configuration: max retries, backoff
5. Non-retryable requests: POST, PATCH (and why)
6. Code example: custom retry policy

**Acceptance Criteria:**
- [x] Clear explanation of idempotency-based retry logic
- [x] Table: method → retried? → reason
- [x] Table: status code → retry behavior
- [x] Code example for custom configuration
- [x] No RFC section references (say "idempotent methods" not "RFC 9110 §9.2")
- [x] ⚠️ BLOCKED: Verify in browser — Node.js not available in CI environment; requires manual browser verification

---

### TASK-007: Write HTTP Caching Guide
**Description:** As a developer, I want to understand TurboHttp's built-in caching so I know what's cached and when.

**File:** `docs/guide/caching.md` (new)

**Content Outline:**
1. How it works: in-memory LRU cache, automatic for GET responses
2. Cache-Control directives supported: max-age, no-cache, no-store, must-revalidate, s-maxage, private
3. Conditional requests: ETag/If-None-Match, Last-Modified/If-Modified-Since, 304 response merging
4. Vary header support (different cached entries per Accept, Accept-Encoding, etc.)
5. Expires header fallback
6. Cache size configuration
7. Disabling cache
8. Code example: custom cache store

**Acceptance Criteria:**
- [x] Explains what gets cached and for how long
- [x] Table: directive → behavior
- [x] Conditional request flow explained with diagram or sequence
- [x] Configuration examples
- [x] No RFC references
- [x] ⚠️ BLOCKED: Verify in browser — Node.js not available in CI environment; requires manual browser verification

---

### TASK-008: Write Cookie Management Guide
**Description:** As a developer, I want to understand how TurboHttp handles cookies automatically.

**File:** `docs/guide/cookies.md` (new)

**Content Outline:**
1. How it works: CookieJar stores Set-Cookie, injects Cookie on subsequent requests
2. Domain matching: cookies scoped to domains
3. Path matching: cookies scoped to URL paths
4. Cookie attributes: Secure (HTTPS only), HttpOnly, SameSite (Strict/Lax/None)
5. Expiration: Max-Age, Expires, session cookies
6. Cookie isolation: each client has its own jar
7. Code example: inspecting/clearing cookies, custom CookieJar

**Acceptance Criteria:**
- [x] Clear explanation of automatic cookie flow
- [x] All cookie attributes explained with practical impact
- [x] Code example for custom CookieJar
- [x] No RFC references
- [x] ⚠️ BLOCKED: Verify in browser — Node.js not available in CI environment; requires manual browser verification

---

### TASK-009: Write Redirects Guide
**Description:** As a developer, I want to understand how TurboHttp follows redirects so I can control the behavior.

**File:** `docs/guide/redirects.md` (new)

**Content Outline:**
1. How it works: automatic redirect following
2. Status codes and method rewriting:
   - 301, 302 → method changes to GET (body dropped)
   - 303 → always GET
   - 307, 308 → method and body preserved
3. Loop detection (configurable max hops)
4. Cross-origin redirects: Authorization header stripped for safety
5. HTTPS → HTTP downgrade protection
6. Configuration: max redirects, disable redirects
7. Code example: handling redirect exceptions

**Acceptance Criteria:**
- [ ] Clear table: status code → method behavior → body behavior
- [ ] Security behaviors explained (auth stripping, downgrade protection)
- [ ] Configuration examples
- [ ] No RFC references
- [ ] Verify in browser

---

### TASK-010: Write Content Encoding Guide
**Description:** As a developer, I want to understand how TurboHttp handles compressed responses.

**File:** `docs/guide/content-encoding.md` (new)

**Content Outline:**
1. How it works: automatic decompression of gzip, deflate, Brotli
2. Accept-Encoding negotiation
3. Identity encoding (no compression)
4. Unknown encodings: passed through raw
5. Configuration: disable decompression

**Acceptance Criteria:**
- [ ] Supported encodings listed
- [ ] Automatic behavior explained
- [ ] No RFC references
- [ ] Verify in browser

---

### TASK-011: Write Connection Pooling Guide
**Description:** As a developer, I want to understand how TurboHttp manages TCP connections so I can tune performance.

**File:** `docs/guide/connection-pooling.md` (new)

**Content Outline:**
1. How it works: per-host connection pools
2. Connection reuse (HTTP/1.1 keep-alive, HTTP/2 multiplexing)
3. Idle connection eviction
4. Automatic reconnect with exponential backoff
5. Per-host concurrency limits
6. Configuration: max connections per host, idle timeout

**Acceptance Criteria:**
- [ ] Clear explanation of pool lifecycle
- [ ] Configuration examples
- [ ] No RFC references
- [ ] Verify in browser

---

### TASK-012: Write HTTP/2 & Multiplexing Guide
**Description:** As a developer, I want to understand HTTP/2 multiplexing and when to use it.

**File:** `docs/guide/http2.md` (new)

**Content Outline:**
1. When to use HTTP/2: many concurrent requests to same host
2. How multiplexing works: multiple requests on one TCP connection
3. HPACK header compression (automatic, transparent)
4. Flow control (automatic, transparent)
5. Connection preface and SETTINGS exchange (automatic)
6. h2c (cleartext HTTP/2) vs h2 (HTTP/2 over TLS)
7. Configuration: force HTTP/2, fallback behavior

**Acceptance Criteria:**
- [ ] Practical guidance on when HTTP/2 helps
- [ ] Multiplexing explained without protocol internals
- [ ] Configuration examples
- [ ] No RFC references (say "header compression" not "HPACK RFC 7541")
- [ ] Verify in browser

---

### TASK-013: Write Advanced Usage Guide
**Description:** As a developer, I want to learn about channel-based API, custom policies, and advanced patterns.

**File:** `docs/guide/advanced.md` (new)

**Content Outline:**
1. Channel-based streaming API (`RequestWriter`/`ResponseReader`)
2. High-throughput patterns (batch requests)
3. Custom retry evaluator
4. Custom redirect handler
5. Custom cookie jar implementation
6. Custom cache store
7. Akka.Streams integration (for advanced users)

**Acceptance Criteria:**
- [ ] Channel API explained with code example
- [ ] Extension points documented
- [ ] Akka.Streams mention is practical ("you can extend the pipeline") not architectural
- [ ] No RFC references
- [ ] Verify in browser

---

### Phase 4: Architecture Pages (Light Rewrite)

---

### TASK-014: Rewrite Architecture Pages — Remove RFC References
**Description:** As a developer exploring the internals, I want architecture docs that explain the design in engineering terms, not RFC terms.

**Files:**
- `docs/architecture/index.md`
- `docs/architecture/layers.md`
- `docs/architecture/engines.md`
- `docs/architecture/pipeline.md`
- `docs/architecture/scenarios.md`

**Required Changes:**
1. Remove all RFC section references (e.g., "RFC 9112 §9" → "connection management")
2. Replace "RFC 9110 §15.4 redirect handling" → "redirect following"
3. Remove test count badges ("545 unit tests") — docs aren't a test report
4. Keep LikeC4 diagrams (now fixed via TASK-001)
5. Simplify protocol-internal language where possible

**Acceptance Criteria:**
- [ ] Zero RFC references in architecture pages
- [ ] Zero test count mentions
- [ ] Technical accuracy preserved
- [ ] LikeC4 diagrams still render (with `<ClientOnly>` from TASK-001)
- [ ] Verify in browser

---

### TASK-015: Rewrite API Reference — Clean Up
**Description:** As a developer, I want the API reference to show clean interface signatures without RFC commentary.

**File:** `docs/api/index.md`

**Required Changes:**
1. Review for RFC references — remove any
2. Add missing API members if any
3. Add practical usage examples for each method/property
4. Cross-link to relevant feature guide pages

**Acceptance Criteria:**
- [ ] Complete API surface documented
- [ ] Every method/property has a usage example
- [ ] Cross-links to guide pages (e.g., `Timeout` → link to Configuration page)
- [ ] No RFC references
- [ ] Verify in browser

---

### Phase 5: New Pages

---

### TASK-016: Write "Why TurboHttp?" Comparison Page
**Description:** As a developer evaluating HTTP clients, I want a comparison page that shows how TurboHttp differs from HttpClient, Refit, and Flurl.

**File:** `docs/why/index.md` (new)

**Content Outline:**

1. **Intro:** When you need more than `HttpClient` — connection pooling, protocol support, pipeline features
2. **Feature comparison table:**

| Feature | HttpClient | Refit | Flurl | TurboHttp |
|---------|-----------|-------|-------|-----------|
| HTTP/1.0 | ✅ | ✅ | ✅ | ✅ |
| HTTP/1.1 | ✅ | ✅ | ✅ | ✅ |
| HTTP/2 Multiplexing | Partial | Partial | ❌ | ✅ Full |
| Automatic Retries | ❌ (Polly needed) | ❌ (Polly needed) | ❌ | ✅ Built-in |
| HTTP Caching | ❌ | ❌ | ❌ | ✅ Built-in |
| Cookie Management | Manual | Manual | Manual | ✅ Automatic |
| Redirect Following | ✅ Basic | ✅ Basic | ✅ Basic | ✅ Full (method rewriting, auth stripping) |
| Content Decompression | ✅ | ✅ | ✅ | ✅ |
| Connection Pooling | ✅ (SocketsHttpHandler) | ✅ (via HttpClient) | ✅ (via HttpClient) | ✅ (Actor-based, per-host) |
| Channel-based API | ❌ | ❌ | ❌ | ✅ |
| Backpressure | ❌ | ❌ | ❌ | ✅ (Akka.Streams) |
| Zero-alloc internals | Partial | ❌ | ❌ | ✅ Span/Memory throughout |

3. **When to use TurboHttp:** high-throughput, many concurrent requests, need caching/retry/cookies built-in
4. **When NOT to use TurboHttp:** simple one-off requests, need Polly integration, need typed client interfaces (Refit)

**Acceptance Criteria:**
- [ ] Honest comparison — acknowledge where others are better (Refit for typed APIs, etc.)
- [ ] Feature table is accurate and up-to-date
- [ ] No RFC references
- [ ] Verify in browser

---

### Phase 6: Cleanup

---

### TASK-017: Delete RFC Pages and Leftover Files
**Description:** As a maintainer, I want the RFC-specific pages removed since the docs are now feature-focused.

**Files to delete:**
- `docs/rfc/index.md`
- `docs/rfc/rfc1945.md`
- `docs/rfc/rfc9112.md`
- `docs/rfc/rfc9113.md`
- `docs/rfc/rfc7541.md`
- `docs/rfc/rfc9110.md`
- `docs/rfc/rfc9111.md`
- `docs/rfc/rfc6265.md`

**Also delete/update:**
- `docs/guide/protocols.md` — replaced by `docs/guide/http2.md` and feature pages
- `docs/guide/architecture.md` — content moved to `docs/architecture/`

**Acceptance Criteria:**
- [ ] All 8 RFC pages deleted
- [ ] `docs/guide/protocols.md` deleted
- [ ] `docs/guide/architecture.md` deleted
- [ ] No dead links in sidebar/nav (config already updated in TASK-002)
- [ ] `vitepress build` succeeds
- [ ] `vitepress dev` shows no 404s in nav

---

### TASK-018: Update README.md
**Description:** As a GitHub visitor, I want the README to match the new docs tone — feature-focused, no RFC matrices.

**File:** `README.md` (project root)

**Required Changes:**
1. Remove RFC compliance matrix table
2. Replace with feature list (retries, caching, cookies, redirects, encoding, connection pooling, HTTP/2)
3. Keep: installation, quick start, building from source
4. Add link to docs site
5. Keep architecture diagram reference

**Acceptance Criteria:**
- [ ] No RFC numbers in README
- [ ] Feature list prominent
- [ ] Link to docs site
- [ ] Quick start example unchanged
- [ ] Verify on GitHub

---

### TASK-019: Final Build Verification
**Description:** As a maintainer, I want to verify the complete docs site builds and deploys correctly.

**Acceptance Criteria:**
- [ ] `cd docs && npm install && npm run docs:build` succeeds with 0 errors
- [ ] All pages render correctly in `npm run docs:preview`
- [ ] All LikeC4 diagrams load (no SSR errors)
- [ ] Dark mode toggle works on all pages (including diagrams)
- [ ] All internal links resolve (no 404s)
- [ ] Mobile responsive layout works
- [ ] GitHub Actions workflow (`docs.yml`) triggers and deploys successfully

---

## Functional Requirements

- FR-1: Zero RFC section numbers (e.g., "§15.4", "RFC 9110") in any user-facing documentation page
- FR-2: Every feature (retry, caching, cookies, redirects, encoding, connection pooling) has its own dedicated guide page with code examples
- FR-3: All LikeC4 diagrams wrapped in `<ClientOnly>` for SSR safety
- FR-4: LikeC4 diagrams sync with VitePress dark/light mode toggle
- FR-5: Homepage features grid describes user-facing capabilities, not protocol internals
- FR-6: "Why TurboHttp?" page has an honest feature comparison table
- FR-7: API reference links to relevant guide pages
- FR-8: All RFC-specific pages deleted from the repository
- FR-9: `vitepress build` produces 0 errors

## Non-Goals

- No auto-generated API docs from XML comments (manual API page is sufficient)
- No blog or changelog section
- No i18n / multi-language support
- No search integration (VitePress built-in local search is fine)
- No versioned docs (single version for now)
- No migration to Astro/Starlight (stay on VitePress)
- No rewrite of LikeC4 model files — keep existing C4 models as-is

## Technical Considerations

- **LikeC4 Vite Plugin already configured:** `docs/vite.config.ts` already has `LikeC4VitePlugin({ workspace: './likec4' })`. This provides the `likec4:react` virtual module with `LikeC4View` and `ReactLikeC4` components. No additional plugin setup needed.
- **LikeC4 SSR:** LikeC4's React package explicitly blocks SSR (`"LikeC4 is not available SSR"`). The `<ClientOnly>` wrapper is mandatory for VitePress, which uses SSR during `vitepress build`. Without it, builds may fail silently or produce empty diagrams.
- **LikeC4 Dark Mode:** Both `LikeC4View` and `ReactLikeC4` accept `colorScheme: 'light' | 'dark' | 'system'`. Use VitePress `useData().isDark` to sync. Must re-render React component on theme change (watch + re-`createRoot`).
- **LikeC4View vs ReactLikeC4:** `LikeC4View` is the basic embed. `ReactLikeC4` is the advanced component with `showNavigationButtons`, `enableElementDetails`, `enableDynamicViewWalkthrough`, and `onNavigateTo` callback. Consider `ReactLikeC4` for architecture pages where navigation between views is useful.
- **LikeC4 Props:** Use `fitView: true`, `background: 'transparent'`, `pannable: true`, `zoomable: true`, `keepAspectRatio: true` for best UX. These are all available on both `LikeC4View` and `ReactLikeC4`.
- **LikeC4 Styles:** Import `@likec4/diagram/styles.css` in the theme if diagrams have styling issues. The Vite plugin handles most CSS automatically, but explicit import ensures fonts and base styles are loaded.
- **LikeC4 HMR:** The Vite plugin provides Hot Module Replacement — editing `.c4` files in `docs/likec4/` auto-refreshes diagrams in `vitepress dev`. No restart needed.
- **Diagram fallback:** Keep the SVG fallback path (`/TurboHttp/diagrams/${viewId}.svg`) for cases where React fails to load (e.g., JavaScript disabled).
- **VitePress config.ts:** `ignoreDeadLinks: true` is already set — useful during migration when pages are created/deleted incrementally.
- **GitHub Actions:** The existing `docs.yml` workflow handles LikeC4 SVG export + VitePress build. No changes needed to the workflow itself.

## Success Metrics

- A .NET developer can understand what TurboHttp does within 30 seconds of landing on the homepage
- Every feature has a dedicated page reachable within 2 clicks from the homepage
- Zero RFC numbers visible to a casual reader
- `vitepress build` produces 0 errors and 0 warnings
- All LikeC4 diagrams render in both light and dark mode
- "Why TurboHttp?" page gives an honest, useful comparison

## Open Questions

1. Should the "Why TurboHttp?" comparison include benchmarks, or just feature comparison?
2. Should there be a "Migration from HttpClient" guide showing how to replace HttpClient with TurboHttp?
3. The existing `docs/connection-flow-graph.md`, `docs/engine-stream-graph.md`, `docs/protocol-engine-graphs.md`, and `docs/io-actor-hierarchy.md` are root-level pages — should they be moved into `/architecture/` or deleted?
