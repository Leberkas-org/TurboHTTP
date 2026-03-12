# Iteration 02 — TASK-041: Http20Engine Content Encoding Integration Tests

## Task
TASK-041: Add integration tests for Http20Engine content encoding (gzip, deflate, brotli, large 500KB).

## Commands Run
1. `dotnet test --filter "FullyQualifiedName~Http20ContentEncodingTests"` → **4/4 passed**

## Files Created
- `src/TurboHttp.IntegrationTests/Http20/10_Http20ContentEncodingTests.cs` — 4 tests (20E-INT-051 through 20E-INT-054)

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http20/10_Http20ContentEncodingTests.cs`
- [x] 4 tests: gzip, deflate, brotli, large (500KB)
- [x] All tests pass

## Notes
- Followed same pipeline construction pattern as Http20CacheTests (manual graph with StreamIdAllocatorStage, Request2FrameStage, Http20ConnectionStage, Http20EncoderStage, Http20DecoderStage, Http20StreamStage)
- Http20StreamStage handles content-encoding decompression natively for HTTP/2
- KestrelH2Fixture already registers content encoding routes via `Routes.RegisterContentEncodingRoutes(app)`
- Test IDs continue from cache tests: 20E-INT-051 through 20E-INT-054
