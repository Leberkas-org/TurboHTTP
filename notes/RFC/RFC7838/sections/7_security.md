---
rfc_section: '7'
---
# RFC7838 §7 – Security Considerations

## HTTPS vs HTTP Origins

### HTTPS Origins (Trusted)

For origins served over HTTPS:
- Alt-Svc information is authenticated by TLS certificate
- Clients MAY cache entries persistently (`persist=1`)
- Server is trusted to advertise legitimate alternatives
- Certificate validation (CN, SAN, validity) applies to alternative endpoints

**Safe behaviors:**
- Cache across sessions
- Use alternative with high confidence
- Fallback to origin is safe if alternative fails

### HTTP Origins (Untrusted)

For origins served over plain HTTP:
- Alt-Svc information is **unauthenticated** (vulnerable to MITM)
- Clients MUST NOT persist entries across sessions
- Clients MUST NOT upgrade to HTTPS alternatives without user consent
- Clients MUST apply skepticism when alternative points to different host

**Required behaviors:**
- Session-only caching only
- Validate HTTPS alternatives with extreme caution
- Log or warn when HTTP serves Alt-Svc
- Consider browser cache-clearing in user settings

**Example attack:**
```
HTTP response from attacker-controlled MITM:
Alt-Svc: h3="attacker.com:443"
```
Client follows, connects to attacker's HTTPS service. Persistent caching across sessions would increase attack window.

## Host Mismatch Risks

When Alt-Svc points to a different host:

```
Alt-Svc: h2="cdn.example.com:443"
```

Clients MUST:

1. **Verify Certificate:** Alternative host MUST be covered by a valid certificate
   - Check SubjectAlternativeName (SAN) includes the alternative host
   - Enforce hostname matching rules (RFC 6125)

2. **Mutual TLS Trust:** Both origin and alternative MUST have valid, trusted certificates
   - Both certificates MUST chain to trusted root
   - Both MUST be valid (not expired, not revoked)

3. **No Cross-Organization Redirects:** Clients SHOULD NOT silently upgrade to alternatives on different organizations' certificates
   - Example: `https://bank.com` advertising `h2="attacker.org"` is fraud
   - User MUST see warning or explicit acceptance

## Connection Hijacking

Alt-Svc allows origin to redirect traffic to another endpoint. Risks:

### Same-Origin Redirects (Low Risk)

```
Alt-Svc: h3="example.com:443"  // still example.com, trusted cert
```

- Relatively safe
- Certificate still covers the destination
- Server operator controls both endpoints

### Third-Party Service Redirects (High Risk)

```
Alt-Svc: h2="cdn.partner.com:443"  // different host
```

- Origin delegates service to third party
- CDN/partner certificate issued to different organization
- Client MUST validate certificate matches the alternative host
- TLS protects against hijacking if certificates are validated correctly

## Authentication & Authorization Bypass

Alt-Svc MUST NOT be used to bypass origin authentication.

**Examples of misuse:**
```
// WRONG: Should not happen
HTTP response from http://user:password@proxy.example.com/secret
Alt-Svc: h2="attacker.com:443"  // Different authority

// CORRECT: Must validate cert, requires user action
HTTPS response from https://user:password@example.com/secret
Alt-Svc: h2="cdn.example.com:443"  // Same origin, valid cert
```

Clients MUST:
- Maintain HTTP Basic Authentication credentials only for origin's domain
- NOT forward credentials to alternative endpoints without explicit user consent
- NOT reuse origin's session cookies on alternative endpoints unless same-domain

## Protocol Downgrade

Care must be taken when upgrading/downgrading protocols.

### HTTP → HTTPS (Upgrade)

```
HTTP response:
Alt-Svc: h3="example.com:443"  // upgrade to TLS
```

- HTTP response is unauthenticated
- Client SHOULD NOT silently upgrade to HTTPS without user interaction
- Attacker could intercept and inject fake Alt-Svc header

**Recommended client behavior:**
- Log or warn about HTTP → HTTPS upgrade offers
- Cache session-only, if at all
- User can explicitly opt-in to use alternative

### HTTPS → h2c (Downgrade)

```
HTTPS response:
Alt-Svc: h2c="example.com:80"  // downgrade to cleartext
```

- Server is offering to downgrade to unencrypted protocol
- Client SHOULD reject or warn user (security regression)
- h2c is appropriate only for same-origin, trusted scenarios

**Recommended client behavior:**
- Clients SHOULD reject h2c alternatives unless explicitly configured
- Log warning if received

## Origin-to-Origin Leaking

Alt-Svc information is **origin-specific** and MUST NOT leak between origins.

**Wrong behavior:**
```
Client receives Alt-Svc from origin A:
Alt-Svc: h3="cdn.com:443"

Client should NOT apply this to origin B, even if both use cdn.com
```

Clients MUST:
- Cache Alt-Svc entries keyed by (scheme, host, port) origin
- Apply only to requests to that specific origin
- NOT infer Alt-Svc for one origin based on another's advertisements

## Denial of Service (DoS)

### Cache Pollution

```
Response from attacker-controlled endpoint:
Alt-Svc: h3="attacker.com:443"; ma=31536000; persist=1
```

Risks:
- Large `ma` value could keep malicious entry cached for a year
- Persistent caching across client restarts amplifies exposure

**Mitigations:**
- Clients MAY cap maximum cacheable `ma` value (e.g., 7 days)
- Clients MAY implement per-origin cache entry limits
- Clients MAY ignore `persist` parameter for non-HTTPS origins
- Security software MAY clear Alt-Svc cache periodically

### Connection Exhaustion

Malicious server could advertise many alternatives:

```
Alt-Svc: h2=":443", h3=":443", h2c=":80", h3=":8000", h3=":8001", ...
```

**Mitigations:**
- Clients SHOULD limit number of alternative services cached per origin
- Clients SHOULD implement connection pool limits per origin
- Clients MAY ignore Alt-Svc entries after threshold

## Man-in-the-Middle (MITM) Considerations

### HTTP Transport (Vulnerable)

```
Attacker intercepts HTTP response:
Alt-Svc: h2="attacker.com:443"
```

**Defense mechanisms:**
- TLS certificate validation (alternative's cert must be trusted)
- Session-only caching (no persistence)
- User warnings (log suspicious redirects)
- HSTS (if origin enforces HTTPS)

### HTTPS Transport (Protected)

```
Attacker cannot modify TLS-protected response
Alt-Svc information is authenticated
```

**But risks remain:**
- Alternative endpoint certificate validity
- Third-party service trust model
- Persistent caching scope

## Recommendations for Implementations

1. **Validate all certificates:** Alternative endpoints MUST pass TLS validation
2. **Limit cache scope:** Per-origin, per-protocol, honor `ma` expiration
3. **Skepticism on HTTP:** Do not persist, do not upgrade to HTTPS automatically
4. **User warnings:** Log or expose unusual Alt-Svc behaviors (cross-org, long TTL)
5. **Rate limiting:** Do not immediately try every advertised alternative (connection exhaustion)
6. **Clear semantics:** Document how client handles mismatches, failures, downgrades
