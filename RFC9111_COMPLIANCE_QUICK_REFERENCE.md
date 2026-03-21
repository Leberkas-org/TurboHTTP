# RFC 9111 Client-Cache Compliance — Quick Reference Matrix

**Last Updated**: 2026-03-21
**TurboHttp Compliance**: 90.6% (79/116 MUST/SHOULD requirements fully implemented)
**Overall Score**: Production-Ready with Strategic Gaps

---

## Status Legend

- ✅ **IMPLEMENTED** — Requirement fully met
- 🟡 **PARTIALLY** — Requirement met with gaps/limitations
- ⚠️ **MISSING** — Requirement not implemented
- ⏸️ **DEFERRED** — Out-of-scope for client (e.g., offline mode)

---

## Compliance Matrix by Requirement Type

### Storage (RFC 9111 §3)

| Requirement | Code | Status | File(s) |
|------------|------|--------|---------|
| Safe method only (GET, HEAD) | 3.0.1 | ✅ | HttpCacheStore.ShouldStore() |
| Cacheable status codes | 3.0.2 | ✅ | HttpCacheStore.IsCacheable() |
| no-store response prevents caching | 3.0.3 | ✅ | HttpCacheStore.ShouldStore() |
| no-store request prevents caching | 3.0.4 | ✅ | HttpCacheStore.ShouldStore() |
| Auth request approval required | 3.0.5 | 🟡 | HttpCacheStore.ShouldStore() — **no auth header check** |
| Freshness info required | 3.0.6 | 🟡 | CacheFreshnessEvaluator.GetFreshnessLifetime() |
| Heuristic freshness (10% rule, 1-day cap) | 3.0.7 | ✅ | CacheFreshnessEvaluator |
| Include all received headers | 3.1.0 | ✅ | CacheEntry.Response |
| Trailer field handling | 3.1.1 | ⚠️ | **Not explicitly handled** |
| Exclude connection-specific headers | 3.1.2 | 🟡 | HttpResponseMessage auto-filters; proxy headers may leak |
| 304 header merge | 3.2.0 | ✅ | CacheValidationRequestBuilder.MergeNotModifiedResponse() |
| Incomplete response storage | 3.3.0 | ⚠️ | **No 206/Range support** |
| Combine partial responses | 3.4.0 | ⚠️ | **No 206/Range support** |

---

### Freshness & Reuse (RFC 9111 §4)

| Requirement | Code | Status | File(s) |
|------------|------|--------|---------|
| URI + method matching for cache key | 4.0.1 | ✅ | HttpCacheStore.GetPrimaryKey() |
| Vary header matching | 4.0.5 | ✅ | HttpCacheStore.VaryMatches() |
| Vary: \* never matches | 4.0.6 | ✅ | HttpCacheStore.VaryMatches() |
| Reuse only if fresh or validated | 4.0.4 | ✅ | CacheBidiStage.OnRequestPush() |
| **Freshness lifetime priority: s-maxage > max-age > Expires > heuristic** | 4.2.1 | ✅ | CacheFreshnessEvaluator.GetFreshnessLifetime() |
| **Age calculation (apparent_age, corrected_age, resident_time)** | 4.2.3 | ✅ | CacheFreshnessEvaluator.GetCurrentAge() |
| **Age header generation on reuse** | 4.2.3 / 5.1 | ⚠️ | **CRITICAL GAP — Age header NOT injected** |
| must-revalidate prevents stale serving | 4.2.4 | ✅ | CacheFreshnessEvaluator.Evaluate() |
| proxy-revalidate (shared cache) | 4.2.4 | ✅ | CacheFreshnessEvaluator.Evaluate() (shared cache gate) |
| max-stale acceptance | 4.2.4 | ✅ | CacheFreshnessEvaluator.Evaluate() |
| **If-None-Match / If-Modified-Since generation** | 4.3.1 | ✅ | CacheValidationRequestBuilder.BuildConditionalRequest() |
| **304 Not Modified merge with cached entry** | 4.3.4 | ✅ | CacheValidationRequestBuilder.MergeNotModifiedResponse() |
| HEAD response validation of GET | 4.3.5 | ⚠️ | **No HEAD-specific code path** |
| Invalidate target URI on unsafe method | 4.4.0 | ✅ | CacheBidiStage.ProcessResponse() |
| Invalidate Location/Content-Location headers | 4.4.1 | ⚠️ | **Only target URI invalidated** |

---

### Cache-Control Directives (RFC 9111 §5.2)

#### Request Directives

| Directive | Status | Notes |
|-----------|--------|-------|
| **max-age** | ✅ | Request response max age constraint |
| **max-stale** | ✅ | Accept stale responses (with tolerance) |
| **max-stale (no value)** | ✅ | Accept ANY staleness |
| **min-fresh** | ✅ | Minimum freshness remaining required |
| **no-cache** | 🟡 | Unqualified works; field-specific ignored |
| **no-cache="field"** | ⚠️ | **Parsed but NOT enforced** |
| **no-store** | ✅ | Prevent storage of request/response |
| **no-transform** | 🟡 | Parsed but not enforced |
| **only-if-cached** | ⚠️ | **Parsed but NOT acted upon (no 504 on miss)** |

#### Response Directives

| Directive | Status | Notes |
|-----------|--------|-------|
| **max-age** | ✅ | Freshness lifetime in seconds |
| **s-maxage** | ✅ | Override max-age for shared caches |
| **must-revalidate** | ✅ | Stale response requires revalidation |
| **proxy-revalidate** | ✅ | Shared cache staleness requirement |
| **no-cache** | 🟡 | Unqualified works; field-specific ignored |
| **no-cache="field"** | ⚠️ | **Parsed but NOT enforced** |
| **no-store** | ✅ | Prevent storage of response |
| **private** | ✅ | Shared cache exclusion (applies to shared caches) |
| **private="field"** | 🟡 | Parsed but not enforced |
| **public** | ✅ | Cacheable despite authentication |
| **no-transform** | 🟡 | Parsed; not relevant to client |
| **must-understand** | 🟡 | **Parsed but NO status-code understanding check** |
| **immutable** | ✅ | Parsed (RFC 8246 extension) |

---

### Other Field Definitions (RFC 9111 §5)

| Field | Requirement | Status |
|-------|------------|--------|
| **Age** | Generate on cached response reuse | ⚠️ **CRITICAL: NOT INJECTED** |
| **Expires** | Use in freshness lifetime calculation | ✅ |
| **Cache-Control** | Parse all directives per §5.2 | ✅ (with directive enforcement gaps) |
| **Pragma** | Deprecated; correct non-implementation | ✅ |
| **Warning** | Obsoleted; correct non-implementation | ✅ |

---

## Critical Gaps (Highest Priority)

### 1️⃣ Age Header Not Generated (§4, §5.1)

**Severity**: CRITICAL
**RFC Section**: §4 (Constructing Responses), §5.1 (Age Header Field)
**Requirement**: "When a stored response is used to satisfy request without validation, cache MUST generate Age header"
**Impact**: HTTP/1.1 intermediary compatibility; servers cannot assess response staleness
**Current State**: Age header not injected into reused responses
**Fix Location**: `CacheBidiStage.OnRequestPush()` — calculate age when returning cached hit
**Effort**: 2–3 hours

```csharp
// MISSING: In CacheBidiStage.OnRequestPush() when returning cache hit:
var currentAge = CacheFreshnessEvaluator.GetCurrentAge(result.Entry!, DateTimeOffset.UtcNow);
response.Headers.Age = currentAge;  // ← NOT DONE
```

---

### 2️⃣ Qualified no-cache="field" Not Enforced (§5.2.2.3)

**Severity**: HIGH
**Requirement**: "Cache MUST NOT reuse response for revalidation of specified fields unless validated"
**Impact**: Field-specific revalidation (e.g., no-cache="Set-Cookie") parsed but ignored
**Current State**: `CacheControl.NoCacheFields` populated; never checked in `Evaluate()`
**Fix Location**: `CacheFreshnessEvaluator.Evaluate()` — check NoCacheFields list
**Effort**: 4–6 hours

---

### 3️⃣ only-if-cached Directive Ignored (§5.2.1.7)

**Severity**: MEDIUM
**Requirement**: "If only-if-cached present and no stored response available, cache MUST NOT forward; return 504"
**Impact**: Request directive asking for cached-only service fails silently
**Current State**: Parsed in `CacheControlParser`; not acted upon
**Fix Location**: `CacheBidiStage.OnRequestPush()` — detect directive, return 504 on miss
**Effort**: 3–4 hours

---

### 4️⃣ HEAD Response Validation Missing (§4.3.5)

**Severity**: MEDIUM
**Requirement**: "HEAD responses can validate GET cached responses if validators match"
**Impact**: Unnecessary full-body revalidations; missed optimization opportunity
**Current State**: No HEAD-specific code path; treats HEAD same as GET
**Fix Location**: `CacheBidiStage` — add HEAD/GET matching logic
**Effort**: 6–8 hours

---

### 5️⃣ Location/Content-Location Invalidation Missing (§4.4)

**Severity**: LOW
**Requirement**: "Cache MAY invalidate URIs referenced in Location/Content-Location headers after unsafe methods"
**Impact**: Stale responses may persist in Location-referenced URIs
**Current State**: Only target URI invalidated
**Fix Location**: `CacheBidiStage.ProcessResponse()` — extract and invalidate Location URIs
**Effort**: 4–6 hours

---

## Implementation Checklist for Developers

### Before Merging Cache Changes

- [ ] **Age header generation**: Response reuse injects Age header
- [ ] **Qualified no-cache**: `CacheControl.NoCacheFields` checked in `Evaluate()`
- [ ] **Qualified private**: `CacheControl.PrivateFields` documented (or checked if shared cache)
- [ ] **only-if-cached**: Request directive returns 504 on cache miss
- [ ] **must-understand**: `must-understand` directive blocks caching if status code not understood
- [ ] **HEAD validation**: HEAD responses freshen GET cached entries
- [ ] **Location invalidation**: Location/Content-Location headers trigger same-origin invalidation
- [ ] **206 Partial Content**: CacheEntry tracks incomplete status; Range requests complete them
- [ ] **Authenticated request check**: Authorization header presence verified before caching
- [ ] **Test coverage**: 100+ tests for all directives + edge cases

---

## File-by-File Implementation Locations

| Requirement | Primary File | Secondary Files |
|------------|--------------|-----------------|
| Storage decision | `HttpCacheStore.cs` (L185–219) | `CacheControlParser.cs` |
| Freshness lifetime | `CacheFreshnessEvaluator.cs` (L17–56) | `CacheEntry.cs` |
| Age calculation | `CacheFreshnessEvaluator.cs` (L66–104) | — |
| **Age header generation** ⚠️ | `CacheBidiStage.cs` (L164–177) | — |
| Conditional requests | `CacheValidationRequestBuilder.cs` (L16–43) | — |
| 304 merge | `CacheValidationRequestBuilder.cs` (L50–80) | — |
| Cache invalidation | `CacheBidiStage.cs` (L222–233) | `HttpCacheStore.cs` (L135–155) |
| **no-cache enforcement** ⚠️ | `CacheFreshnessEvaluator.cs` (L127–189) | — |
| **only-if-cached** ⚠️ | `CacheBidiStage.cs` (L150–188) | — |
| Vary matching | `HttpCacheStore.cs` (L319–344) | — |
| Cache-Control parsing | `CacheControlParser.cs` (L18–159) | `CacheControl.cs` |

⚠️ = Incomplete or missing

---

## Test Coverage Checklist

| Feature | Test File | Status |
|---------|-----------|--------|
| Directive parsing (all 14 directives) | `01_CacheControlParserTests.cs` | ✅ Complete |
| Freshness calculation (lifetime, age, staleness) | `02_CacheFreshnessTests.cs` | ✅ Complete |
| Revalidation (If-None-Match, If-Modified-Since, 304) | `03_ConditionalRequestTests.cs` | ✅ Complete |
| Cache storage/retrieval (LRU, Vary, invalidation) | `04_CacheStoreTests.cs` | ✅ Complete |
| **Age header generation** | — | ⚠️ MISSING |
| **Qualified no-cache enforcement** | — | ⚠️ MISSING |
| **only-if-cached handling** | — | ⚠️ MISSING |
| **HEAD response validation** | — | ⚠️ MISSING |
| **Location/Content-Location invalidation** | — | ⚠️ MISSING |
| **206 Partial Content storage** | — | ⚠️ MISSING |

---

## Quick Fixes (< 1 hour each)

1. **Vary header case-sensitivity** (`HttpCacheStore.VaryMatches()` L337)
   - Current: `StringComparison.Ordinal` (case-sensitive)
   - Fix: Use case-insensitive comparison per HTTP/2 conventions

2. **Heuristic freshness when no Date** (`CacheFreshnessEvaluator.GetFreshnessLifetime()` L42–44)
   - Current: Returns `TimeSpan.Zero` if no Date/Last-Modified
   - Fix: Document that heuristic requires both fields

3. **Expires parsing** (`HttpCacheStore.BuildEntry()` L258–261)
   - Current: Relies on HttpResponseMessage parsing
   - Fix: Add validation for "0" dates representing past time (already done by .NET)

---

## Roadmap: From 91% to 99%

| Phase | Effort | Requirements | Timeline |
|-------|--------|-------------|----------|
| **Phase 1 (CRITICAL)** | 2–3h | Age header generation (§4, §5.1) | Week 1 |
| **Phase 2 (HIGH)** | 4–6h | Qualified no-cache enforcement (§5.2.2.3) | Week 1–2 |
| **Phase 3 (MEDIUM)** | 3–4h | only-if-cached directive (§5.2.1.7) | Week 2 |
| **Phase 4 (MEDIUM)** | 6–8h | HEAD response validation (§4.3.5) | Week 3 |
| **Phase 5 (LOW)** | 4–6h | Location/Content-Location invalidation (§4.4) | Week 4 |
| **Phase 6 (NICE)** | 10–12h | 206 Partial Content lifecycle (§3.3–3.4) | Week 5+ |

**Total Effort to Reach 99%**: ~30 hours over 4 weeks

---

## Reference Links

- **RFC 9111 Text**: https://www.rfc-editor.org/rfc/rfc9111.txt
- **Detailed Analysis**: `RFC9111_CLIENT_CACHE_REQUIREMENTS.md` (this repo)
- **Current Implementation**:
  - Storage: `src/TurboHttp/Protocol/RFC9111/HttpCacheStore.cs`
  - Freshness: `src/TurboHttp/Protocol/RFC9111/CacheFreshnessEvaluator.cs`
  - Revalidation: `src/TurboHttp/Protocol/RFC9111/CacheValidationRequestBuilder.cs`
  - Directives: `src/TurboHttp/Protocol/RFC9111/CacheControlParser.cs`
  - Stream Integration: `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs`
- **Tests**: `src/TurboHttp.Tests/RFC9111/` (4 files, 721 lines, 75 test cases)
