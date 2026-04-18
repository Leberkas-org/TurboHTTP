---
rfc_section: '5'
---
# RFC7838 §5 – Caching Alt-Svc Information

## Cache Freshness

Clients MUST use the `ma` (max-age) parameter to determine the freshness lifetime of cached Alt-Svc entries.

### Default Behavior

If the `ma` parameter is not present in the Alt-Svc header:
- For HTTPS origins: Clients SHOULD cache the entry for a reasonable default duration (e.g., 24 hours / 86400 seconds)
- For HTTP origins: Clients SHOULD NOT cache the entry (treat as session-only)

## Scope of Caching

Alt-Svc information is cached **per origin**, not per host or endpoint.

**Origin definition (RFC3986):**
```
scheme + authority = origin
```

Example origins:
- `https://example.com:443` (default HTTPS)
- `https://example.com:8443` (non-standard HTTPS)
- `http://example.com:80` (HTTP)

Clients MUST treat `example.com` and `sub.example.com` as **separate origins** for Alt-Svc caching.

## Invalidation

An origin's Alt-Svc cache entry is invalidated when:

1. **Explicit Clear Header:** Origin sends `Alt-Svc: clear` (or `Alt-Svc: ""`)
   - Clients MUST immediately clear all cached entries for that origin
   - No new alternative services are available

2. **Expiration:** The `ma` value expires (current-time > cached-time + ma)
   - Clients SHOULD remove the entry from cache
   - Next request to origin will refresh if Alt-Svc header is resent

3. **Protocol Failure:** Client detects connection failure to alternative service
   - Clients MAY invalidate the specific alt-svc entry
   - Fallback to origin or other alternatives

## Persistence

### Across Sessions

The `persist` parameter (when present in HTTPS responses) indicates whether clients MAY store Alt-Svc information persistently:

```
Alt-Svc: h3="example.com:443"; ma=31536000; persist=1
```

**Rules:**
- Clients SHOULD only respect `persist=1` on HTTPS origins
- Clients MAY persist across browser/client restarts
- Clients MAY ignore this parameter (behavior is optional)
- Expiration (based on `ma`) still applies even for persisted entries

### HTTP-Only Origins

For non-HTTPS origins (scheme `http://`):
- Clients SHOULD NOT persist Alt-Svc entries across sessions
- Clients MUST treat as session-only, regardless of `persist` parameter
- Clearing browser cache MUST clear all HTTP Alt-Svc entries

## Cache Key

Alt-Svc cache entries are keyed by:
1. **Origin** (scheme + host + port)
2. **Protocol-id** (h2, h3, h2c, etc.)

Example cache structure:
```
Cache[
  origin="https://example.com:443",
  protocol="h3"
] = {
  alt_authority: "example.com:443",
  expires_at: <time>,
  ma: 7200,
  persist: true
}
```

## Multiple Entries per Origin

An origin MAY advertise multiple alternative services in a single response:

```
Alt-Svc: h2=":443"; ma=3600, h3=":443"; ma=3600
```

Clients MUST cache each independently:
```
Cache["https://example.com", "h2"] = {...}
Cache["https://example.com", "h3"] = {...}
```

Clients MAY attempt protocols in order of preference or availability.

## Connection Pooling Implications

When caching Alt-Svc information:

1. Clients SHOULD maintain separate connection pools for each alternative protocol
2. Clients MAY proactively establish connections to advertised alternatives
3. Clients MUST validate certificate validity for HTTPS alternatives (SNI, hostname matching)
4. Clients SHOULD implement exponential backoff for failed alternative connections

## Interaction with HTTPS

For HTTPS origins, Alt-Svc information received on the TLS connection:
- Is trusted (authenticated by TLS certificate)
- MAY be cached persistently (with `persist=1`)
- Fallback to original origin is always safe if alternative fails

For HTTP origins, Alt-Svc information:
- Is unauthenticated (vulnerable to MITM)
- MUST NOT be persisted across sessions
- SHOULD be used only in the current session
- Clients SHOULD apply skepticism when alternative points to different host

## Cache Eviction

If client cache is limited, entries are evicted based on:
1. **Freshness:** Expired entries evicted first
2. **LRU (Least Recently Used):** Access patterns
3. **Protocol Priority:** Preferred protocols retained

No specific eviction order is mandated by RFC.
