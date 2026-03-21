# RFC Compliance

TurboHttp implementiert Client-seitiges HTTP basierend auf sechs Core-RFCs. **~1835 Tests** verifizieren die Compliance.

## Unterstützte RFCs

| RFC | Titel | Tests |
|-----|-------|-------|
| [RFC 1945](https://www.rfc-editor.org/rfc/rfc1945) | HTTP/1.0 | ~233 |
| [RFC 9112](https://www.rfc-editor.org/rfc/rfc9112) | HTTP/1.1 Message Syntax | ~374 |
| [RFC 9113](https://www.rfc-editor.org/rfc/rfc9113) | HTTP/2 | ~545 |
| [RFC 7541](https://www.rfc-editor.org/rfc/rfc7541) | HPACK | ~419 |
| [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110) | HTTP Semantics | ~123 |
| [RFC 9111](https://www.rfc-editor.org/rfc/rfc9111) | HTTP Caching | ~75 |
| [RFC 6265](https://www.rfc-editor.org/rfc/rfc6265) | Cookies | ~66 |

## Compliance Score: ~86/100

| Bereich | Score | Highlights |
|---------|-------|-----------|
| HTTP/1.0 Encoding | 85 | Vollständig — minor: Streaming-Body |
| HTTP/1.1 Encoding | 92 | Chunked, Host, Keep-Alive, Pipelining |
| HTTP/2 Framing | 90 | Alle 10 Frame-Typen, Flow Control |
| HPACK | 95 | Am gründlichsten getestet (419 Tests) |
| Redirects | 95 | Volle RFC-Coverage inkl. Security |
| Retries | 90 | Idempotency-basiert |
| Caching | 85 | Freshness, Validation, Vary |
| Cookies | 85 | Domain/Path, Attributes, Expiration |

## Bekannte Lücken

1. HTTP/1.0 Streaming-Body-Encoding (große Bodies ohne Content-Length)
2. HTTP/1.1 Trailer-Generierung (Parsing ist komplett)
3. HTTP/2 Server Push Rejection (PUSH_PROMISE wird geparst aber nicht proaktiv abgelehnt)
4. HTTP/2 Stream Priority Scheduling (geparst, nicht für Scheduling verwendet)
5. RFC 9111 stale-while-revalidate / stale-if-error Extensions

Detaillierte Matrix: `RFC_COVERAGE.md` im Repository-Root.
