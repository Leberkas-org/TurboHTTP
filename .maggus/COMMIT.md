TASK-021-006: TLS Uniform Coverage Part 1 (Compression + Cookie + Redirect + Retry)

Add four dedicated TLS integration test classes mirroring the HTTP/1.1 feature test classes
over HTTPS transport. All 41 new tests pass; build is zero-warning.

- TLS/CompressionIntegrationTests: 7 tests (gzip, deflate, brotli, identity, negotiate)
- TLS/CookieIntegrationTests: 11 tests (set/echo, Secure over HTTPS, HttpOnly, SameSite,
  Max-Age expiry, domain/path scoping, multi-cookie, delete, set-and-redirect)
- TLS/RedirectIntegrationTests: 14 tests (301-308, chains, loop, relative URL, method
  preservation, HTTPS→HTTP downgrade blocking for cross-scheme and cross-origin routes)
- TLS/RetryIntegrationTests: 9 tests (408/503 retries, Retry-After seconds and HTTP-date,
  succeed-after-N, idempotent PUT/DELETE, non-idempotent POST)
