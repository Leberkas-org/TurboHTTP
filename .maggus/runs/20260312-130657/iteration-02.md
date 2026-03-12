# Iteration 02 — TASK-046: TLS Integration Tests

## Task
TASK-046: TLS Integration Tests

## Commands Run
1. `dotnet build src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — success (0 errors)
2. `dotnet test --filter "FullyQualifiedName~TlsTests"` — initially 5 failures due to KestrelTlsFixture bug
3. Fixed `KestrelTlsFixture.CreateSelfSignedCertificate()`: `LoadCertificate` → `LoadPkcs12` (PFX contains private key)
4. `dotnet test --filter "FullyQualifiedName~TlsTests"` — 5/5 passed
5. `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — 224/226 passed (2 pre-existing failures: RETRY-INT-001, COOKIE-INT-006)

## Files Changed
- **Created:** `src/TurboHttp.IntegrationTests/Shared/04_TlsTests.cs` — 5 TLS integration tests
- **Modified:** `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs` — fix cert loading
- **Modified:** `.maggus/plan_2.md` — marked TASK-046 acceptance criteria as complete

## Deviations
None. All 4 acceptance criteria met.

## Notes
- KestrelTlsFixture had a bug: `X509CertificateLoader.LoadCertificate()` can't load PFX (which contains a private key). Changed to `LoadPkcs12()` which correctly handles PFX format needed for Kestrel HTTPS.
- Tests 1, 3, 4 make real TLS connections to KestrelTlsFixture via the Http11Engine pipeline with TlsOptions.
- Tests 2 and 5 test protocol handler logic (CookieJar Secure attribute, RedirectHandler downgrade blocking) without network I/O.
- 2 pre-existing integration test failures remain (RETRY-INT-001, COOKIE-INT-006 — both 10s timeouts).
