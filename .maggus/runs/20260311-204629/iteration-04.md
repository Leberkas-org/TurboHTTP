# Iteration 04 — TASK-023: Http10Engine Content Encoding Integration Tests

## Task
**TASK-023**: Add integration tests for Http10Engine content encoding (gzip, deflate, identity passthrough, Content-Encoding removal, Content-Length update).

## Commands Run

1. `dotnet build src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — **success** (0 errors)
2. `dotnet test --filter "FullyQualifiedName~Http10ContentEncodingTests"` — first run: **2 failures** (CE-INT-004, CE-INT-005)
   - Root cause: Http10Decoder already decompresses body, removes Content-Encoding, and updates Content-Length internally. Initial test design tried to apply ContentEncodingDecoder manually on top, but the engine already handles it.
3. Rewrote tests to verify the decoder's built-in decompression behavior.
4. `dotnet test --filter "FullyQualifiedName~Http10ContentEncodingTests"` — **5/5 passed**

## Key Finding
The `Http10Decoder` (lines 235-282) already performs transparent decompression:
- Calls `ContentEncodingDecoder.Decompress()` when Content-Encoding is present
- Strips Content-Encoding header from the response
- Updates Content-Length to decompressed body size

This means integration tests verify end-to-end behavior without needing a separate decompression step.

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http10/06_Http10ContentEncodingTests.cs`
- [x] 5 tests: gzip decompressed, deflate decompressed, identity passthrough, Content-Encoding removed, Content-Length updated
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http10ContentEncodingTests"`

## Deviations
None.
