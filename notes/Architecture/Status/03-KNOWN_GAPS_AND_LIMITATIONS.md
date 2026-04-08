---
title: Known Gaps & Limitations
description: Critical issues, high-priority gaps, and recommended fixes before v1.0 production release
tags: [gaps, limitations, issues, roadmap, critical]
aliases: [KnownGaps, Limitations, Blockers, Issues]
---

# TurboHTTP Known Gaps & Limitations

**Last Updated**: 2026-03-26  
**Severity Levels**: 🔴 Critical, 🟠 High, 🟡 Medium, 🟢 Low

## Critical Gaps (Blocks Production)

### 🔴 1. Server-Side Implementation Missing

**Problem**: Only client-side HTTP client library exists. No server.

**Impact**: Cannot build HTTP server applications with TurboHTTP. No symmetric API.

**Current State**:
- Encoders (serialize HttpRequestMessage) ✅ exist
- Decoders (parse HttpResponseMessage) ✅ exist
- Server request parsing ❌ missing
- Server response encoding ❌ missing

**Solution**: Post-v1.0 roadmap item. Requires:
1. New `/TurboHTTP/Server/` layer with `ITurboHttpServer`
2. Reverse of client pipeline: requests in, responses out
3. ASP.NET Core integration (MapTurboHttpServer middleware)

**Timeline**: Estimated 8-12 weeks after v1.0

---

### 🔴 2. HTTP/3 QPACK Encoder Missing

**Problem**: QPACK decoder exists (RFC 9204), encoder missing. Can't send HTTP/3 requests.

**Impact**: HTTP/3 is write-only (can't write headers to wire format).

**Current State**:
```
RFC 9204 QPACK Implementation:
✅ Decoder (read compressed headers from wire)
❌ Encoder (write headers to wire format)
```

**Missing Code**:
- `QpackEncoder` class (mirrors `HpackEncoder` from RFC 7541)
- `QpackEncoderInstructionStream` for dynamic table updates
- `QpackFieldWriter` for field encoding
- Instruction processing (INSERT_WITH_NAME_REF, INSERT_LITERAL, DUPLICATE)

**Solution**: 
1. Study RFC 9204 §4.1 (encoder algorithm)
2. Implement `QpackEncoder` with synchronized table updates
3. Test against RFC 9204 §C (test vectors)
4. Integrate into `Http30EncoderStage`

**Timeline**: 3-4 weeks estimated

---

### 🔴 3. QUIC Transport Incomplete

**Problem**: Only variable-length integers (RFC 9000 §16) implemented. Missing packet structure, handshake, ACK/loss detection.

**Impact**: No actual QUIC over UDP. Only HTTP/3 frame parsing (which still requires QUIC below).

**Current State**:
```
RFC 9000 QUIC Implementation:
✅ Variable-length integers (QuicVarInt)
❌ Long form packet headers
❌ Packet number encoding
❌ Handshake (Initial/Handshake/Retry packets)
❌ Loss detection + congestion control
❌ Key update (1-RTT)
```

**Missing Code**:
- `QuicPacket` / `QuicPacketHeader` types
- `QuicHandshakeManager` (client TLS handshake integration)
- `QuicLossDetector` / `QuicCongestionController`
- `QuicStream` state machine
- `QuicConnection` manager (Connection ID, migration)

**Why It's Hard**:
- QUIC handshake requires TLS 1.3 integration (Tls13 context)
- Loss detection is stateful and complex (rto, pto, etc.)
- Interop testing requires real servers (Google QUIC, cloudflare, etc.)

**Solution**: 
1. Integrate System.Net.Quic (.NET's native QUIC) as transport
2. OR implement full QUIC from scratch (10+ weeks)

**Recommended**: Use `System.Net.Quic` — already ships with .NET 7+

**Timeline**: 4-6 weeks if using System.Net.Quic, 10+ weeks if from scratch

---

## High-Priority Gaps (Feature Completeness)

### 🟠 1. Header Size/Count DoS Protection

**Problem**: No limits on header size or count. Large responses can OOM client.

**Risk**: Malicious servers can crash client with:
```http
HTTP/1.1 200 OK\r\n
X-Large: [10MB header value]\r\n
X-Count: [10,000 headers]\r\n
```

**Current State**:
- No `MaxHeaderSize` limit
- No `MaxHeaderCount` limit
- No per-header-field size limit

**RFC Guidance**:
- RFC 9110 §5 suggests reasonable limits
- RFC 9113 §6.5.2 recommends 16KB for HTTP/2 header blocks

**Solution**:
```csharp
public class HttpDecoderLimits
{
    public int MaxHeaderSize = 16 * 1024;        // 16KB total
    public int MaxHeaderCount = 100;              // 100 headers max
    public int MaxSingleHeaderSize = 8 * 1024;    // 8KB per header
}
```

**Implementation**:
- Add `HttpDecoderLimits` to decoder constructors
- Throw `HttpDecoderException` if exceeded
- Document sensible defaults

**Timeline**: 2-3 hours

---

### 🟠 2. HTTP/2 MAX_CONCURRENT_STREAMS Client Enforcement

**Problem**: Server sends `SETTINGS_MAX_CONCURRENT_STREAMS`, client ignores it. Can't match server concurrency limits.

**Current State**:
```csharp
// Client receives SETTINGS frame with MAX_CONCURRENT_STREAMS=100
// But then tries to open stream 101, 102, … — no limit enforced!
```

**Impact**: Violates RFC 9113 §5.1.2. Causes server to RST_STREAM when limit exceeded.

**Solution**:
1. Track `MaxConcurrentStreams` from server SETTINGS
2. Maintain `_activeStreamCount` counter
3. Block new stream allocation if limit reached
4. Emit `GOAWAY` if received RST_STREAM with FLOW_CONTROL_ERROR

**Implementation**:
- Extend `Http20StreamIdAllocatorStage` to check limit
- Add backpressure mechanism (queue pending streams)
- Test with Kestrel H2 configured with low limits

**Timeline**: 4-6 hours

---

### 🟠 3. Redirect Loop Detection

**Problem**: Infinite redirect chains (A→B→A→B) crash client with stack overflow or hang indefinitely.

**Current State**:
```csharp
// No tracking of visited URLs
// No max-redirects limit (defaults to HTTP spec)
```

**RFC Guidance**:
- RFC 9110 §15.4 doesn't mandate limits but implies reasonable ones
- HTTP spec typically suggests 5-10 max redirects

**Solution**:
```csharp
public class RedirectPolicy
{
    public int MaxRedirects = 10;                    // Configurable limit
    public TimeSpan RedirectTimeout = TimeSpan.FromSeconds(30);
}

// Track visited URLs in RedirectBidiStage
private readonly HashSet<Uri> _visitedUrls = new();
if (_visitedUrls.Contains(nextUri))
    throw new RedirectException($"Redirect loop detected: {nextUri}");
```

**Implementation**:
- Add `RedirectPolicy` to `TurboClientOptions`
- Extend `RedirectBidiStage` to track visited URLs
- Throw `RedirectException` with loop details

**Timeline**: 3-4 hours

---

### 🟠 4. HTTPS→HTTP Downgrade Protection

**Problem**: Server sends redirect from HTTPS→HTTP. Client follows without warning (security issue).

**RFC Guidance**:
- RFC 9110 §15.4.6 recommends blocking cross-scheme downgrades for security

**Current State**:
```csharp
// No checking of scheme changes
client.SendAsync(new() { RequestUri = new("https://example.com/") })
  // Server redirects to http://example.com/ 
  // Client follows silently — DATA EXPOSED!
```

**Solution**:
```csharp
if (originalRequest.RequestUri.Scheme == "https" && 
    redirectUri.Scheme == "http")
{
    throw new RedirectException("Cannot redirect from HTTPS to HTTP");
}
```

**Implementation**:
- Add check in `RedirectBidiStage`
- Make it configurable: `AllowInsecureRedirects = false` (default: true for compatibility)

**Timeline**: 1-2 hours

---

## Medium-Priority Gaps (RFC Edges)

### 🟡 1. Connection Pooling Per-Host Limits Not Enforced

**Problem**: No documented limit on connections per host. Load tests can exhaust port ranges.

**Current State**:
```csharp
var pool = new ConnectionPool();
// Creates unlimited new connections to example.com
for (int i = 0; i < 10000; i++)
    await pool.AcquireAsync(new("example.com", 80), opts);
```

**Windows Ephemeral Port Exhaustion**:
- Windows has ~16,384 ephemeral ports (49152–65535)
- TIME_WAIT lasts 120 seconds
- Creating 20,000 connections → exhausts ports → EADDRINUSE errors

**Solution**:
```csharp
public class ConnectionPoolOptions
{
    public int MaxConnectionsPerHost = 10;        // HTTP spec default
    public int MaxTotalConnections = 100;          // Global limit
    public TimeSpan IdleConnectionTimeout = TimeSpan.FromSeconds(60);
}
```

**Implementation**:
- Document `HostConnections._limiter: SemaphoreSlim` semantics
- Add configurable limits to `ConnectionPool`
- Test with BenchmarkDotNet to validate

**Timeline**: 4-6 hours

---

### 🟡 2. Trailer Headers Not Supported (HTTP/1.1)

**Problem**: RFC 9112 §6.1 defines trailer headers (headers after body chunks), but decoder ignores them.

**Severity**: 🟢 Low — rarely used in practice (mostly for signing, checksums)

**Current State**:
```http
POST / HTTP/1.1
Transfer-Encoding: chunked

5\r\n
Hello\r\n
0\r\n
X-Checksum: abc123\r\n   ← Trailer (not parsed)
\r\n
```

**Solution**:
1. Extend `Http11DecoderPipeline` to parse trailer lines after `0\r\n`
2. Add `TrailerHeaders` to `HttpResponseMessage` (or `HttpContent.TrailingHeaders`)
3. Test with RFC compliance vectors

**Timeline**: 6-8 hours

---

### 🟡 3. Chunk Extensions Not Parsed (HTTP/1.1)

**Problem**: RFC 9112 §6.1 allows extensions after chunk size, but decoder skips them.

**Severity**: 🟢 Low — rarely used (reserved for future extensions)

**Current State**:
```http
HTTP/1.1 200 OK
Transfer-Encoding: chunked

5;ext=val\r\n   ← Extension `;ext=val` ignored
Hello\r\n
0\r\n
\r\n
```

**RFC Example**: `5e3;name=value\r\n` (chunk size in hex with name-value pair)

**Solution**:
1. Extend `Http11DecoderPipeline` to parse and validate extensions
2. Store in `ChunkExtensions` (or log and discard)
3. Test with RFC test vectors

**Timeline**: 4-6 hours

---

### 🟡 4. Public Suffix Cookies Not Enforced (RFC 6265)

**Problem**: Cookies for bare domains (e.g., `example.com` vs `sub.example.com`) not validated against public suffix list.

**Severity**: 🟢 Low — affects multi-tenant domains (e.g., github.io pages)

**Current State**:
```csharp
var jar = new CookieJar();
// Server: Set-Cookie: id=123; Domain=.github.io
// → Creates cookie for ALL github.io subdomains!
```

**RFC Guidance**: RFC 6265 §5.3 recommends consulting public suffix list

**Solution**:
1. Embed Mozilla public suffix list (or load from https://publicsuffix.org/list/)
2. Check domain against list before setting cookies
3. Reject cookies for bare public domains

**Timeline**: 4-6 hours (mostly data management)

---

### 🟡 5. Server Push (HTTP/2) Minimally Implemented

**Problem**: Clients receive PUSH_PROMISE frames but don't handle promised streams correctly.

**Severity**: 🟡 Medium — server push rarely used (only ~2-3% of production HTTP/2)

**Current State**:
```csharp
// Server: PUSH_PROMISE for /styles.css
// Client: Receives frame but doesn't validate promised stream
```

**RFC Requirement**: RFC 9113 §6.6 requires validating push promise constraints

**Solution**:
1. Extend `Http20ConnectionStage` to validate PUSH_PROMISE
2. Create promised stream in reserved state
3. Allow server to send DATA on promised stream
4. Let client reject with RST_STREAM if not interested

**Timeline**: 8-10 hours

---

## Low-Priority Gaps (Advanced Features)

### 🟢 1. QUIC Connection Migration (RFC 9000 §9)

**Severity**: 🟢 Low — needed for mobile clients, not typical desktop/server use

**Problem**: No support for changing IP/port mid-connection (happens on mobile network switch)

**Solution**: Post-v1.0, requires `System.Net.Quic` integration

**Timeline**: 2-3 weeks

---

### 🟢 2. Alternative Service (Alt-Svc) Header

**Severity**: 🟢 Low — rarely used (mostly CDNs)

**Problem**: Ignore Alt-Svc header that advertises HTTP/3 upgrade

**Solution**: Parse header, track alternative endpoints, test on next request

**Timeline**: 3-4 hours

---

### 🟢 3. Proxy Support (Proxy-Authorization, CONNECT)

**Severity**: 🟢 Low — enterprise-only, not in v1.0 roadmap

**Problem**: No support for HTTP proxy tunneling (CONNECT method)

**Solution**: Post-v1.0 roadmap item

**Timeline**: 4-5 weeks

---

## Mitigations (Workarounds)

| Gap | Workaround |
|-----|-----------|
| Server implementation missing | Use Kestrel for now; switch after v1.0 |
| HTTP/3 encoder missing | Stick with HTTP/1.1/2 for now; wait for HTTP/3 release |
| DoS protection | Implement own limits in `HttpMessageHandler` wrapping |
| Redirect loops | Wrap client with retry policy that tracks URLs |
| QUIC transport | Use `System.Net.Quic` as underlying transport (if available) |
| Trailer headers | Configure servers not to send trailers (most don't) |
| Chunk extensions | Ignore (not used in practice) |
| Public suffix cookies | Use own cookie policy layer above `CookieJar` |
| Server push | Disable with SETTINGS_ENABLE_PUSH = 0 |

---

## Testing Gaps

| Component | Unit Tests | Integration Tests | Compliance Tests |
|-----------|-----------|-------------------|-----------------|
| HTTP/1.0 | ✅ 233 | ✅ 15 | ✅ Complete |
| HTTP/1.1 | ✅ 374 | ✅ 45 | ✅ Complete |
| HTTP/2 | ✅ 545 | ✅ 60 | ✅ 85% |
| HTTP/3 | 🟡 < 50 | ❌ 0 | ❌ 0% |
| HPACK | ✅ 419 | ✅ 10 | ✅ 100% |
| QPACK | 🟡 < 50 | ❌ 0 | ❌ 0% |
| Caching | ✅ 75 | ✅ 20 | ✅ 80% |
| Cookies | ✅ 66 | ✅ 15 | ✅ 85% |

---

## Recommended Fixes Before v1.0

**Priority 1** (MUST):
- [ ] DoS protection (header size/count limits) — 2-3 hours
- [ ] QPACK encoder — 3-4 weeks
- [ ] Expand RFC9110 tests — 1 week

**Priority 2** (SHOULD):
- [ ] Redirect loop detection — 3-4 hours
- [ ] HTTPS→HTTP protection — 1-2 hours
- [ ] MAX_CONCURRENT_STREAMS enforcement — 4-6 hours

**Priority 3** (NICE-TO-HAVE):
- [ ] Trailer headers support — 6-8 hours
- [ ] Chunk extensions parsing — 4-6 hours
- [ ] Public suffix cookies — 4-6 hours

**Total Estimated Time**: 4-6 weeks for Priority 1+2, additional 1-2 weeks for Priority 3
