# TEST_MATRIX - TurboHttp Complete Test Inventory

Generated: 2026-03-10

**Total test methods: 2339**

## Summary

| Project | Tests |
|---------|-------|
| TurboHttp.Tests (Unit Tests) | 1833 |
| TurboHttp.StreamTests (Stream Tests) | 127 |
| TurboHttp.IntegrationTests (Integration Tests) | 379 |
| **Total** | **2339** |

---

## TurboHttp.Tests (Unit Tests) - 1833 tests

### Integration (309 tests)

#### `RedirectHandlerTests.cs` - `RedirectHandlerTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `IsRedirect_Returns_True_For_Redirect_Status_Codes` | Theory | RH-001: IsRedirect returns true for redirect status codes |
| 2 | `IsRedirect_Returns_False_For_Non_Redirect_Status_Codes` | Theory | RH-002: IsRedirect returns false for non-redirect status codes |
| 3 | `SeeOther_303_Rewrites_Post_To_Get` | Fact | RH-003: 303 rewrites POST to GET and drops body |
| 4 | `SeeOther_303_Rewrites_Put_To_Get` | Fact | RH-004: 303 rewrites PUT to GET and drops body |
| 5 | `SeeOther_303_Rewrites_Delete_To_Get` | Fact | RH-005: 303 rewrites DELETE to GET |
| 6 | `TemporaryRedirect_307_Preserves_Post_Method_And_Body` | Fact | RH-006: 307 preserves POST method and body |
| 7 | `TemporaryRedirect_307_Preserves_Put_Method_And_Body` | Fact | RH-007: 307 preserves PUT method and body |
| 8 | `TemporaryRedirect_307_Preserves_Delete_Method` | Fact | RH-008: 307 preserves DELETE method |
| 9 | `PermanentRedirect_308_Preserves_Post_Method_And_Body` | Fact | RH-009: 308 preserves POST method and body |
| 10 | `PermanentRedirect_308_Preserves_Patch_Method_And_Body` | Fact | RH-010: 308 preserves PATCH method and body |
| 11 | `MovedPermanently_302_Rewrites_Post_To_Get` | Theory | RH-011: 301/302 rewrites POST to GET (historical behavior) |
| 12 | `MovedPermanently_302_Preserves_Get_Method` | Theory | RH-012: 301/302 preserves GET method |
| 13 | `MovedPermanently_302_Preserves_Head_Method` | Theory | RH-013: 301/302 preserves HEAD method |
| 14 | `BuildRedirectRequest_Uses_Absolute_Location` | Fact | RH-014: Absolute Location URI used as-is |
| 15 | `BuildRedirectRequest_Resolves_Relative_Location` | Fact | RH-015: Relative Location URI resolved against request URI |
| 16 | `BuildRedirectRequest_Resolves_Relative_Path_Location` | Fact | RH-016: Relative path Location URI resolved correctly |
| 17 | `BuildRedirectRequest_Throws_When_Max_Redirects_Exceeded` | Fact | RH-017: Throws RedirectException when max redirects exceeded |
| 18 | `BuildRedirectRequest_Throws_When_Default_Max_Redirects_Exceeded` | Fact | RH-018: Throws RedirectException after default max 10 redirects |
| 19 | `RedirectCount_Tracks_Number_Of_Redirects` | Fact | RH-019: RedirectCount tracks number of redirects |
| 20 | `BuildRedirectRequest_Throws_On_Direct_Loop` | Fact | RH-020: Throws RedirectException on direct redirect loop |
| 21 | `BuildRedirectRequest_Throws_On_Self_Redirect` | Fact | RH-021: Throws RedirectException on self-redirect (A → A) |
| 22 | `BuildRedirectRequest_Throws_When_Location_Header_Missing` | Fact | RH-022: Throws RedirectException when Location header is missing |
| 23 | `PermanentRedirect_308_Preserves_Get_Method` | Fact | RH-023: 308 preserves GET method (no body rewrite) |
| 24 | `BuildRedirectRequest_Strips_Authorization_On_Cross_Origin` | Fact | RH-024: Strips Authorization header on cross-origin redirect |
| 25 | `BuildRedirectRequest_Preserves_Authorization_On_Same_Origin` | Fact | RH-025: Preserves Authorization header on same-origin redirect |
| 26 | `BuildRedirectRequest_Strips_Authorization_When_Scheme_Changes` | Fact | RH-026: Strips Authorization header when scheme changes (HTTPS→HTTP) |
| 27 | `BuildRedirectRequest_Strips_Authorization_When_Port_Changes` | Fact | RH-027: Strips Authorization header when port changes |
| 28 | `BuildRedirectRequest_Throws_On_Https_To_Http_Downgrade` | Fact | RH-028: Throws RedirectDowngradeException on HTTPS to HTTP redirect |
| 29 | `BuildRedirectRequest_Allows_Downgrade_When_Policy_Permits` | Fact | RH-029: Allows HTTPS to HTTP downgrade when policy permits |
| 30 | `BuildRedirectRequest_Allows_Http_To_Https_Upgrade` | Fact | RH-030: Allows HTTP to HTTPS upgrade (no downgrade block) |
| 31 | `Reset_Clears_Redirect_Count_And_History` | Fact | RH-031: Reset clears redirect count and history |
| 32 | `Reset_Allows_Previously_Visited_Uri_After_Reset` | Fact | RH-032: Reset allows previously visited URI to be visited again |
| 33 | `BuildRedirectRequest_Copies_Non_Sensitive_Headers` | Fact | RH-033: Non-sensitive headers are copied on redirect |
| 34 | `BuildRedirectRequest_Does_Not_Copy_Host_Header` | Fact | RH-034: Host header is not blindly copied on redirect |
| 35 | `Default_Policy_Has_MaxRedirects_10` | Fact | RH-035: Default policy has MaxRedirects = 10 |
| 36 | `Default_Policy_Does_Not_Allow_Downgrade` | Fact | RH-036: Default policy does not allow HTTPS to HTTP downgrade |
| 37 | `IsRedirect_Throws_For_Null_Response` | Fact | RH-037: IsRedirect throws ArgumentNullException for null response |
| 38 | `BuildRedirectRequest_Throws_For_Null_Original` | Fact | RH-038: BuildRedirectRequest throws ArgumentNullException for null original |
| 39 | `BuildRedirectRequest_Throws_For_Null_Response` | Fact | RH-039: BuildRedirectRequest throws ArgumentNullException for null response |
| 40 | `BuildRedirectRequest_Strips_Cookie_Header` | Fact | RH-040: Cookie header is stripped when building redirect request |
| 41 | `BuildRedirectRequest_WithJar_ReappliesCookies_SameOrigin` | Fact | RH-041: With CookieJar, cookies re-applied for same-origin redirect |
| 42 | `BuildRedirectRequest_WithJar_DoesNotReapplyCookies_CrossOrigin` | Fact | RH-042: With CookieJar, cookies NOT re-applied for different domain |
| 43 | `BuildRedirectRequest_WithJar_ProcessesSetCookieFromRedirectResponse` | Fact | RH-043: With CookieJar, Set-Cookie from redirect response is processed |
| 44 | `BuildRedirectRequest_WithJar_SetCookieAppliedToRedirectRequest` | Fact | RH-044: With CookieJar, Set-Cookie from redirect applied to next hop |
| 45 | `BuildRedirectRequest_WithJar_SecureCookiesOnlySentToHttps` | Fact | RH-045: With CookieJar, Secure cookies only sent to HTTPS redirect |
| 46 | `BuildRedirectRequest_WithJar_SecureCookiesSentOverHttps` | Fact | RH-046: With CookieJar, Secure cookies sent when redirect stays on HTTPS |
| 47 | `BuildRedirectRequest_WithJar_PathRestrictedCookieNotSentForNonMatchingPath` | Fact | RH-047: With CookieJar, path-restricted cookie not sent for non-matching path |
| 48 | `BuildRedirectRequest_WithJar_PathRestrictedCookieSentForMatchingPath` | Fact | RH-048: With CookieJar, path-restricted cookie sent for matching path |
| 49 | `BuildRedirectRequest_WithJar_Throws_For_Null_CookieJar` | Fact | RH-049: BuildRedirectRequest(jar) throws ArgumentNullException for null jar |
| 50 | `BuildRedirectRequest_WithEmptyJar_NosCookieHeader` | Fact | RH-050: With empty CookieJar, no Cookie header added to redirect |
| 51 | `BuildRedirectRequest_WithJar_DomainCookieReappliedForSubdomainRedirect` | Fact | RH-051: Domain cookie re-evaluated for subdomain redirect |

#### `Phase60ValidationGateTests.cs` - `Phase60ValidationGateTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_EncodeMethod_When_AnyStandardMethod` | Theory | P60-9110-001: HTTP/1.1 encoder handles all standard request methods |
| 2 | `Should_EmitMethodPseudoHeader_When_AnyStandardMethodHttp2` | Theory | P60-9110-002: HTTP/2 encoder emits :method pseudo-header for all standard methods |
| 3 | `Should_DecodeCorrectStatusCode_When_2xxResponse` | Theory | P60-9110-003: 2xx status codes decoded with correct StatusCode value |
| 4 | `Should_DecodeCorrectStatusCode_When_3xxResponse` | Theory | P60-9110-004: 3xx status codes decoded correctly |
| 5 | `Should_DecodeCorrectStatusCode_When_4xxResponse` | Theory | P60-9110-005: 4xx status codes decoded correctly |
| 6 | `Should_DecodeCorrectStatusCode_When_5xxResponse` | Theory | P60-9110-006: 5xx status codes decoded correctly |
| 7 | `Should_AcceptHeaders_When_MixedCaseHeaderNames` | Fact | P60-9110-007: RFC 9110 §5.1 — header names are case-insensitive (mixed case accepted) |
| 8 | `Should_AcceptHeaders_When_LowercaseContentLength` | Fact | P60-9110-008: RFC 9110 §5.1 — lowercase Content-Length accepted |
| 9 | `Should_AcceptHeaders_When_TransferEncodingCaseVariants` | Fact | P60-9110-009: RFC 9110 §5.1 — Transfer-Encoding case variants accepted |
| 10 | `Should_RejectResponse_When_BothTransferEncodingAndContentLengthPresent` | Fact | P60-9110-010: RFC 9112 §6.3 — Transfer-Encoding + Content-Length both present is rejected |
| 11 | `Should_RejectResponse_When_MultipleContentLengthValues` | Fact | P60-9110-011: RFC 9112 §6.3 — Multiple differing Content-Length values rejected |
| 12 | `Should_DecodeExactBody_When_ContentLengthFraming` | Fact | P60-9110-012: RFC 9110 §6.3 — body decoded correctly for Content-Length framing |
| 13 | `Should_DecodeFullBody_When_ChunkedFraming` | Fact | P60-9110-013: RFC 9110 §6.3 — body decoded correctly for chunked framing |
| 14 | `Should_DecodeEmptyBody_When_ZeroContentLength` | Fact | P60-9110-014: RFC 9110 §6.3 — zero-length body for Content-Length: 0 |
| 15 | `Should_SkipInterim100_When_100ThenFinalResponse` | Fact | P60-9110-015: RFC 9110 §15.2 — 100 Continue before 200 OK skips interim |
| 16 | `Should_Skip1xx_When_InterimBeforeFinalResponse` | Theory | P60-9110-016: RFC 9110 §15.2 — all 1xx codes skipped before final response |
| 17 | `Should_Return_NeedMoreData_When_Only1xxPresent` | Fact | P60-9110-017: RFC 9110 §15.2 — 1xx is NOT treated as a final response |
| 18 | `Should_HaveEmptyBody_When_204NoContent` | Fact | P60-9110-018: RFC 9110 §15.3.4 — 204 No Content has empty body |
| 19 | `Should_HaveEmptyBody_When_204WithContentLength` | Fact | P60-9110-019: RFC 9110 §15.3.4 — 204 ignores Content-Length body |
| 20 | `Should_HaveEmptyBody_When_304NotModified` | Fact | P60-9110-020: RFC 9110 §15.4.5 — 304 Not Modified has empty body |
| 21 | `Should_ReturnHeadersOnly_When_HeadResponseDecoded` | Fact | P60-9110-021: RFC 9110 §9.3.2 — TryDecodeHead returns headers without body |
| 22 | `Should_ReturnNoBody_When_HeadReturns404` | Fact | P60-9110-022: RFC 9110 §9.3.2 — TryDecodeHead with 404 returns no body |
| 23 | `Should_DecodeSingleByteChunks_When_ChunkedEncoding` | Fact | P60-9112-001: RFC 9112 §7.1 — single-byte chunks decoded correctly |
| 24 | `Should_DecodeUppercaseHexChunkSize_When_ChunkedEncoding` | Fact | P60-9112-002: RFC 9112 §7.1 — uppercase hex chunk sizes accepted |
| 25 | `Should_IgnoreChunkExtension_When_NameOnlyExtension` | Fact | P60-9112-003: RFC 9112 §7.1.1 — chunk extension with name safely ignored |
| 26 | `Should_IgnoreChunkExtension_When_NameValueExtension` | Fact | P60-9112-004: RFC 9112 §7.1.1 — chunk extension with name=value safely ignored |
| 27 | `Should_IgnoreMultipleChunkExtensions_When_SemicolonSeparated` | Fact | P60-9112-005: RFC 9112 §7.1.1 — multiple chunk extensions safely ignored |
| 28 | `Should_IgnoreChunkExtension_When_QuotedValue` | Fact | P60-9112-006: RFC 9112 §7.1.1 — chunk extension with quoted value safely ignored |
| 29 | `Should_AccessTrailer_When_TrailerAfterFinalChunk` | Fact | P60-9112-007: RFC 9112 §7.1.2 — trailer headers after final chunk accessible |
| 30 | `Should_AccessMultipleTrailers_When_MultipleTrailerFields` | Fact | P60-9112-008: RFC 9112 §7.1.2 — multiple trailer fields all accessible |
| 31 | `Should_DecodeChunked_When_NoTrailers` | Fact | P60-9112-009: RFC 9112 §7.1.2 — chunked body without trailers decoded correctly |
| 32 | `Should_TreatNegativeContentLengthAsZero_When_NegativeContentLength` | Fact | P60-9112-010: RFC 9112 §6.3 — negative Content-Length treated as zero (graceful) |
| 33 | `Should_ReturnNeedMoreData_When_ContentLengthExceedsBody` | Fact | P60-9112-011: RFC 9112 §6.3 — Content-Length larger than actual body is NeedMoreData |
| 34 | `Should_RejectResponse_When_BothTEAndCL` | Fact | P60-9112-012: RFC 9112 §6.3 — Transfer-Encoding + Content-Length in either order rejected |
| 35 | `Should_CombineMultipleSameNameHeaders_When_HeadersAppearMultipleTimes` | Fact | P60-9110-023: RFC 9110 §5.3 — multiple same-name headers combined with comma |
| 36 | `Should_HaveEmptyBody_When_NoBodyStatusCode` | Theory | P60-9110-024: RFC 9110 §6.4 — no-body status codes have empty body |
| 37 | `Should_DecodeLargeBody_When_LargeChunkedResponse` | Fact | P60-9112-013: RFC 9112 §7.1 — 32KB chunked body decoded correctly |
| 38 | `Should_RejectResponse_When_NonHexChunkSize` | Fact | P60-9112-014: RFC 9112 §7.1 — non-hex chunk size is a parse error |

#### `TcpFragmentationTests.cs` - `TcpFragmentationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_BufferAndComplete_When_Http10StatusLineSplitAtByte1` | Fact | FRAG-10-001: HTTP/1.0 status-line split at byte 1 |
| 2 | `Should_BufferAndComplete_When_Http10StatusLineSplitMidVersion` | Fact | FRAG-10-002: HTTP/1.0 status-line split mid-version |
| 3 | `Should_BufferAndComplete_When_Http10HeaderNameSplitMidWord` | Fact | FRAG-10-003: HTTP/1.0 header name split mid-word |
| 4 | `Should_BufferAndComplete_When_Http10BodySplitAtFirstByte` | Fact | FRAG-10-004: HTTP/1.0 body split at first byte |
| 5 | `Should_BufferAndComplete_When_Http10BodySplitAtMidpoint` | Fact | FRAG-10-005: HTTP/1.0 body split at midpoint |
| 6 | `Should_BufferAndComplete_When_Http11StatusLineSplitAtByte1` | Fact | FRAG-11-001: HTTP/1.1 status-line split at byte 1 |
| 7 | `Should_BufferAndComplete_When_Http11StatusLineSplitMidVersion` | Fact | FRAG-11-002: HTTP/1.1 status-line split inside version |
| 8 | `Should_BufferAndComplete_When_Http11HeaderSplitAtColon` | Fact | FRAG-11-003: HTTP/1.1 header split at colon |
| 9 | `Should_BufferAndComplete_When_Http11SplitAtFirstByteOfCrlfCrlf` | Fact | FRAG-11-004: HTTP/1.1 split at first byte of CRLFCRLF |
| 10 | `Should_BufferAndComplete_When_Http11ChunkSizeLineSplitMidHex` | Fact | FRAG-11-005: HTTP/1.1 chunk-size line split mid-hex |
| 11 | `Should_BufferAndComplete_When_Http11ChunkDataSplitMidContent` | Fact | FRAG-11-006: HTTP/1.1 chunk data split mid-content |
| 12 | `Should_BufferAndComplete_When_Http11FinalChunkSplit` | Fact | FRAG-11-007: HTTP/1.1 final 0-chunk split |
| 13 | `Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte1` | Fact | FRAG-2-001: HTTP/2 frame header split at byte 1 |
| 14 | `Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte3` | Fact | FRAG-2-002: HTTP/2 frame header split at byte 3 (end of length) |
| 15 | `Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte5` | Fact | FRAG-2-003: HTTP/2 frame header split at byte 5 (flags) |
| 16 | `Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte8` | Fact | FRAG-2-004: HTTP/2 frame header split at byte 8 (last stream byte) |
| 17 | `Should_BufferAndComplete_When_Http2DataPayloadSplitMidContent` | Fact | FRAG-2-005: HTTP/2 DATA payload split mid-content |
| 18 | `Should_ReturnFalse_When_Http2HpackBlockSplitMidStream` | Fact | FRAG-2-006: HTTP/2 HEADERS HPACK block split mid-stream |
| 19 | `Should_AccumulateAndComplete_When_Http2SplitBetweenHeadersAndContinuation` | Fact | FRAG-2-007: HTTP/2 split between HEADERS and CONTINUATION frames |
| 20 | `Should_ProcessBothFrames_When_TwoCompleteFramesInOneBuffer` | Fact | FRAG-2-008: Two complete HTTP/2 frames in one read both processed |
| 21 | `Should_BufferAndComplete_When_SecondStreamHeadersSplitWhileFirstActive` | Fact | FRAG-2-009: Second stream's HEADERS split across reads while first stream active |

#### `RetryEvaluatorTests.cs` - `RetryEvaluatorTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Retry_When_GET_And_NetworkFailure` | Fact | RE-001: Should_Retry_When_GET_And_NetworkFailure |
| 2 | `Should_Retry_When_HEAD_And_NetworkFailure` | Fact | RE-002: Should_Retry_When_HEAD_And_NetworkFailure |
| 3 | `Should_Retry_When_PUT_And_NetworkFailure` | Fact | RE-003: Should_Retry_When_PUT_And_NetworkFailure |
| 4 | `Should_Retry_When_DELETE_And_NetworkFailure` | Fact | RE-004: Should_Retry_When_DELETE_And_NetworkFailure |
| 5 | `Should_Retry_When_OPTIONS_And_NetworkFailure` | Fact | RE-005: Should_Retry_When_OPTIONS_And_NetworkFailure |
| 6 | `Should_Retry_When_TRACE_And_NetworkFailure` | Fact | RE-006: Should_Retry_When_TRACE_And_NetworkFailure |
| 7 | `Should_NotRetry_When_POST_And_NetworkFailure` | Fact | RE-007: Should_NotRetry_When_POST_And_NetworkFailure |
| 8 | `Should_NotRetry_When_PATCH_And_NetworkFailure` | Fact | RE-008: Should_NotRetry_When_PATCH_And_NetworkFailure |
| 9 | `Should_NotRetry_When_POST_And_408Response` | Fact | RE-009: Should_NotRetry_When_POST_And_408Response |
| 10 | `Should_NotRetry_When_POST_And_503Response` | Fact | RE-010: Should_NotRetry_When_POST_And_503Response |
| 11 | `Should_Retry_When_GET_And_408Response` | Fact | RE-011: Should_Retry_When_GET_And_408Response |
| 12 | `Should_Retry_When_GET_And_503Response` | Fact | RE-012: Should_Retry_When_GET_And_503Response |
| 13 | `Should_Retry_When_DELETE_And_408Response` | Fact | RE-013: Should_Retry_When_DELETE_And_408Response |
| 14 | `Should_NotRetry_When_GET_And_500Response` | Fact | RE-014: Should_NotRetry_When_GET_And_500Response |
| 15 | `Should_NotRetry_When_GET_And_404Response` | Fact | RE-015: Should_NotRetry_When_GET_And_404Response |
| 16 | `Should_NotRetry_When_GET_And_429Response` | Fact | RE-016: Should_NotRetry_When_GET_And_429Response |
| 17 | `Should_NotRetry_When_GET_And_200Response` | Fact | RE-017: Should_NotRetry_When_GET_And_200Response |
| 18 | `Should_NotRetry_When_BodyPartiallyConsumed_GET` | Fact | RE-018: Should_NotRetry_When_BodyPartiallyConsumed_GET |
| 19 | `Should_NotRetry_When_BodyPartiallyConsumed_PUT` | Fact | RE-019: Should_NotRetry_When_BodyPartiallyConsumed_PUT |
| 20 | `Should_NotRetry_When_BodyPartiallyConsumed_DELETE` | Fact | RE-020: Should_NotRetry_When_BodyPartiallyConsumed_DELETE |
| 21 | `Should_NotRetry_When_MaxRetries_Reached` | Fact | RE-021: Should_NotRetry_When_MaxRetries_Reached |
| 22 | `Should_Retry_When_AttemptCount_BelowLimit` | Fact | RE-022: Should_Retry_When_AttemptCount_BelowLimit |
| 23 | `Should_NotRetry_When_MaxRetries_Zero` | Fact | RE-023: Should_NotRetry_When_MaxRetries_Zero |
| 24 | `Should_NotRetry_When_AttemptCount_ExceedsLimit` | Fact | RE-024: Should_NotRetry_When_AttemptCount_ExceedsLimit |
| 25 | `Should_IncludeRetryAfterDelay_When_503_With_Seconds` | Fact | RE-025: Should_IncludeRetryAfterDelay_When_503_With_Seconds |
| 26 | `Should_IncludeRetryAfterDelay_When_408_With_Seconds` | Fact | RE-026: Should_IncludeRetryAfterDelay_When_408_With_Seconds |
| 27 | `Should_RetryAfterDelay_Be_Null_When_No_Header` | Fact | RE-027: Should_RetryAfterDelay_Be_Null_When_No_Header |
| 28 | `Should_RetryAfterDelay_Be_Null_When_RespectRetryAfter_False` | Fact | RE-028: Should_RetryAfterDelay_Be_Null_When_RespectRetryAfter_False |
| 29 | `Should_RetryAfterDelay_Be_Zero_When_Date_In_Past` | Fact | RE-029: Should_RetryAfterDelay_Be_Zero_When_Date_In_Past |
| 30 | `Should_RetryAfterDelay_Be_Null_When_Header_Unparseable` | Fact | RE-030: Should_RetryAfterDelay_Be_Null_When_Header_Unparseable |
| 31 | `Should_UseDefaultPolicy_When_Policy_Null` | Fact | RE-031: Should_UseDefaultPolicy_When_Policy_Null |
| 32 | `Should_Retry_When_NoResponse_And_NoNetworkFailureFlag_GET` | Fact | RE-032: Should_Retry_When_NoResponse_And_NoNetworkFailureFlag_GET |
| 33 | `Should_NotRetry_When_NoResponse_And_POST` | Fact | RE-033: Should_NotRetry_When_NoResponse_And_POST |
| 34 | `Should_AlwaysHaveNonEmptyReason` | Fact | RE-034: Should_AlwaysHaveNonEmptyReason |
| 35 | `RetryPolicy_Default_MaxRetries_Is_Three` | Fact | RE-035: RetryPolicy_Default_MaxRetries_Is_Three |
| 36 | `RetryPolicy_Default_RespectRetryAfter_Is_True` | Fact | RE-036: RetryPolicy_Default_RespectRetryAfter_Is_True |
| 37 | `RetryDecision_Retry_Sets_ShouldRetry_True` | Fact | RE-037: RetryDecision_Retry_Sets_ShouldRetry_True |
| 38 | `RetryDecision_Retry_WithDelay_Sets_RetryAfterDelay` | Fact | RE-038: RetryDecision_Retry_WithDelay_Sets_RetryAfterDelay |
| 39 | `RetryDecision_NoRetry_Sets_ShouldRetry_False` | Fact | RE-039: RetryDecision_NoRetry_Sets_ShouldRetry_False |
| 40 | `Should_RetryAfterDelay_Be_Positive_When_Date_In_Future` | Fact | RE-040: Should_RetryAfterDelay_Be_Positive_When_Date_In_Future |

#### `PerHostConnectionLimiterTests.cs` - `PerHostConnectionLimiterTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Default_MaxConnectionsPerHost_Is_6` | Fact | CL-001: Default_MaxConnectionsPerHost_Is_6 |
| 2 | `Custom_MaxConnectionsPerHost_Is_Stored` | Fact | CL-002: Custom_MaxConnectionsPerHost_Is_Stored |
| 3 | `Constructor_Throws_When_MaxConnectionsPerHost_Negative` | Fact | CL-003: Constructor_Throws_When_MaxConnectionsPerHost_Negative |
| 4 | `TryAcquire_Returns_True_For_First_Connection` | Fact | CL-004: TryAcquire_Returns_True_For_First_Connection |
| 5 | `TryAcquire_Returns_False_When_At_Limit` | Fact | CL-005: TryAcquire_Returns_False_When_At_Limit |
| 6 | `TryAcquire_Returns_False_When_Max_Is_Zero` | Fact | CL-006: TryAcquire_Returns_False_When_Max_Is_Zero |
| 7 | `TryAcquire_Tracks_Different_Hosts_Independently` | Fact | CL-007: TryAcquire_Tracks_Different_Hosts_Independently |
| 8 | `TryAcquire_Is_Case_Insensitive_For_Host` | Fact | CL-008: TryAcquire_Is_Case_Insensitive_For_Host |
| 9 | `Release_Decrements_Active_Count` | Fact | CL-009: Release_Decrements_Active_Count |
| 10 | `TryAcquire_Succeeds_After_Release` | Fact | CL-010: TryAcquire_Succeeds_After_Release |
| 11 | `Release_On_Unknown_Host_Does_Not_Throw` | Fact | CL-011: Release_On_Unknown_Host_Does_Not_Throw |
| 12 | `Release_Does_Not_Go_Negative` | Fact | CL-012: Release_Does_Not_Go_Negative |
| 13 | `GetActiveConnections_Returns_Zero_For_Unknown_Host` | Fact | CL-013: GetActiveConnections_Returns_Zero_For_Unknown_Host |
| 14 | `GetActiveConnections_Returns_Correct_Count_After_Multiple_Acquires` | Fact | CL-014: GetActiveConnections_Returns_Correct_Count_After_Multiple_Acquires |
| 15 | `GetActiveConnections_Is_Case_Insensitive` | Fact | CL-015: GetActiveConnections_Is_Case_Insensitive |
| 16 | `Can_Fill_Exactly_To_MaxConnectionsPerHost` | Fact | CL-016: Can_Fill_Exactly_To_MaxConnectionsPerHost |
| 17 | `ConnectionPolicy_Default_MaxConnectionsPerHost_Is_6` | Fact | CL-017: ConnectionPolicy_Default_MaxConnectionsPerHost_Is_6 |
| 18 | `ConnectionPolicy_AllowHttp2Multiplexing_Is_True_By_Default` | Fact | CL-018: ConnectionPolicy_AllowHttp2Multiplexing_Is_True_By_Default |

#### `CookieJarTests.cs` - `CookieJarTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Basic_Cookie_Is_Stored` | Fact | CM-001: Basic name=value cookie is stored |
| 2 | `Cookie_Value_Is_Added_To_Request` | Fact | CM-002: Cookie value is accessible when adding to request |
| 3 | `Malformed_Cookie_No_Equals_Is_Ignored` | Fact | CM-003: Malformed cookie (no '=') is ignored |
| 4 | `Cookie_With_Empty_Name_Is_Ignored` | Fact | CM-004: Cookie with empty name is ignored |
| 5 | `Multiple_SetCookie_Headers_Are_All_Processed` | Fact | CM-005: Multiple Set-Cookie headers are all processed |
| 6 | `HostOnly_Cookie_Matches_Exact_Host_Only` | Fact | CM-006: Host-only cookie (no Domain attr) matches exact host only |
| 7 | `HostOnly_Cookie_Matches_Same_Host` | Fact | CM-007: Host-only cookie matches same host |
| 8 | `Domain_Cookie_Matches_Subdomain` | Fact | CM-008: Domain cookie matches subdomain |
| 9 | `Domain_Cookie_Does_Not_Match_Unrelated_Host` | Fact | CM-009: Domain cookie does NOT match unrelated host (no naive EndsWith) |
| 10 | `Domain_Cookie_Leading_Dot_Is_Stripped` | Fact | CM-010: Domain cookie with leading dot is stored correctly (dot stripped) |
| 11 | `Path_Cookie_Matches_Sub_Path` | Fact | CM-011: Cookie with path=/api matches /api/users |
| 12 | `Path_Cookie_Does_Not_Match_Partial_Label` | Fact | CM-012: Cookie with path=/api does NOT match /apiv2 |
| 13 | `Path_Root_Matches_All_Paths` | Fact | CM-013: Cookie with path=/ matches all paths |
| 14 | `Path_With_Trailing_Slash_Matches_Sub_Path` | Fact | CM-014: Cookie with path=/foo/ (trailing slash) matches /foo/bar |
| 15 | `Default_Path_Is_Computed_From_Request_URI` | Fact | CM-015: Cookie path is correctly defaulted from request URI |
| 16 | `Secure_Cookie_Not_Sent_Over_Http` | Fact | CM-016: Secure cookie is NOT sent over HTTP |
| 17 | `Secure_Cookie_Sent_Over_Https` | Fact | CM-017: Secure cookie IS sent over HTTPS |
| 18 | `NonSecure_Cookie_Sent_Over_Http` | Fact | CM-018: Non-secure cookie IS sent over HTTP |
| 19 | `HttpOnly_Cookie_Is_Stored` | Fact | CM-019: HttpOnly cookie is stored with HttpOnly=true |
| 20 | `NonHttpOnly_Cookie_Is_Stored_And_Sent` | Fact | CM-020: Non-HttpOnly cookie is stored and sent |
| 21 | `Expired_Cookie_Is_Not_Sent` | Fact | CM-021: Expired cookie (past Expires) is not sent |
| 22 | `Future_Expires_Cookie_Is_Sent` | Fact | CM-022: Future Expires cookie IS sent |
| 23 | `MaxAge_Zero_Deletes_Existing_Cookie` | Fact | CM-023: Max-Age=0 deletes existing cookie |
| 24 | `MaxAge_Takes_Precedence_Over_Expires` | Fact | CM-024: Max-Age takes precedence over Expires |
| 25 | `MaxAge_Positive_Sets_Future_Expiry` | Fact | CM-025: Max-Age positive sets future expiry |
| 26 | `Cookie_Replacement_Same_Name_Domain_Path` | Fact | CM-026: Cookie with same name+domain+path replaces existing cookie |
| 27 | `Cookies_Same_Name_Different_Paths_Coexist` | Fact | CM-027: Cookies with same name but different paths coexist |
| 28 | `Clear_Removes_All_Cookies` | Fact | CM-028: Clear() removes all cookies |
| 29 | `SameSite_Strict_Is_Stored` | Fact | CM-029: SameSite=Strict is stored correctly |
| 30 | `SameSite_Lax_Is_Stored` | Fact | CM-030: SameSite=Lax is stored correctly |
| 31 | `Cookie_Domain_For_Unrelated_Host_Is_Rejected` | Fact | CM-031: Cookie with Domain for unrelated host is rejected |
| 32 | `Cookie_Domain_SuperDomain_Accepted` | Fact | CM-032: Cookie Domain=example.com accepted from sub.example.com |
| 33 | `Cookie_Domain_SubDomain_From_Parent_Is_Rejected` | Fact | CM-033: Cookie Domain=sub.example.com from example.com is rejected |
| 34 | `Cookie_From_Ip_Address_Is_HostOnly` | Fact | CM-034: Cookie from IP address is host-only |
| 35 | `Domain_Cookie_Not_Matched_To_Ip_Address` | Fact | CM-035: Domain cookie is not matched to IP address host |
| 36 | `DomainMatches_Correct_Result` | Theory | CM-036: DomainMatches returns correct result for various combinations |
| 37 | `PathMatches_Correct_Result` | Theory | CM-037: PathMatches returns correct result for various combinations |
| 38 | `Cookies_Sorted_By_Path_Length_Longer_First` | Fact | CM-038: Cookies sorted by path length (longer first) in Cookie header |
| 39 | `Cookie_Jar_Evaluates_For_Redirect_URI` | Fact | CM-039: Cookie jar evaluates cookies for new URI on redirect |
| 40 | `No_Cookies_Sent_When_No_Match` | Fact | CM-040: No cookies sent when jar has no matching cookies |
| 41 | `Various_Expires_Formats_Are_Parsed` | Theory | CM-041: Various Expires date formats are parsed correctly |
| 42 | `Unrecognized_Expires_Format_Treated_As_Session_Cookie` | Fact | CM-042: Cookie with unrecognized Expires format is treated as session cookie |

#### `ConnectionReuseEvaluatorTests.cs` - `ConnectionReuseEvaluatorTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Close_When_Http10_And_No_Connection_Header` | Fact | CM-001: Should_Close_When_Http10_And_No_Connection_Header |
| 2 | `Should_KeepAlive_When_Http10_And_Connection_KeepAlive` | Fact | CM-002: Should_KeepAlive_When_Http10_And_Connection_KeepAlive |
| 3 | `Should_KeepAlive_When_Http10_And_Connection_Keep_Alive_Lowercase` | Fact | CM-003: Should_KeepAlive_When_Http10_And_Connection_Keep_Alive_Lowercase |
| 4 | `Should_KeepAlive_When_Http10_And_Connection_KEEP_ALIVE_Uppercase` | Fact | CM-004: Should_KeepAlive_When_Http10_And_Connection_KEEP_ALIVE_Uppercase |
| 5 | `Should_Close_When_Http10_And_Connection_Close` | Fact | CM-005: Should_Close_When_Http10_And_Connection_Close |
| 6 | `Should_KeepAlive_When_Http11_And_No_Connection_Header` | Fact | CM-006: Should_KeepAlive_When_Http11_And_No_Connection_Header |
| 7 | `Should_Close_When_Http11_And_Connection_Close` | Fact | CM-007: Should_Close_When_Http11_And_Connection_Close |
| 8 | `Should_Close_When_Http11_And_Connection_Close_Uppercase` | Fact | CM-008: Should_Close_When_Http11_And_Connection_Close_Uppercase |
| 9 | `Should_KeepAlive_When_Http11_And_Connection_KeepAlive_Header` | Fact | CM-009: Should_KeepAlive_When_Http11_And_Connection_KeepAlive_Header |
| 10 | `Should_ParseTimeout_When_Http11_And_KeepAlive_Timeout` | Fact | CM-010: Should_ParseTimeout_When_Http11_And_KeepAlive_Timeout |
| 11 | `Should_ParseTimeoutAndMax_When_Http11_And_KeepAlive_Both_Params` | Fact | CM-011: Should_ParseTimeoutAndMax_When_Http11_And_KeepAlive_Both_Params |
| 12 | `Should_ParseTimeout_When_Http10_KeepAlive_With_Timeout_Param` | Fact | CM-012: Should_ParseTimeout_When_Http10_KeepAlive_With_Timeout_Param |
| 13 | `Should_IgnoreInvalidTimeout_When_KeepAlive_Has_Non_Numeric_Timeout` | Fact | CM-013: Should_IgnoreInvalidTimeout_When_KeepAlive_Has_Non_Numeric_Timeout |
| 14 | `Should_ParseMax_When_KeepAlive_Has_Max_Only` | Fact | CM-014: Should_ParseMax_When_KeepAlive_Has_Max_Only |
| 15 | `Should_Close_When_Http11_And_Body_Not_Fully_Consumed` | Fact | CM-015: Should_Close_When_Http11_And_Body_Not_Fully_Consumed |
| 16 | `Should_Close_When_Http11_And_Protocol_Error_Occurred` | Fact | CM-016: Should_Close_When_Http11_And_Protocol_Error_Occurred |
| 17 | `Should_Close_On_ProtocolError_Even_When_ConnectionClose_Not_Set` | Fact | CM-017: Should_Close_On_ProtocolError_Even_When_ConnectionClose_Not_Set |
| 18 | `Should_Close_When_101_Switching_Protocols` | Fact | CM-018: Should_Close_When_101_Switching_Protocols |
| 19 | `Should_KeepAlive_When_Http2_No_Headers` | Fact | CM-019: Should_KeepAlive_When_Http2_No_Headers |
| 20 | `Should_KeepAlive_When_Http2_Body_Not_Consumed` | Fact | CM-020: Should_KeepAlive_When_Http2_Body_Not_Consumed |
| 21 | `Should_KeepAlive_When_Http2_Protocol_Error_Occurred` | Fact | CM-021: Should_KeepAlive_When_Http2_Protocol_Error_Occurred |
| 22 | `Should_KeepAlive_When_Http2_Even_If_Connection_Close_Present` | Fact | CM-022: Should_KeepAlive_When_Http2_Even_If_Connection_Close_Present |
| 23 | `Should_Have_NonEmpty_Reason_On_KeepAlive` | Fact | CM-023: Should_Have_NonEmpty_Reason_On_KeepAlive |
| 24 | `Should_Have_NonEmpty_Reason_On_Close` | Fact | CM-024: Should_Have_NonEmpty_Reason_On_Close |
| 25 | `Should_HaveNullTimeouts_When_Http11_No_KeepAlive_Header` | Fact | CM-025: Should_HaveNullTimeouts_When_Http11_No_KeepAlive_Header |

#### `HttpDecodeErrorMessagesTests.cs` - `HttpDecodeErrorMessagesTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Include_RfcReference_When_InvalidStatusLine` | Fact | 34-msg-001: InvalidStatusLine — message contains RFC 9112 §4 |
| 2 | `Should_Include_RfcReference_When_InvalidHeader` | Fact | 34-msg-002: InvalidHeader — message contains RFC 9112 §5.1 |
| 3 | `Should_Include_RfcReference_When_InvalidContentLength` | Fact | 34-msg-003: InvalidContentLength — message contains RFC 9112 §6.3 |
| 4 | `Should_Include_RfcReference_When_InvalidChunkedEncoding` | Fact | 34-msg-004: InvalidChunkedEncoding — message contains RFC 9112 §7.1 |
| 5 | `Should_Include_RfcReference_When_LineTooLong` | Fact | 34-msg-005: LineTooLong — message contains RFC 9112 §2.3 |
| 6 | `Should_Include_RfcReference_When_InvalidRequestLine` | Fact | 34-msg-006: InvalidRequestLine — message contains RFC 9112 §3 |
| 7 | `Should_Include_RfcReference_When_InvalidMethodToken` | Fact | 34-msg-007: InvalidMethodToken — message contains RFC 9112 §3.1 |
| 8 | `Should_Include_RfcReference_When_InvalidRequestTarget` | Fact | 34-msg-008: InvalidRequestTarget — message contains RFC 9112 §3.2 |
| 9 | `Should_Include_RfcReference_When_InvalidHttpVersion` | Fact | 34-msg-009: InvalidHttpVersion — message contains RFC 9112 §2.3 |
| 10 | `Should_Include_RfcReference_When_MissingHostHeader` | Fact | 34-msg-010: MissingHostHeader — message contains RFC 9112 §5.4 |
| 11 | `Should_Include_RfcReference_When_MultipleHostHeaders` | Fact | 34-msg-011: MultipleHostHeaders — message contains RFC 9112 §5.4 |
| 12 | `Should_Include_RfcReference_When_MultipleContentLengthValues` | Fact | 34-msg-012: MultipleContentLengthValues — message contains RFC 9112 §6.3 |
| 13 | `Should_Include_RfcReference_When_InvalidFieldName` | Fact | 34-msg-013: InvalidFieldName — message contains RFC 9112 §5.1 |
| 14 | `Should_Include_RfcReference_When_InvalidFieldValue` | Fact | 34-msg-014: InvalidFieldValue — message contains RFC 9112 §5.5 |
| 15 | `Should_Include_RfcReference_When_ObsoleteFoldingDetected` | Fact | 34-msg-015: ObsoleteFoldingDetected — message contains RFC 9112 §5.2 |
| 16 | `Should_Include_RfcReference_When_ChunkedWithContentLength` | Fact | 34-msg-016: ChunkedWithContentLength — message contains RFC 9112 §6.3 |
| 17 | `Should_Include_RfcReference_When_InvalidChunkSize` | Fact | 34-msg-017: InvalidChunkSize — message contains RFC 9112 §7.1.1 |
| 18 | `Should_Include_RfcReference_When_ChunkDataTruncated` | Fact | 34-msg-018: ChunkDataTruncated — message contains RFC 9112 §7.1.3 |
| 19 | `Should_Include_RfcReference_When_InvalidChunkExtension` | Fact | 34-msg-019: InvalidChunkExtension — message contains RFC 9112 §7.1.1 |
| 20 | `Should_Include_SecurityNote_When_TooManyHeaders` | Fact | 34-msg-020: TooManyHeaders — message contains Security note and RFC 9112 §5 |
| 21 | `Should_NotBeJustEnumName_When_InvalidStatusLine` | Fact | 34-msg-021: InvalidStatusLine — message is not just enum name |
| 22 | `Should_NotBeJustEnumName_When_TooManyHeaders` | Fact | 34-msg-022: TooManyHeaders — message is not just enum name |
| 23 | `Should_IncludeContext_When_ContextProvided` | Fact | 34-msg-023: Context overload — caller context appears in message |
| 24 | `Should_StillIncludeDefaultMessage_When_ContextProvided` | Fact | 34-msg-024: Context overload — default RFC message also present |
| 25 | `Should_PreserveDecodeError_When_ContextOverloadUsed` | Fact | 34-msg-025: Context overload — DecodeError property preserved |
| 26 | `Should_PreserveDecodeError_When_DefaultConstructorUsed` | Fact | 34-msg-026: Default constructor — DecodeError property correct |
| 27 | `Should_InheritFromException` | Fact | 34-msg-027: HttpDecoderException inherits from System.Exception |
| 28 | `Should_IncludeHeaderCount_When_Http11DecoderThrowsTooManyHeaders` | Fact | 34-msg-028: Http11Decoder TooManyHeaders — context includes count and limit |
| 29 | `Should_IncludeFieldName_When_Http11DecoderThrowsInvalidFieldValue` | Fact | 34-msg-029: Http11Decoder InvalidFieldValue — context includes field name |
| 30 | `Should_IncludeConflictingValues_When_Http11DecoderThrowsMultipleContentLengths` | Fact | 34-msg-030: Http11Decoder MultipleContentLengthValues — context includes both values |
| 31 | `Should_IncludeStatusLine_When_Http10DecoderThrowsInvalidStatusLine` | Fact | 34-msg-031: Http10Decoder InvalidStatusLine — context includes actual line |
| 32 | `Should_IncludeActualValue_When_Http10DecoderThrowsInvalidContentLength` | Fact | 34-msg-032: Http10Decoder InvalidContentLength — context includes actual value |
| 33 | `Should_IncludeConflictingValues_When_Http10DecoderThrowsMultipleContentLengths` | Fact | 34-msg-033: Http10Decoder MultipleContentLengthValues — context includes both values |
| 34 | `Should_Include_RfcSection_When_Http11DecoderThrowsInvalidChunkSize` | Fact | 34-msg-034: Http11Decoder InvalidChunkSize — message contains RFC 9112 §7.1.1 |

#### `CrossFeatureIntegrityTests.cs` - `CrossFeatureIntegrityTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Redirect_CrossDomain_CookieNotForwardedToNewDomain` | Fact | CFI-001: Cross-domain redirect — cookie not forwarded to new domain |
| 2 | `Redirect_SameDomain_CookieForwarded` | Fact | CFI-002: Same-domain redirect — applicable cookies forwarded |
| 3 | `Redirect_SetCookieInRedirectResponse_ProcessedBeforeNewRequest` | Fact | CFI-003: Set-Cookie in redirect response is processed before new request |
| 4 | `Redirect_SecureCookie_NotForwardedOnHttp` | Fact | CFI-004: Secure cookie not forwarded on HTTP redirect |
| 5 | `Redirect_SecureCookie_ForwardedOnHttps` | Fact | CFI-005: Cookie forwarded on same-domain HTTPS redirect |
| 6 | `Redirect_PathScopedCookie_NotForwardedWhenOutOfPath` | Fact | CFI-006: Path-scoped cookie not forwarded when redirect leaves scope |
| 7 | `Redirect_MultipleCookies_OnlyMatchingForwarded` | Fact | CFI-007: Cookie jar handles multiple cookies; only matching ones forwarded |
| 8 | `Redirect_CrossOrigin_StripsAuthorizationHeader` | Fact | CFI-011: Cross-origin redirect strips Authorization header |
| 9 | `Redirect_SameOrigin_PreservesAuthorizationHeader` | Fact | CFI-012: Same-origin redirect preserves Authorization header |
| 10 | `Redirect_CrossScheme_SameHost_StripsAuthorization` | Fact | CFI-013: Cross-scheme (HTTP to HTTPS same host) strips Authorization |
| 11 | `Redirect_307_SameOrigin_PreservesAuthorizationAndBody` | Fact | CFI-014: 307 same-origin redirect preserves Authorization and body |
| 12 | `Redirect_307_CrossOrigin_StripsAuthorization_ButPreservesMethodAndBody` | Fact | CFI-015: Cross-origin 307 strips Authorization but preserves method and body |
| 13 | `Decompress_Gzip_ReturnsCorrectContent` | Fact | CFI-021: Gzip decompression returns correct content |
| 14 | `Decompress_Deflate_ReturnsCorrectContent` | Fact | CFI-022: Deflate decompression returns correct content |
| 15 | `Decompress_Identity_ReturnBodyUnchanged` | Fact | CFI-023: Identity encoding returns body unchanged |
| 16 | `Decompress_NullEncoding_ReturnBodyUnchanged` | Fact | CFI-024: Null content-encoding returns body unchanged |
| 17 | `Decompress_EmptyBody_GzipEncoding_ReturnsEmpty` | Fact | CFI-025: Empty body with gzip encoding returns empty bytes |
| 18 | `Decompress_UnknownEncoding_ThrowsHttpDecoderException` | Fact | CFI-026: Unknown encoding throws HttpDecoderException with DecompressionFailed |
| 19 | `Decompress_StackedGzipIdentity_DecodesCorrectly` | Fact | CFI-027: Stacked gzip+identity decodes correctly (identity is no-op) |
| 20 | `Decompress_DoesNotModifyOriginalBytes` | Fact | CFI-028: Decompression does not modify original compressed bytes (immutability) |
| 21 | `Connection_Http11_Default_IsReusable` | Fact | CFI-031: HTTP/1.1 default — connection reusable after successful response |
| 22 | `Connection_BodyNotConsumed_MustClose` | Fact | CFI-032: Body not fully consumed — connection closed to prevent framing desync |
| 23 | `Connection_ProtocolError_MustClose` | Fact | CFI-033: Protocol error — connection closed (state unknown) |
| 24 | `Connection_CloseHeader_MustNotReuse` | Fact | CFI-034: Connection: close header — connection must not be reused |
| 25 | `PerHostLimiter_AcquireAndRelease_CorrectTracking` | Fact | CFI-035: Per-host limiter — slot acquired and released correctly |
| 26 | `PerHostLimiter_Release_BringsCountToZero` | Fact | CFI-036: Per-host limiter — release brings count to zero cleanly |
| 27 | `Connection_Http2_AlwaysReusable` | Fact | CFI-037: HTTP/2 connection always reusable regardless of body state |
| 28 | `Retry_Get_PartiallyConsumedBody_NoRetry` | Fact | CFI-041: GET with partially consumed body — NoRetry (cannot rewind) |
| 29 | `Retry_Get_RewindableBody_NetworkFailure_ShouldRetry` | Fact | CFI-042: GET with rewindable body on network failure — Retry |
| 30 | `Retry_Post_NonIdempotent_NoRetry` | Fact | CFI-043: POST — NoRetry regardless of failure type |
| 31 | `Retry_Get_StreamedBody_On408_NoRetry` | Fact | CFI-044: Streamed GET body (partial) on 408 — NoRetry |
| 32 | `Retry_Get_RewindableBody_On408_ShouldRetry` | Fact | CFI-045: Rewindable GET on 408 — Retry |
| 33 | `Retry_MaxRetriesExhausted_NoRetry` | Fact | CFI-046: MaxRetries exhausted — NoRetry even for idempotent GET |
| 34 | `Retry_RetryAfterHeader_Extracted` | Fact | CFI-047: Retry-After delay extracted and surfaced in decision |
| 35 | `Head_TryDecodeHead_BodyAlwaysEmpty` | Fact | CFI-051: HEAD decoded via TryDecodeHead — body is always empty |
| 36 | `Head_LargeContentLength_BodyStillEmpty` | Fact | CFI-052: HEAD response with large Content-Length — body still empty |
| 37 | `Head_ContentEncoding_HeadersPreserved_BodyEmpty` | Fact | CFI-053: HEAD response with Content-Encoding — headers preserved, body empty |
| 38 | `Head_IsIdempotent_RetryAllowed` | Fact | CFI-054: HEAD method is idempotent — RetryEvaluator allows retry |
| 39 | `Head_Redirect307_MethodPreserved` | Fact | CFI-055: HEAD redirect with 307 preserves HEAD method |
| 40 | `Head_ConnectionReuse_IsReusable` | Fact | CFI-056: HEAD ConnectionReuseEvaluator — connection reusable after HEAD |

### IO (18 tests)

#### `TcpOptionsFactoryTests.cs` - `TcpOptionsFactoryTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `TCP_001_Http_DefaultPort_80` | Fact |  |
| 2 | `TCP_002_Https_DefaultPort_443_ReturnsTlsOptions` | Fact |  |
| 3 | `TCP_003_Http_ExplicitPort_8080` | Fact |  |
| 4 | `TCP_004_Https_ExplicitPort_8443` | Fact |  |
| 5 | `TCP_005_IPv4Literal_InterNetwork` | Fact |  |
| 6 | `TCP_006_IPv6Literal_InterNetworkV6` | Fact |  |
| 7 | `TCP_007_Hostname_Unspecified` | Fact |  |
| 8 | `TCP_008_ConnectTimeout_Propagated` | Fact |  |
| 9 | `TCP_009_ReconnectInterval_Propagated` | Fact |  |
| 10 | `TCP_010_MaxReconnectAttempts_Propagated` | Fact |  |
| 11 | `TCP_011_MaxFrameSize_Propagated` | Fact |  |
| 12 | `TCP_012_Https_CallbackPropagated` | Fact |  |
| 13 | `TCP_013_Http_CallbackIgnored_ReturnsTcpOptions` | Fact |  |
| 14 | `TCP_014_TlsOptions_TargetHost_EqualsHost` | Fact |  |
| 15 | `TCP_015_Wss_ReturnsTlsOptions` | Fact |  |

#### `ClientManagerProviderSelectionTests.cs` - `StubClientProvider`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `CLT_001_TcpOptions_NoStreamProvider_CreatesTcpClientProvider` | Fact |  |
| 2 | `CLT_002_TlsOptions_NoStreamProvider_CreatesTlsClientProvider` | Fact |  |
| 3 | `CLT_003_StreamProviderSet_UsedRegardlessOfOptionsType` | Fact |  |

### RFC1945 (211 tests)

#### `12_RoundTripMethodTests.cs` - `Http10RoundTripMethodTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_PreserveGetMethod_When_RoundTrip` | Fact | RFC1945-RT-M01: GET method preserved in round-trip |
| 2 | `Should_PreservePostMethod_When_RoundTrip` | Fact | RFC1945-RT-M02: POST method preserved in round-trip |
| 3 | `Should_PreservePutMethod_When_RoundTrip` | Fact | RFC1945-RT-M03: PUT method preserved in round-trip |
| 4 | `Should_PreserveDeleteMethod_When_RoundTrip` | Fact | RFC1945-RT-M04: DELETE method preserved in round-trip |
| 5 | `Should_PreservePatchMethod_When_RoundTrip` | Fact | RFC1945-RT-M05: PATCH method preserved in round-trip |
| 6 | `Should_PreserveOptionsMethod_When_RoundTrip` | Fact | RFC1945-RT-M06: OPTIONS method preserved in round-trip |
| 7 | `Should_PreserveHeadMethod_When_RoundTrip` | Fact | RFC1945-RT-M07: HEAD method preserved in round-trip |
| 8 | `Should_PreserveQueryString_When_GetRoundTrip` | Fact | RFC1945-RT-M08: GET with query string round-trip |
| 9 | `Should_PreservePostBody_When_PostRoundTrip` | Fact | RFC1945-RT-M09: POST with body round-trip |
| 10 | `Should_PreserveMethodsConsistently_When_MultipleRequests` | Fact | RFC1945-RT-M10: Multiple requests with different methods |
| 11 | `Should_PreserveTraceMethod_When_RoundTrip` | Fact | RFC1945-RT-M11: TRACE method (extension) round-trip |
| 12 | `Should_PreserveUppercaseMethod_When_Encoded` | Fact | RFC1945-RT-M12: Method case sensitivity (uppercase required) |

#### `13_RoundTripStatusCodeTests.cs` - `Http10RoundTripStatusCodeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Decode200Ok_When_RoundTrip` | Fact | RFC1945-RT-S01: 200 OK status code round-trip |
| 2 | `Should_Decode201Created_When_RoundTrip` | Fact | RFC1945-RT-S02: 201 Created status code round-trip |
| 3 | `Should_Decode204NoContent_When_RoundTrip` | Fact | RFC1945-RT-S03: 204 No Content status code round-trip |
| 4 | `Should_Decode301MovedPermanently_When_RoundTrip` | Fact | RFC1945-RT-S04: 301 Moved Permanently status code round-trip |
| 5 | `Should_Decode302Found_When_RoundTrip` | Fact | RFC1945-RT-S05: 302 Found status code round-trip |
| 6 | `Should_Decode304NotModified_When_RoundTrip` | Fact | RFC1945-RT-S06: 304 Not Modified status code round-trip |
| 7 | `Should_Decode400BadRequest_When_RoundTrip` | Fact | RFC1945-RT-S07: 400 Bad Request status code round-trip |
| 8 | `Should_Decode401Unauthorized_When_RoundTrip` | Fact | RFC1945-RT-S08: 401 Unauthorized status code round-trip |
| 9 | `Should_Decode404NotFound_When_RoundTrip` | Fact | RFC1945-RT-S09: 404 Not Found status code round-trip |
| 10 | `Should_Decode500InternalServerError_When_RoundTrip` | Fact | RFC1945-RT-S10: 500 Internal Server Error status code round-trip |
| 11 | `Should_Decode503ServiceUnavailable_When_RoundTrip` | Fact | RFC1945-RT-S11: 503 Service Unavailable status code round-trip |

#### `10_DecoderFragmentationTests.cs` - `Http10DecoderFragmentationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Fragmentation_HeadersSplitAcrossTwoChunks_ReassembledCorrectly` | Fact | RFC1945-FRAG-001: Headers split across two chunks reassembled |
| 2 | `Fragmentation_BodySplitAcrossTwoChunks_ReassembledCorrectly` | Fact | RFC1945-FRAG-002: Body split across two chunks reassembled |
| 3 | `Fragmentation_SingleByteChunks_EventuallyDecodes` | Fact | RFC1945-FRAG-003: Single byte chunks eventually decoded |
| 4 | `Fragmentation_MultipleResponses_DecodedIndependently` | Fact | RFC1945-FRAG-004: Multiple responses decoded independently |
| 5 | `Fragmentation_IncompleteHeader_ReturnsFalseAndBuffers` | Fact | RFC1945-FRAG-005: Incomplete header returns false and buffers |
| 6 | `Fragmentation_IncompleteBody_ReturnsFalseAndBuffers` | Fact | RFC1945-FRAG-006: Incomplete body returns false and buffers |
| 7 | `Fragmentation_ThreeChunks_DecodesCorrectly` | Fact | RFC1945-FRAG-007: Three chunks decoded correctly |
| 8 | `Should_Reassemble_When_StatusLineSplitAtByte1` | Fact | RFC1945-FRAG-008: Status-line split at byte 1 reassembled |
| 9 | `Should_Reassemble_When_StatusLineSplitInsideVersion` | Fact | RFC1945-FRAG-009: Status-line split inside version reassembled |
| 10 | `Should_Reassemble_When_HeaderNameSplit` | Fact | RFC1945-FRAG-010: Header name split across two reads |
| 11 | `Should_Reassemble_When_HeaderValueSplit` | Fact | RFC1945-FRAG-011: Header value split across two reads |
| 12 | `Should_Reassemble_When_BodySplitMidContent` | Fact | RFC1945-FRAG-012: Body split mid-content reassembled |

#### `11_DecoderStateTests.cs` - `Http10DecoderStateTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `TryDecodeEof_WithBufferedData_ReturnsTrue` | Fact | RFC1945-STATE-001: TryDecodeEof with buffered data returns true |
| 2 | `TryDecodeEof_WithEmptyBuffer_ReturnsFalse` | Fact | RFC1945-STATE-002: TryDecodeEof with empty buffer returns false |
| 3 | `TryDecodeEof_WithIncompleteHeader_ReturnsFalse` | Fact | RFC1945-STATE-003: TryDecodeEof with incomplete header returns false |
| 4 | `TryDecodeEof_ClearsRemainder` | Fact | RFC1945-STATE-004: TryDecodeEof clears remainder |
| 5 | `Reset_ClearsBufferedData` | Fact | RFC1945-STATE-005: Reset clears buffered data |
| 6 | `Reset_AfterReset_DecodesNewResponseCorrectly` | Fact | RFC1945-STATE-006: Reset allows decoding new response |
| 7 | `Reset_CalledMultipleTimes_DoesNotThrow` | Fact | RFC1945-STATE-007: Reset called multiple times does not throw |
| 8 | `EdgeCase_EmptyInput_ReturnsFalse` | Fact | RFC1945-STATE-008: Empty input returns false |
| 9 | `EdgeCase_StatePreservedAcrossPartials` | Fact | RFC1945-STATE-009: Decoder state preserved across partial decodes |
| 10 | `EdgeCase_DecoderReusableAfterDecode` | Fact | RFC1945-STATE-010: Decoder reusable after successful decode |
| 11 | `EdgeCase_MultipleResetIdempotent` | Fact | RFC1945-STATE-011: Multiple Reset calls idempotent |
| 12 | `EdgeCase_StateMaintenanceMultipleFragments` | Fact | RFC1945-STATE-012: Decoder maintains state through multiple fragments |
| 13 | `EdgeCase_TryDecodeEofAfterSuccess` | Fact | RFC1945-STATE-013: TryDecodeEof called after successful decode returns false |

#### `16_RoundTripFragmentationTests.cs` - `Http10RoundTripFragmentationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_HandleFragmentationAtStatusLine_When_RoundTrip` | Fact | RFC1945-RT-F01: Fragmented at status line |
| 2 | `Should_HandleFragmentationAtHeaderBoundary_When_RoundTrip` | Fact | RFC1945-RT-F02: Fragmented at header boundary |
| 3 | `Should_HandleFragmentationAtHeaderEndBoundary_When_RoundTrip` | Fact | RFC1945-RT-F03: Fragmented at CRLF CRLF boundary |
| 4 | `Should_HandleBodyFragmentation_When_RoundTrip` | Fact | RFC1945-RT-F04: Fragmented body delivery |

#### `17_RoundTripProtocolTests.cs` - `Http10RoundTripProtocolTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_EncodeHttp10Version_When_RequestEncoded` | Fact | RFC1945-RT-P01: HTTP/1.0 version invariant |
| 2 | `Should_FormatRequestLineCorrectly_When_Encoded` | Fact | RFC1945-RT-P02: Request line format invariant |
| 3 | `Should_UseCrlfLineEndings_When_Encoded` | Fact | RFC1945-RT-P03: CRLF line endings required |
| 4 | `Should_DecodeThreeDigitStatusCode_When_RoundTrip` | Fact | RFC1945-RT-P04: Status code must be 3 digits |
| 5 | `Should_ResetDecoderState_When_Called` | Fact | RFC1945-RT-P05: Decoder state reset between requests |
| 6 | `Should_MaintainIndependentDecoderStates_When_MultipleDecodersUsed` | Fact | RFC1945-RT-P06: Multiple decoders independent |
| 7 | `Should_PreserveCustomReasonPhrase_When_Decoded` | Fact | RFC1945-RT-P07: Reason phrase can be custom |
| 8 | `Should_HandleCaseInsensitiveHeaders_When_Decoded` | Fact | RFC1945-RT-P08: Headers case-insensitive |
| 9 | `Should_ProduceDeterministicEncoding_When_SameRequest` | Fact | RFC1945-RT-P09: Request encoding deterministic |
| 10 | `Should_IncludeContentLength_When_RequestHasBody` | Fact | RFC1945-RT-P10: Content-Length required for request bodies |

#### `14_RoundTripHeaderTests.cs` - `Http10RoundTripHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_PreserveContentTypeHeader_When_RoundTrip` | Fact | RFC1945-RT-H01: Content-Type header preserved |
| 2 | `Should_PreserveContentLengthHeader_When_RoundTrip` | Fact | RFC1945-RT-H02: Content-Length header preserved |
| 3 | `Should_PreserveCustomHeader_When_RoundTrip` | Fact | RFC1945-RT-H03: Custom X-Custom header preserved |
| 4 | `Should_PreserveLocationHeader_When_RoundTrip` | Fact | RFC1945-RT-H04: Location header preserved in redirect |
| 5 | `Should_PreserveMultipleCustomHeaders_When_RoundTrip` | Fact | RFC1945-RT-H05: Multiple custom headers preserved |
| 6 | `Should_PreserveServerHeader_When_RoundTrip` | Fact | RFC1945-RT-H06: Server header preserved |
| 7 | `Should_PreserveDateHeader_When_RoundTrip` | Fact | RFC1945-RT-H07: Date header preserved |
| 8 | `Should_PreserveHeaderWithSpecialChars_When_RoundTrip` | Fact | RFC1945-RT-H08: Header values with special characters preserved |

#### `15_RoundTripBodyTests.cs` - `Http10RoundTripBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_PreserveTextBody_When_ContentLengthRoundTrip` | Fact | RFC1945-RT-B01: Content-Length with text body round-trip |
| 2 | `Should_PreserveBinaryBody_When_RoundTrip` | Fact | RFC1945-RT-B02: Binary body with correct Content-Length |
| 3 | `Should_PreserveUtf8Body_When_RoundTrip` | Fact | RFC1945-RT-B03: UTF-8 encoded body round-trip |
| 4 | `Should_DecodeEmptyBody_When_ContentLengthZero` | Fact | RFC1945-RT-B04: Empty body with Content-Length 0 |
| 5 | `Should_PreserveLargeBody_When_1MbRoundTrip` | Fact | RFC1945-RT-B05: Large body (1MB) round-trip |

#### `09_DecoderConnectionTests.cs` - `Http10DecoderConnectionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DefaultToClose_When_NoConnectionHeader` | Fact | RFC1945-8-CONN-001: HTTP/1.0 default connection is close |
| 2 | `Should_RecognizeKeepAlive_When_ConnectionHeaderPresent` | Fact | RFC1945-8-CONN-002: Connection: keep-alive recognized in HTTP/1.0 |
| 3 | `Should_ParseKeepAliveParams_When_KeepAliveHeader` | Fact | RFC1945-8-CONN-003: Keep-Alive timeout and max parameters parsed |
| 4 | `Should_SignalClose_When_ConnectionCloseHeader` | Fact | RFC1945-8-CONN-004: Connection: close signals close after response |
| 5 | `Should_NotDefaultToKeepAlive_When_Http10` | Fact | RFC1945-8-CONN-005: HTTP/1.0 does not default to keep-alive |

#### `03_EncoderBodyTests.cs` - `Http10EncoderBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Body_PostWithBody_ContentLengthIsCorrect` | Fact |  |
| 2 | `Body_PostWithBody_BodyIsCorrectlyWritten` | Fact |  |
| 3 | `Body_GetWithNoBody_ContentLengthAbsent` | Fact |  |
| 4 | `Body_GetWithNoBody_ContentTypeAbsent` | Fact |  |
| 5 | `Body_PostWithBinaryBody_BytesExactlyPreserved` | Fact |  |
| 6 | `Body_PostWithEmptyBody_ContentLengthIsZero` | Fact |  |
| 7 | `Body_PostWithLargeBody_ContentLengthMatchesBodySize` | Fact |  |
| 8 | `Body_PostWithBody_BodyAppearsAfterHeaderSeparator` | Fact |  |
| 9 | `Should_SetContentLength_When_PostHasBody` | Fact | 1945-enc-005: Content-Length present for POST body |
| 10 | `Should_OmitContentLength_When_GetHasNoBody` | Fact | 1945-enc-006: Content-Length absent for bodyless GET |
| 11 | `Should_EncodeBinaryBodyVerbatim_When_PostWithBinaryContent` | Fact | 1945-enc-008: Binary POST body encoded verbatim |
| 12 | `Should_EncodeUtf8JsonBody_When_PostWithJsonContent` | Fact | 1945-enc-009: UTF-8 JSON body encoded correctly |
| 13 | `Should_NotTruncateBody_When_BodyContainsNullBytes` | Fact | enc1-body-001: Body with null bytes not truncated |
| 14 | `Should_EncodeWithCorrectContentLength_When_BodyIs2MB` | Fact | enc1-body-002: 2 MB body encoded with correct Content-Length |
| 15 | `Should_SeparateHeadersFromBody_When_EncodingWithBody` | Fact | enc1-body-003: CRLFCRLF separates headers from body |

#### `04_EncoderSecurityTests.cs` - `Http10EncoderSecurityTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `HeaderInjection_CrInValue_ThrowsArgumentException` | Fact |  |
| 2 | `HeaderInjection_LfInValue_ThrowsArgumentException` | Fact |  |
| 3 | `HeaderInjection_CrLfInValue_ThrowsArgumentException` | Fact |  |
| 4 | `HeaderInjection_Exception_ContainsHeaderName` | Fact |  |
| 5 | `HeaderInjection_NormalValue_DoesNotThrow` | Fact |  |
| 6 | `BufferOverflow_BufferTooSmallForHeaders_ThrowsInvalidOperationException` | Fact |  |
| 7 | `BufferOverflow_BufferTooSmallForBody_ThrowsInvalidOperationException` | Fact |  |
| 8 | `BufferOverflow_ExactSizeBuffer_DoesNotThrow` | Fact |  |
| 9 | `BufferOverflow_EmptyBuffer_ThrowsInvalidOperationException` | Fact |  |

#### `01_EncoderRequestLineTests.cs` - `Http10EncoderRequestLineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `RequestLine_Get_IsCorrectlyFormatted` | Fact |  |
| 2 | `RequestLine_Head_IsCorrectlyFormatted` | Fact |  |
| 3 | `RequestLine_Post_IsCorrectlyFormatted` | Fact |  |
| 4 | `RequestLine_ContainsExactlyOneSpaceBetweenParts` | Fact |  |
| 5 | `RequestLine_ProtocolVersionIsHttp10` | Fact |  |
| 6 | `RequestLine_EndsWithCrLf` | Fact |  |
| 7 | `RequestLine_WithQueryString_IncludesQueryInUri` | Fact |  |
| 8 | `RequestLine_RootPath_IsForwardSlash` | Fact |  |
| 9 | `RequestLine_DeepPath_IsPreserved` | Fact |  |
| 10 | `Should_UseHttp10Version_When_EncodingRequestLine` | Fact | 1945-enc-001: Request-line uses HTTP/1.0 |
| 11 | `Should_PreservePathAndQuery_When_EncodingRequestLine` | Fact | 1945-enc-007: Path-and-query preserved in request-line |
| 12 | `Should_ThrowArgumentException_When_MethodIsLowercase` | Fact | 1945-5.1-004: Lowercase method rejected by HTTP/1.0 encoder |
| 13 | `Should_EncodeAbsoluteUri_When_AbsoluteFormRequested` | Fact | 1945-5.1-005: Absolute URI encoded in request-line |
| 14 | `Should_ProduceCorrectRequestLine_When_UsingHttpMethod` | Theory | enc1-m-001: All HTTP methods produce correct uppercase request-line |
| 15 | `Should_NormalizeToSlash_When_PathIsMissing` | Fact | enc1-uri-001: Missing path normalized to / |
| 16 | `Should_PreserveQueryString_When_EncodingRequestTarget` | Fact | enc1-uri-002: Query string preserved in request-target |
| 17 | `Should_NotDoubleEncode_When_PathContainsPercentEncoding` | Fact | enc1-uri-003: Percent-encoded chars not double-encoded |
| 18 | `Should_StripFragment_When_UriContainsFragment` | Fact | enc1-uri-004: URI fragment stripped from request-target |

#### `02_EncoderHeaderTests.cs` - `Http10EncoderHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Headers_HostHeader_IsRemovedForHttp10` | Fact |  |
| 2 | `Headers_ConnectionHeader_IsRemoved` | Fact |  |
| 3 | `Headers_KeepAliveHeader_IsRemoved` | Fact |  |
| 4 | `Headers_TransferEncodingHeader_IsRemoved` | Fact |  |
| 5 | `Headers_CustomHeader_IsPreserved` | Fact |  |
| 6 | `Headers_MultipleCustomHeaders_AllPreserved` | Fact |  |
| 7 | `Headers_HeaderFormat_IsNameColonSpaceValue` | Fact |  |
| 8 | `Headers_EachHeaderEndsWithCrLf` | Fact |  |
| 9 | `Headers_MultiValueHeader_EachValueOnSeparateLine` | Fact |  |
| 10 | `Headers_AcceptHeader_IsPreserved` | Fact |  |
| 11 | `Headers_RequestWithNoCustomHeaders_OnlyContainsRfcMandatoryHeaders` | Fact |  |
| 12 | `Headers_HeaderSeparator_IsDoubleCrLf` | Fact |  |
| 13 | `Should_OmitHostHeader_When_EncodingHttp10` | Fact | 1945-enc-002: Host header absent in HTTP/1.0 request |
| 14 | `Should_OmitTransferEncoding_When_EncodingHttp10` | Fact | 1945-enc-003: Transfer-Encoding absent in HTTP/1.0 request |
| 15 | `Should_OmitConnectionHeader_When_EncodingHttp10` | Fact | 1945-enc-004: Connection header absent in HTTP/1.0 request |
| 16 | `Should_TerminateEveryHeaderWithCrlf_When_Encoding` | Fact | enc1-hdr-001: Every header line terminated with CRLF |
| 17 | `Should_PreserveHeaderNameCasing_When_Encoding` | Fact | enc1-hdr-002: Custom header name casing preserved |
| 18 | `Should_EmitAllCustomHeaders_When_MultiplePresent` | Fact | enc1-hdr-003: Multiple custom headers all emitted |
| 19 | `Should_PreserveSemicolon_When_InHeaderValue` | Fact | enc1-hdr-004: Semicolon in header value preserved verbatim |
| 20 | `Should_ThrowArgumentException_When_HeaderValueContainsNul` | Fact | enc1-hdr-005: NUL byte in header value throws exception |

#### `07_DecoderHeaderTests.cs` - `Http10DecoderHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Headers_SingleHeader_ParsedCorrectly` | Fact | RFC1945-4-HDR-001: Single header parsed correctly |
| 2 | `Headers_CustomHeader_ParsedCorrectly` | Fact | RFC1945-4-HDR-002: Custom header parsed correctly |
| 3 | `Headers_MultipleCustomHeaders_AllParsed` | Fact | RFC1945-4-HDR-003: Multiple custom headers all parsed |
| 4 | `Headers_NamesAreCaseInsensitive` | Fact | RFC1945-4-HDR-004: Header names are case-insensitive |
| 5 | `Headers_FoldedHeader_IsContinuedCorrectly` | Fact | RFC1945-4-HDR-005: Obs-fold continuation accepted |
| 6 | `Headers_HeaderWithLeadingTrailingSpaces_AreTrimmed` | Fact | RFC1945-4-HDR-006: Header with leading/trailing spaces trimmed |
| 7 | `Headers_LfOnlyLineEnding_ParsedCorrectly` | Fact | RFC1945-4-HDR-007: LF-only line endings accepted in headers |
| 8 | `Should_MergeDoubleObsFold_When_TwoContinuationLines` | Fact | RFC1945-4-HDR-008: Obs-fold with multiple continuation lines merged |
| 9 | `Should_PreserveBothHeaders_When_DuplicateNonContentLength` | Fact | RFC1945-4-HDR-009: Duplicate response headers both accessible |
| 10 | `Should_ThrowInvalidHeader_When_NoColon` | Fact | RFC1945-4-HDR-010: Header without colon causes parse error |
| 11 | `Should_MatchCaseInsensitive_When_UppercaseHeaderName` | Fact | RFC1945-4-HDR-011: Case-insensitive Content-Length header matching |
| 12 | `Should_TrimWhitespace_When_HeaderValueHasExtraSpaces` | Fact | RFC1945-4-HDR-012: Header value whitespace trimmed |
| 13 | `Should_ThrowInvalidFieldName_When_SpaceInHeaderName` | Fact | RFC1945-4-HDR-013: Space in header name causes parse error |
| 14 | `Should_AcceptTab_When_HeaderValueContainsTab` | Fact | RFC1945-4-HDR-014: Tab character in header value accepted |
| 15 | `Should_AcceptResponse_When_ZeroHeaders` | Fact | RFC1945-4-HDR-015: Response with no headers except status-line accepted |
| 16 | `EdgeCase_HeaderWithoutValue_SkippedSafely` | Fact | RFC1945-4-HDR-016: Empty header value skipped safely |

#### `08_DecoderBodyTests.cs` - `Http10DecoderBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Body_WithContentLength_BodyReadCorrectly` | Fact | RFC1945-7-BODY-001: Content-Length body decoded to exact byte count |
| 2 | `Body_WithContentLength_ExactBytesRead` | Fact | RFC1945-7-BODY-002: Content-Length exact bytes read |
| 3 | `Body_WithZeroContentLength_EmptyBody` | Fact | RFC1945-7-BODY-003: Zero Content-Length produces empty body |
| 4 | `Body_WithoutContentLength_ReadsUntilEndOfData` | Fact | RFC1945-7-BODY-004: Body without Content-Length read until EOF |
| 5 | `Body_BinaryContent_PreservedExactly` | Fact | RFC1945-7-BODY-005: Binary content preserved exactly |
| 6 | `Body_ContentLengthHeader_SetOnContent` | Fact | RFC1945-7-BODY-006: Content-Length header set on content |
| 7 | `Body_NoBody_ResponseContentIsNull` | Fact | RFC1945-7-BODY-007: 204 No Content has no body |
| 8 | `EdgeCase_ContentLengthNegative_ThrowsDecoderException` | Fact | RFC1945-7-BODY-008: Negative Content-Length rejected |
| 9 | `Should_ThrowMultipleContentLength_When_DifferentValues` | Fact | RFC1945-7-BODY-009: Two different Content-Length values rejected |
| 10 | `Should_AcceptIdenticalContentLength_When_DuplicateValues` | Fact | RFC1945-7-BODY-010: Two identical Content-Length values accepted |
| 11 | `Should_HaveEmptyBody_When_304WithContentLength` | Fact | RFC1945-7-BODY-011: 304 Not Modified ignores Content-Length body |
| 12 | `Should_HaveEmptyBody_When_304WithoutContentLength` | Fact | RFC1945-7-BODY-012: 304 Not Modified without Content-Length has empty body |
| 13 | `Should_HaveEmptyBody_When_204NoContent` | Fact | RFC1945-7-BODY-013: 204 No Content has empty body |
| 14 | `Should_PreserveNullBytes_When_BodyContainsThem` | Fact | RFC1945-7-BODY-014: Body with null bytes preserved |
| 15 | `Should_Decode2MbBody_When_LargeContentLength` | Fact | RFC1945-7-BODY-015: 2 MB body decoded with correct Content-Length |
| 16 | `EdgeCase_VeryLargeHeader_HandledCorrectly` | Fact | RFC1945-7-BODY-016: Very large header handled correctly |
| 17 | `Should_TreatChunkedAsRawBody_When_Http10` | Fact | RFC1945-7-BODY-017: Transfer-Encoding chunked treated as raw body in HTTP/1.0 |
| 18 | `Should_ReadBodyViaEof_When_NoContentLength` | Fact | RFC1945-7-BODY-018: Body without Content-Length via TryDecodeEof |
| 19 | `EdgeCase_EmptyInput_ReturnsFalse` | Fact | RFC1945-7-BODY-019: Empty input returns false |

#### `05_EncoderIntegrationTests.cs` - `Http10EncoderIntegrationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Uri_NonAsciiPath_IsPercentEncoded` | Fact |  |
| 2 | `Uri_SpaceInPath_IsPercentEncoded` | Fact |  |
| 3 | `Uri_QueryStringWithSpecialChars_IsPreserved` | Fact |  |
| 4 | `Uri_EmptyPath_NormalizesToSlash` | Fact |  |
| 5 | `Uri_PathWithFragment_FragmentIsNotIncluded` | Fact |  |
| 6 | `Uri_NonStandardPort_IsNotInRequestLine` | Fact |  |
| 7 | `ContentType_WhenSetExplicitly_IsPreserved` | Fact |  |
| 8 | `ContentType_WithoutBody_IsNotSet` | Fact |  |
| 9 | `ContentType_NoDefaultIsInjected_WhenMissingAndBodyExists` | Fact |  |
| 10 | `BytesWritten_MatchesActualEncodedLength` | Fact |  |
| 11 | `BytesWritten_IsGreaterThanZero` | Fact |  |
| 12 | `BytesWritten_WithBody_IsLargerThanWithout` | Fact |  |
| 13 | `BytesWritten_BufferBeyondWrittenBytes_IsUntouched` | Fact |  |
| 14 | `Idempotent_SameRequestEncodedTwice_ProducesIdenticalOutput` | Fact |  |
| 15 | `Idempotent_SameGetRequestEncodedTwice_ProducesIdenticalOutput` | Fact |  |
| 16 | `Integration_MinimalGetRequest_IsFullyRfc1945Compliant` | Fact |  |
| 17 | `Integration_PostWithJsonBody_IsFullyRfc1945Compliant` | Fact |  |
| 18 | `Integration_HeadRequest_HasNoBody` | Fact |  |
| 19 | `Integration_RequestWithMultipleHeaders_AllWrittenCorrectly` | Fact |  |
| 20 | `Integration_ContentHeadersMergedWithRequestHeaders` | Fact |  |

#### `06_DecoderStatusLineTests.cs` - `Http10DecoderStatusLineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `StatusLine_200Ok_ParsedCorrectly` | Fact | RFC1945-6-SL-001: Status-Line format for HTTP/1.0 200 OK |
| 2 | `StatusLine_404NotFound_ParsedCorrectly` | Fact | RFC1945-6-SL-002: Status-Line format for HTTP/1.0 404 Not Found |
| 3 | `StatusLine_500InternalServerError_ParsedCorrectly` | Fact | RFC1945-6-SL-003: Status-Line format for HTTP/1.0 500 Internal Server Error |
| 4 | `StatusLine_301MovedPermanently_ParsedCorrectly` | Fact | RFC1945-6-SL-004: Status-Line format for HTTP/1.0 301 Moved Permanently |
| 5 | `StatusLine_ReasonPhraseWithMultipleWords_PreservedCompletely` | Fact | RFC1945-6-SL-005: Status-Line reason phrase with multiple words preserved |
| 6 | `StatusLine_Version_IsSetToHttp10` | Fact | RFC1945-6-SL-006: Status-Line HTTP version is 1.0 |
| 7 | `StatusLine_InvalidStatusCode_ThrowsDecoderException` | Fact | RFC1945-6-SL-007: Invalid status code rejected |
| 8 | `StatusLine_CommonStatusCodes_AllParsedCorrectly` | Theory | RFC1945-6-SL-008: Common RFC1945 status codes parsed |
| 9 | `Should_AcceptUnknownStatusCode_When_299` | Fact | RFC1945-6-SL-009: Unknown status code 299 accepted |
| 10 | `Should_RejectStatusCode_When_99` | Fact | RFC1945-6-SL-010: Status code 99 (too low) rejected |
| 11 | `Should_RejectStatusCode_When_1000` | Fact | RFC1945-6-SL-011: Status code 1000 (too high) rejected |
| 12 | `Should_AcceptLfOnlyLineEndings_When_Http10` | Fact | RFC1945-6-SL-012: LF-only line endings accepted in HTTP/1.0 |
| 13 | `Should_AcceptEmptyReasonPhrase_When_StatusCodeOnly` | Fact | RFC1945-6-SL-013: Empty reason phrase after status code accepted |
| 14 | `EdgeCase_OnlyHeaderSeparator_ThrowsDecoderException` | Fact | RFC1945-6-SL-014: Only header separator without status-line rejected |

### RFC7541 (242 tests)

#### `05_HeaderBlockDecodingTests.cs` - `HpackHeaderBlockDecodingTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Indexed_StaticIndex2_DecodesMethodGet` | Fact | HD-001: Static index 2 decodes to :method GET |
| 2 | `Indexed_StaticIndex4_DecodesPathSlash` | Fact | HD-002: Static index 4 decodes to :path / |
| 3 | `Indexed_StaticIndex7_DecodesSchemeHttps` | Fact | HD-003: Static index 7 decodes to :scheme https |
| 4 | `Indexed_StaticIndex61_DecodesWwwAuthenticate` | Fact | HD-004: Static index 61 (last static entry) decodes correctly |
| 5 | `Indexed_MultipleEntries_AllDecoded` | Fact | HD-005: Multiple indexed entries decoded in sequence |
| 6 | `Indexed_IndexZero_ThrowsHpackException` | Fact | HD-006: Index 0 in indexed representation throws HpackException (§2.3.3) |
| 7 | `Indexed_OutOfRangeIndex_ThrowsHpackException` | Fact | HD-007: Index beyond static+dynamic table throws HpackException (§2.3.3) |
| 8 | `LiteralIncrementalIndexing_NewName_AddedToDynamicTable` | Fact | HD-010: Literal+indexing new name/value → header decoded and added to dynamic table |
| 9 | `LiteralIncrementalIndexing_StaticNameIndex_NameFromStaticTable` | Fact | HD-011: Literal+indexing static name index → name resolved from static table |
| 10 | `LiteralIncrementalIndexing_SubsequentBlock_DynamicIndexResolvable` | Fact | HD-012: After Literal+indexing, dynamic entry indexed in subsequent block |
| 11 | `LiteralIncrementalIndexing_MultipleEntries_FifoOrder` | Fact | HD-013: Multiple Literal+indexing entries build dynamic table in FIFO order |
| 12 | `LiteralWithoutIndexing_NewName_NotAddedToDynamicTable` | Fact | HD-020: Literal without indexing new name → decoded but NOT in dynamic table |
| 13 | `LiteralWithoutIndexing_StaticNameIndex_NameFromStatic_NotAdded` | Fact | HD-021: Literal without indexing static name index → name from static table, not added |
| 14 | `LiteralWithoutIndexing_NeverIndex_IsFalse` | Fact | HD-022: Literal without indexing sets NeverIndex = false |
| 15 | `NeverIndexed_NewName_NeverIndexIsTrue` | Fact | HD-030: Never indexed new name → NeverIndex = true |
| 16 | `NeverIndexed_NotAddedToDynamicTable` | Fact | HD-031: Never indexed → NOT added to dynamic table |
| 17 | `NeverIndexed_StaticNameIndex_NeverIndexIsTrue` | Fact | HD-032: Never indexed static name index → name from static table, NeverIndex = true |
| 18 | `TableSizeUpdate_Zero_ClearsTable` | Fact | HD-040: Table size update to 0 at start → dynamic table cleared |
| 19 | `TableSizeUpdate_AtStart_Accepted` | Fact | HD-041: Table size update at start of block is accepted |
| 20 | `TableSizeUpdate_TwoUpdatesAtStart_BothAccepted` | Fact | HD-042: Two table size updates at start of block are both accepted (RFC allows) |
| 21 | `TableSizeUpdate_AfterIndexedHeader_ThrowsHpackException` | Fact | HD-043: Table size update after indexed header throws HpackException (§6.3) |
| 22 | `TableSizeUpdate_AfterLiteralHeader_ThrowsHpackException` | Fact | HD-044: Table size update after literal header throws HpackException (§6.3) |
| 23 | `TableSizeUpdate_ExceedsSettings_ThrowsHpackException` | Fact | HD-045: Table size update exceeding SETTINGS_HEADER_TABLE_SIZE throws (§4.2) |
| 24 | `ReadInteger_SingleByte_Zero` | Fact | PI-001: Single-byte integer value 0 decodes correctly |
| 25 | `ReadInteger_SingleByte_FitsInPrefix` | Fact | PI-002: Single-byte integer value fits within 7-bit prefix |
| 26 | `ReadInteger_MultiByte_300_FiveBitPrefix` | Fact | PI-003: Multi-byte integer 300 decoded from 5-bit prefix |
| 27 | `ReadInteger_MultiByte_1337_FiveBitPrefix` | Fact | PI-004: Multi-byte integer 1337 decoded from 5-bit prefix |
| 28 | `ReadInteger_Truncated_ThrowsHpackException` | Fact | PI-005: Truncated integer (no stop bit) throws HpackException (§5.1) |
| 29 | `ReadInteger_Overflow_ThrowsHpackException` | Fact | PI-006: Integer overflow exceeding int.MaxValue throws HpackException (§5.1) |
| 30 | `ReadInteger_PrefixBitsZero_ThrowsArgumentOutOfRangeException` | Fact | PI-008: ReadInteger with prefixBits=0 throws ArgumentOutOfRangeException (§5.1) |
| 31 | `ReadInteger_PrefixBitsNine_ThrowsArgumentOutOfRangeException` | Fact | PI-009: ReadInteger with prefixBits=9 throws ArgumentOutOfRangeException (§5.1) |
| 32 | `ReadInteger_TenContinuationBytes_ThrowsEncodingOverflowException` | Fact | PI-010: Integer with 10 continuation bytes triggers shift>=62 overflow guard (§5.1) |
| 33 | `ReadInteger_AtEndOfData_ThrowsHpackException` | Fact | PI-007: Reading integer at end of data throws HpackException (§5.1) |
| 34 | `HpackException_TwoArgCtor_SetsInnerException` | Fact | LF-006: HpackException two-arg constructor sets InnerException |
| 35 | `StringLength_ExceedsAvailableData_ThrowsHpackException` | Fact | LF-001: String length exceeds available data throws HpackException (§5.2) |
| 36 | `StringLength_Zero_Accepted` | Fact | LF-002: Empty string literal (length 0) is accepted |
| 37 | `StringLength_ExceedsMaxStringLength_ThrowsHpackException` | Fact | LF-003: String length exceeding maxStringLength throws HpackException |
| 38 | `StringValueLength_ExceedsMaxStringLength_ThrowsHpackException` | Fact | LF-005: String value length exceeding maxStringLength throws HpackException |
| 39 | `StringLiteral_NonHuffman_MultiByteContent_Decoded` | Fact | LF-004: Non-Huffman string with multi-byte content decoded correctly |
| 40 | `Decode_EmptyInput_ReturnsEmptyList` | Fact | ME-001: Empty byte array returns empty header list |
| 41 | `Decode_IndexedRepresentation_Index0_ThrowsHpackException` | Fact | ME-002: Index 0 in indexed representation throws HpackException (§2.3.3) |
| 42 | `Decode_DynamicIndex_TableEmpty_ThrowsHpackException` | Fact | ME-003: Dynamic index out of range (table empty) throws HpackException (§2.3.3) |
| 43 | `Decode_EmptyHeaderName_ThrowsHpackException` | Fact | ME-004: Empty header name in literal representation throws HpackException (§7.2) |
| 44 | `Decode_TruncatedIndexedField_ThrowsHpackException` | Fact | ME-005: Truncated indexed field (no data after prefix byte) throws HpackException |
| 45 | `Decode_TruncatedStringData_ThrowsHpackException` | Fact | ME-006: Truncated string data (fewer bytes than declared length) throws HpackException |
| 46 | `Decode_MixedRepresentationTypes_AllDecoded` | Fact | ME-007: Mixed representation types decoded correctly in one block |
| 47 | `RoundTrip_StaticOnlyHeaders_MatchAfterDecode` | Fact | RT-001: Encoder/decoder round-trip — all static-only headers |
| 48 | `RoundTrip_DynamicTable_Repopulated` | Fact | RT-002: Encoder/decoder round-trip — dynamic table populated correctly |
| 49 | `RoundTrip_SecondRequest_ReusesDynamicTable` | Fact | RT-003: Encoder/decoder round-trip — second request reuses dynamic table |
| 50 | `RoundTrip_HuffmanEnabled_CorrectlyDecoded` | Fact | RT-004: Encoder/decoder round-trip — Huffman encoding enabled |
| 51 | `RoundTrip_SensitiveHeaders_AutomaticallyNeverIndexed` | Fact | RT-005: Sensitive headers (authorization, cookie) are automatically NeverIndexed |

#### `06_TableSizeTests.cs` - `HpackHeaderListSizeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `HLS_001_Default_NoLimit_ManyHeadersSucceed` | Fact | HLS-001: Should_DecodeHeaders_When_NoLimitConfigured |
| 2 | `HLS_002_LimitZero_AnyHeaderThrows` | Fact | HLS-002: Should_Throw_When_LimitIsZeroAndAnyHeaderDecoded |
| 3 | `HLS_010_HeaderSizeEqualsLimit_Succeeds` | Fact | HLS-010: Should_Succeed_When_HeaderSizeExactlyEqualsLimit |
| 4 | `HLS_011_HeaderSizeBelowLimitByOne_Throws` | Fact | HLS-011: Should_Throw_When_HeaderSizeOneBelowLimit |
| 5 | `HLS_012_TwoHeadersExactlyAtLimit_Succeeds` | Fact | HLS-012: Should_Succeed_When_TwoHeadersSumToExactLimit |
| 6 | `HLS_013_SecondHeaderExceedsLimit_Throws` | Fact | HLS-013: Should_Throw_When_SecondHeaderExceedsLimit |
| 7 | `HLS_020_IndexedStaticHeader_CountedTowardLimit` | Fact | HLS-020: Should_CountIndexedStaticHeader_Toward_Limit |
| 8 | `HLS_021_IndexedStaticHeader_AtExactLimit_Succeeds` | Fact | HLS-021: Should_CountIndexedStaticHeader_WhenExactlyAtLimit |
| 9 | `HLS_022_LiteralIncrementalIndexing_CountedTowardLimit` | Fact | HLS-022: Should_CountLiteralIncrementalIndexing_Toward_Limit |
| 10 | `HLS_023_LiteralNeverIndex_CountedTowardLimit` | Fact | HLS-023: Should_CountLiteralNeverIndex_Toward_Limit |
| 11 | `HLS_024_IndexedDynamicHeader_CountedTowardLimit` | Fact | HLS-024: Should_CountIndexedDynamicHeader_Toward_Limit |
| 12 | `HLS_025_LiteralNoIndexing_CountedTowardLimit` | Fact | HLS-025: Should_CountLiteralNoIndexing_Toward_Limit |
| 13 | `HLS_030_CumulativeSizeAcrossMultipleHeaders` | Fact | HLS-030: Should_AccumulateSizeAcrossAllHeaders |
| 14 | `HLS_031_CumulativeResets_BetweenDecodeInvocations` | Fact | HLS-031: Should_ResetCumulativeSize_BetweenDecodeInvocations |
| 15 | `HLS_032_LargeValueHeader_ExceedsLimit` | Fact | HLS-032: Should_Throw_When_SingleLargeValueExceedsLimit |
| 16 | `HLS_040_NegativeSize_ThrowsHpackException` | Fact | HLS-040: Should_Throw_When_NegativeSizeProvided |
| 17 | `HLS_041_ZeroSize_IsValidAndEnforced` | Fact | HLS-041: Should_Accept_ZeroSizeLimit |
| 18 | `HLS_042_MaxIntSize_EffectivelyUnlimited` | Fact | HLS-042: Should_Accept_MaxIntSizeLimit_AsUnlimited |
| 19 | `HLS_043_RaiseLimit_PreviouslyFailingDecodesNowSucceed` | Fact | HLS-043: Should_RaiseLimit_AllowingPreviouslyFailingDecodes |
| 20 | `HLS_050_ExceptionMessage_ContainsRfcReference` | Fact | HLS-050: Should_ThrowWithRfcReference_InExceptionMessage |
| 21 | `HLS_051_ExceptionMessage_ContainsCompressionError` | Fact | HLS-051: Should_ThrowWithCompressionError_InExceptionMessage |
| 22 | `HLS_052_EmptyBlock_AlwaysSucceeds` | Fact | HLS-052: Should_Handle_EmptyHeaderBlock_UnderAnyLimit |
| 23 | `HLS_053_StaticEntry_SizeCorrectlyCalculated` | Fact | HLS-053: Should_CountStaticEntry_UsingCorrectOctetSize |
| 24 | `HLS_054_StaticEntry_ExceedsExactLimit_Throws` | Fact | HLS-054: Should_Throw_When_StaticEntryExceedsExactLimit |
| 25 | `HLS_055_MixedRepresentations_UnderLimit_Succeeds` | Fact | HLS-055: Should_Succeed_When_MixedRepresentationsUnderLimit |
| 26 | `HLS_057_SetMaxStringLength_NegativeValue_ThrowsHpackException` | Fact | HLS-057: SetMaxStringLength with negative value throws HpackException |
| 27 | `HLS_056_MixedRepresentations_ExceedLimit_Throws` | Fact | HLS-056: Should_Throw_When_MixedRepresentationsExceedLimit |

#### `HpackTests.cs` - `HpackTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Encode_IndexedStaticEntry_SingleByte` | Fact |  |
| 2 | `Encode_Decode_RoundTrip_PseudoHeaders` | Fact |  |
| 3 | `Encode_Decode_RoundTrip_WithHuffman` | Fact |  |
| 4 | `Decode_LiteralNewName_CorrectOrder` | Fact |  |
| 5 | `Decode_DynamicTableSizeUpdate_Respected` | Fact |  |
| 6 | `DynamicTable_Eviction_OldestEntryRemovedWhenFull` | Fact |  |
| 7 | `DynamicTable_EvictionOrder_NewestSurvives` | Fact |  |
| 8 | `DynamicTable_SizeTooBig_ThrowsHpackException` | Fact |  |
| 9 | `ReadInteger_FitsInPrefix_SingleByte` | Fact |  |
| 10 | `ReadInteger_MultiByteEncoding_DecodedCorrectly` | Fact |  |
| 11 | `ReadInteger_MaxValue_Accepted` | Fact |  |
| 12 | `ReadInteger_Overflow_ThrowsHpackException` | Fact |  |
| 13 | `ReadInteger_TruncatedData_ThrowsHpackException` | Fact |  |
| 14 | `Decode_Rfc7541_AppendixC2_FirstRequest` | Fact |  |
| 15 | `StaticTableEntry_RoundTrips` | Theory | 7541-st-001: Static table entry {0} [{1}:{2}] round-trips as indexed representation |
| 16 | `NeverIndexed_SensitiveHeader_FirstByteHas0x10Flag` | Theory | 7541-ni-001: {0} encoded with NeverIndexed byte pattern (0x10) |
| 17 | `NeverIndexed_SensitiveHeader_DoesNotGrowDynamicTable` | Theory | 7541-ni-002: {0} with NeverIndexed does not grow dynamic table |
| 18 | `NeverIndexed_AuthorizationHeader_PreservesFlag` | Fact | 7541-ni-003: Decoded authorization header preserves NeverIndex flag |
| 19 | `DynamicTable_IncrementallyIndexed_AddedAtIndex62` | Fact | 7541-2.3-001: Incrementally indexed header added at dynamic index 62 |
| 20 | `DynamicTable_OldestEntryEvicted_WhenFull` | Fact | 7541-2.3-002: Oldest entry evicted when dynamic table full |
| 21 | `DynamicTable_Resized_OnSettingsHeaderTableSize` | Fact | 7541-2.3-003: Dynamic table resized on SETTINGS_HEADER_TABLE_SIZE |
| 22 | `DynamicTable_SizeZero_EvictsAllEntries` | Fact | 7541-2.3-004: Dynamic table size 0 evicts all entries |
| 23 | `DynamicTable_SizeExceedingMax_ThrowsHpackException` | Fact | 7541-2.3-005: Table size exceeding maximum causes COMPRESSION_ERROR |
| 24 | `DynamicTable_EntrySize_NamePlusValuePlus32` | Fact | hpack-dt-001: Entry size counted as name + value + 32 overhead |
| 25 | `DynamicTable_SizeUpdatePrefix_EmittedAfterResize` | Fact | hpack-dt-002: Size update prefix emitted when table resized |
| 26 | `DynamicTable_ThreeEntries_EvictedFifoOrder` | Fact | hpack-dt-003: Three entries evicted in FIFO order |
| 27 | `Integer_SmallerThanPrefixLimit_EncodesInOneByte` | Fact | 7541-5.1-001: Integer smaller than prefix limit encodes in one byte |
| 28 | `Integer_AtPrefixLimit_RequiresContinuationBytes` | Fact | 7541-5.1-002: Integer at prefix limit requires continuation bytes |
| 29 | `Integer_MaxValue_2147483647_RoundTrips` | Fact | 7541-5.1-003: Maximum integer 2147483647 round-trips |
| 30 | `Integer_ExceedingMaxInt_ThrowsHpackException` | Fact | 7541-5.1-004: Integer exceeding 2^31-1 causes COMPRESSION_ERROR |
| 31 | `Integer_BoundaryValues_ForPrefixBits` | Theory | hpack-int-001: Integer encoding with {0}-bit prefix |
| 32 | `StringLiteral_Plain_Decoded` | Fact | 7541-5.2-001: Plain string literal decoded |
| 33 | `StringLiteral_Huffman_Decoded` | Fact | 7541-5.2-002: Huffman-encoded string decoded |
| 34 | `StringLiteral_Empty_Decoded` | Fact | 7541-5.2-003: Empty string literal decoded |
| 35 | `StringLiteral_LargerThan8KB_DecodedWithoutTruncation` | Fact | 7541-5.2-004: String larger than 8KB decoded |
| 36 | `StringLiteral_MalformedHuffman_ThrowsHpackException` | Fact | 7541-5.2-005: Malformed Huffman data causes COMPRESSION_ERROR |
| 37 | `StringLiteral_NonOneEosPaddingBits_ThrowsHpackException` | Fact | hpack-str-001: Non-1 EOS padding bits cause COMPRESSION_ERROR |
| 38 | `StringLiteral_EosPaddingMoreThan7Bits_ThrowsHpackException` | Fact | hpack-str-002: EOS padding > 7 bits causes COMPRESSION_ERROR |
| 39 | `IndexedHeader_DynamicEntry_RetrievedAtIndex62Plus` | Fact | 7541-6.1-002: Dynamic table entry at index 62+ retrieved |
| 40 | `IndexedHeader_OutOfRange_ThrowsHpackException` | Fact | 7541-6.1-003: Index out of range causes COMPRESSION_ERROR |
| 41 | `IndexedHeader_Index0_ThrowsHpackException` | Fact | hpack-idx-001: Index 0 is invalid per RFC 7541 §6.1 |
| 42 | `LiteralHeader_IncrementalIndexing_AddsToTable` | Fact | 7541-6.2-001: Incremental indexing adds entry to dynamic table |
| 43 | `LiteralHeader_WithoutIndexing_NotAddedToTable` | Fact | 7541-6.2-002: Without-indexing literal not added to dynamic table |
| 44 | `LiteralHeader_NeverIndexed_NotAddedToTable_FlagPreserved` | Fact | 7541-6.2-003: NeverIndexed literal not added to table |
| 45 | `LiteralHeader_IndexedNameWithLiteralValue_Decoded` | Fact | 7541-6.2-004: Literal with indexed name and literal value decoded |
| 46 | `LiteralHeader_LiteralNameAndValue_Decoded` | Fact | 7541-6.2-005: Literal with literal name and literal value decoded |
| 47 | `AppendixC2_1_FirstRequest_NoHuffman` | Fact | 7541-C.2-001: RFC 7541 Appendix C.2.1 decode |
| 48 | `AppendixC2_2_SecondRequest_DynamicTableReferenced` | Fact | 7541-C.2-002: RFC 7541 Appendix C.2.2 decode (dynamic table) |
| 49 | `AppendixC2_3_ThirdRequest_TableStateCorrect` | Fact | 7541-C.2-003: RFC 7541 Appendix C.2.3 decode |
| 50 | `AppendixC3_AllThreeRequests_WithHuffman` | Fact | 7541-C.3-001: RFC 7541 Appendix C.3 decode with Huffman |
| 51 | `AppendixC4_1_FirstResponse_NoHuffman` | Fact | 7541-C.4-001: RFC 7541 Appendix C.4.1 decode |
| 52 | `AppendixC4_2_SecondResponse_DynamicTableReused` | Fact | 7541-C.4-002: RFC 7541 Appendix C.4.2 decode (dynamic table reused) |
| 53 | `AppendixC4_3_ThirdResponse_CorrectTableStateAfterC4_2` | Fact | 7541-C.4-003: RFC 7541 Appendix C.4.3 decode |
| 54 | `AppendixC5_ResponsesWithHuffman_DecodeCorrectly` | Fact | 7541-C.5-001: RFC 7541 Appendix C.5 decode with Huffman |
| 55 | `AppendixC6_LargeCookieResponses_DecodeCorrectly` | Fact | 7541-C.6-001: RFC 7541 Appendix C.6 large cookie responses |
| 56 | `Decode_Rfc7541_AppendixC3_AllThreeRequests` | Fact |  |

#### `01_StaticTableTests.cs` - `HpackStaticTableTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `StaticTable_Count_IsExactly61` | Fact | ST-001: Static table contains exactly 61 entries |
| 2 | `StaticTable_Array_Has62Slots` | Fact | ST-002: Static table array has 62 slots (index 0 reserved) |
| 3 | `StaticTable_IndexZero_IsReserved` | Fact | ST-003: Index 0 is reserved (empty name and value) |
| 4 | `StaticTable_AllEntries_HaveCorrectNameAndValue` | Theory | ST-004: All 61 static entries have correct name and value |
| 5 | `Decoder_Index2_Returns_MethodGet` | Fact | ST-010: Decode index 2 → :method=GET |
| 6 | `Decoder_Index3_Returns_MethodPost` | Fact | ST-011: Decode index 3 → :method=POST |
| 7 | `Decoder_Index4_Returns_PathRoot` | Fact | ST-012: Decode index 4 → :path=/ |
| 8 | `Decoder_Index5_Returns_PathIndexHtml` | Fact | ST-013: Decode index 5 → :path=/index.html |
| 9 | `Decoder_Index7_Returns_SchemeHttps` | Fact | ST-014: Decode index 7 → :scheme=https |
| 10 | `Decoder_Index8_Returns_Status200` | Fact | ST-015: Decode index 8 → :status=200 |
| 11 | `Decoder_Index13_Returns_Status404` | Fact | ST-016: Decode index 13 → :status=404 |
| 12 | `Decoder_Index16_Returns_AcceptEncoding` | Fact | ST-017: Decode index 16 → accept-encoding=gzip, deflate |
| 13 | `Decoder_Index61_Returns_WwwAuthenticate` | Fact | ST-018: Decode index 61 → www-authenticate='' |
| 14 | `Encoder_MethodGet_ProducesByte0x82` | Fact | ST-020: Encode :method=GET produces single byte 0x82 |
| 15 | `Encoder_MethodPost_ProducesByte0x83` | Fact | ST-021: Encode :method=POST produces single byte 0x83 |
| 16 | `Encoder_PathRoot_ProducesByte0x84` | Fact | ST-022: Encode :path=/ produces single byte 0x84 |
| 17 | `Encoder_SchemeHttps_ProducesByte0x87` | Fact | ST-023: Encode :scheme=https produces single byte 0x87 |
| 18 | `Encoder_Status200_ProducesByte0x88` | Fact | ST-024: Encode :status=200 produces single byte 0x88 |
| 19 | `Encoder_Status404_ProducesByte0x8D` | Fact | ST-025: Encode :status=404 produces single byte 0x8D |
| 20 | `Decoder_IndexZero_ThrowsHpackException` | Fact | ST-030: Decode index 0 (0x80) throws HpackException — reserved index |
| 21 | `Decoder_Index62_EmptyDynamicTable_ThrowsHpackException` | Fact | ST-031: Decode index 62 (0xBE) with empty dynamic table throws HpackException |
| 22 | `Decoder_Index100_EmptyDynamicTable_ThrowsHpackException` | Fact | ST-032: Decode index 100 with empty dynamic table throws HpackException |
| 23 | `Decoder_VeryLargeIndex_ThrowsHpackException` | Fact | ST-033: Decode very large index throws HpackException |
| 24 | `Encoder_AuthorityWithCustomValue_UsesStaticNameIndex1` | Fact | ST-040: Encode :authority with custom value uses static index 1 for name |
| 25 | `Encoder_AcceptEncodingWithCustomValue_UsesStaticNameIndex16` | Fact | ST-041: Encode accept-encoding with custom value uses static index 16 for name |
| 26 | `RoundTrip_AllPseudoHeaders_ViaStaticTable` | Fact | ST-050: Round-trip encode/decode all pseudo-headers via static table |
| 27 | `Encoder_AllStaticFullMatches_ProduceSingleByte` | Fact | ST-051: All 61 static full-match entries produce exactly 1 byte when encoded |
| 28 | `Decoder_AllStaticIndices_ResolveCorrectly` | Fact | ST-052: Decoder can decode all 61 static indices (Theory via loop) |

#### `02_DynamicTableTests.cs` - `HpackDynamicTableTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `DynamicTable_Empty_HasSizeZero` | Fact | DT-001: Empty table has CurrentSize = 0 |
| 2 | `DynamicTable_Empty_HasCountZero` | Fact | DT-002: Empty table has Count = 0 |
| 3 | `DynamicTable_Default_MaxSizeIs4096` | Fact | DT-003: Default MaxSize = 4096 (RFC 7541 §4.2) |
| 4 | `DynamicTable_GetEntry_EmptyTable_ReturnsNull` | Fact | DT-004: GetEntry(1) returns null on empty table |
| 5 | `DynamicTable_GetEntry_IndexZero_ReturnsNull` | Fact | DT-005: GetEntry(0) returns null (out of range) |
| 6 | `DynamicTable_Add_SingleEntry_SizeIsCorrect` | Fact | DT-010: Single entry size = UTF8(name) + UTF8(value) + 32 |
| 7 | `DynamicTable_Add_TwoEntries_SizeAccumulates` | Fact | DT-011: Two entries sum their individual sizes |
| 8 | `DynamicTable_Add_EmptyNameValue_Adds32Bytes` | Fact | DT-012: Empty name and value still adds 32 bytes overhead |
| 9 | `DynamicTable_Add_MultiByteName_SizeCounted_AsUtf8Bytes` | Fact | DT-013: UTF-8 multi-byte name counted in bytes, not chars |
| 10 | `DynamicTable_Add_MultiByteValue_SizeCounted_AsUtf8Bytes` | Fact | DT-014: UTF-8 multi-byte value counted in bytes, not chars |
| 11 | `DynamicTable_GetEntry1_ReturnsMostRecent` | Fact | DT-020: GetEntry(1) returns most recently added entry |
| 12 | `DynamicTable_GetEntry2_ReturnsSecondMostRecent` | Fact | DT-021: GetEntry(2) returns second-most-recently added entry |
| 13 | `DynamicTable_FifoOrdering_OldestIsAtHighestIndex` | Fact | DT-022: FIFO — oldest entry is at index Count |
| 14 | `DynamicTable_GetEntry_BeyondCount_ReturnsNull` | Fact | DT-023: GetEntry beyond Count returns null |
| 15 | `DynamicTable_Eviction_RemovesOldestFirst` | Fact | DT-030: Eviction removes oldest entry first (FIFO) |
| 16 | `DynamicTable_AddOversizedEntry_ClearsTable` | Fact | DT-031: Entry larger than MaxSize clears entire table (RFC §4.4) |
| 17 | `DynamicTable_SetMaxSizeZero_EvictsAll` | Fact | DT-032: SetMaxSize(0) evicts all entries |
| 18 | `DynamicTable_AddToFullTable_EvictsOldestToFit` | Fact | DT-033: Adding to full table evicts oldest to make room |
| 19 | `DynamicTable_AddEntry_EvictsMultipleOldEntries` | Fact | DT-034: Multiple evictions until size fits new entry |
| 20 | `DynamicTable_SetMaxSize_UpdatesMaxSize` | Fact | DT-040: SetMaxSize updates MaxSize property |
| 21 | `DynamicTable_SetMaxSize_SameValue_NoChange` | Fact | DT-041: SetMaxSize to same value is idempotent |
| 22 | `DynamicTable_SetMaxSize_Negative_Throws` | Fact | DT-042: Negative MaxSize throws HpackException |
| 23 | `DynamicTable_SetMaxSize_ExactEntrySize_Keeps` | Fact | DT-043: SetMaxSize to exact entry size keeps that entry |
| 24 | `DynamicTable_SetMaxSize_OneLessThanEntry_EvictsIt` | Fact | DT-044: SetMaxSize to one less than entry size evicts it |
| 25 | `Decoder_TableSizeUpdate_AtStart_Accepted` | Fact | TS-001: Table size update at start of block is accepted |
| 26 | `Decoder_TwoTableSizeUpdates_AtStart_BothAccepted` | Fact | TS-002: Two size updates at start of block are both accepted |
| 27 | `Decoder_TableSizeUpdate_AfterIndexedHeader_Throws` | Fact | TS-003: Table size update after indexed header throws HpackException |
| 28 | `Decoder_TableSizeUpdate_AfterLiteralWithIndexing_Throws` | Fact | TS-004: Table size update after literal-with-indexing throws HpackException |
| 29 | `Decoder_TableSizeUpdate_ExceedingSettings_Throws` | Fact | TS-005: Table size update exceeding SETTINGS throws HpackException |
| 30 | `Decoder_TableSizeUpdate_ExactSettings_Accepted` | Fact | TS-006: Size update to exact SETTINGS value is accepted |
| 31 | `Encoder_AcknowledgeTableSizeChange_EmitsSizeUpdateBeforeHeaders` | Fact | ET-001: AcknowledgeTableSizeChange emits size update prefix at next encode |
| 32 | `Encoder_AcknowledgeTableSizeChange_SizeUpdateThenHeader` | Fact | ET-002: After AcknowledgeTableSizeChange, next encode contains header after update |
| 33 | `Encoder_AcknowledgeTableSizeChange_Zero_EmitsZeroUpdate` | Fact | ET-003: AcknowledgeTableSizeChange(0) emits zero-size update |
| 34 | `Encoder_AcknowledgeTableSizeChange_Negative_Throws` | Fact | ET-004: AcknowledgeTableSizeChange with negative size throws HpackException |
| 35 | `Encoder_AcknowledgeTableSizeChange_OnlyEmittedOnce` | Fact | ET-005: Second encode after AcknowledgeTableSizeChange does NOT re-emit update |
| 36 | `EncoderDecoder_DynamicEntry_AccessibleViaIndex` | Fact | ES-001: Dynamic entry added by encode is accessible via index on decode |
| 37 | `EncoderDecoder_MultipleEntries_MaintainFifoIndexing` | Fact | ES-002: Multiple dynamic entries maintain FIFO indexing on both sides |
| 38 | `EncoderDecoder_MultipleBlocks_StayInSync` | Fact | ES-003: Encoder and decoder stay in sync across multiple header blocks |
| 39 | `EncoderDecoder_TableSizeChange_Synchronized` | Fact | ES-004: Synchronized table size change via AcknowledgeTableSizeChange |
| 40 | `EncoderDecoder_NeverIndexed_NotAddedToDynamicTable` | Fact | ES-005: Never-indexed headers not added to dynamic table on either side |
| 41 | `DynamicTable_FillsExactlyToMaxSize_NoEviction` | Fact | DT-050: Table fills exactly to MaxSize without eviction |
| 42 | `DynamicTable_OneByteBeyondMaxSize_EvictsOldest` | Fact | DT-051: Adding one more byte beyond MaxSize evicts oldest |
| 43 | `DynamicTable_HighVolumeAdds_SizeRemainsWithinMaxSize` | Fact | DT-052: 100 sequential adds with small MaxSize keeps size bounded |
| 44 | `DynamicTable_AfterClear_CanAddNewEntries` | Fact | DT-053: After clear via SetMaxSize(0), new entries can be added again |
| 45 | `DynamicTable_NegativeIndex_ReturnsNull` | Fact | DT-054: Negative index returns null without throwing |

#### `04_HuffmanTests.cs` - `HuffmanDecoderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `HF001_Decode_WwwExampleCom_MatchesRfc` | Fact | HF-001: Decode 'www.example.com' matches RFC 7541 Appendix C |
| 2 | `HF002_Decode_NoCache_MatchesRfc` | Fact | HF-002: Decode 'no-cache' matches RFC 7541 Appendix C |
| 3 | `HF003_Decode_EmptyInput_ReturnsEmpty` | Fact | HF-003: Decode empty input returns empty byte array |
| 4 | `HF004_Decode_SingleChar_A` | Fact | HF-004: Decode single ASCII char 'a' (5-bit code 00011 + padding 111) |
| 5 | `HF005_Decode_Digits_5BitCodes` | Fact | HF-005: Decode digits '0' through '9' (all 5-bit codes) |
| 6 | `HF006_Decode_Status200` | Fact | HF-006: Decode common HTTP status '200' |
| 7 | `HF007_Decode_ContentTypeApplicationJson` | Fact | HF-007: Decode HTTP header 'content-type: application/json' |
| 8 | `HF010_AllPrintableAscii_RoundTrip` | Fact | HF-010: All 128 printable ASCII chars encode and decode correctly |
| 9 | `HF011_AllByteValues_RoundTrip` | Fact | HF-011: All 256 byte values encode and decode correctly |
| 10 | `HF012_MixedCodeLengths_Decode` | Fact | HF-012: Multi-byte sequence with mixed code lengths decodes correctly |
| 11 | `HF013_LongString_RoundTrip` | Fact | HF-013: Long string (256 bytes) round-trips correctly |
| 12 | `HF014_CustomKeyValue_RoundTrip` | Fact | HF-014: 'custom-key' and 'custom-value' from RFC 7541 Appendix C.5 |
| 13 | `EO001_FourBytesAllOnes_EosAtBit30_Throws` | Fact | EO-001: 4 bytes all-ones triggers EOS at bit 30 — throws HpackException |
| 14 | `EO002_ValidSymbolThenEos_Throws` | Fact | EO-002: 'a' then EOS (bytes [0x1F, 0xFF, 0xFF, 0xFF, 0xFF]) — throws after valid symbol |
| 15 | `EO003_ThreeBytesAllOnesPlusBits_Throws` | Fact | EO-003: 3 bytes of all-ones triggers EOS at bit 30 — throws |
| 16 | `EO004_TwoCharsBeforeEos_Throws` | Fact | EO-004: Two valid chars then EOS in stream — throws |
| 17 | `EO005_SingleByteFF_IsValidPaddingForSymbolWithLongCode` | Fact | EO-005: Single byte 0xFF does not trigger EOS (only 8 ones, need 30) — valid padding |
| 18 | `PA001_ValidPadding_A_3Bits` | Fact | PA-001: Valid 3-bit all-ones padding for 'a' [0x1F] — no exception |
| 19 | `PA002_InvalidPadding_A_LastBitZero_Throws` | Fact | PA-002: Invalid padding for 'a' — last bit zero [0x1E] — throws |
| 20 | `PA003_InvalidPadding_A_MiddleBitZero_Throws` | Fact | PA-003: Invalid padding for 'a' — middle bit zero [0x1B] — throws |
| 21 | `PA004_OverlongPadding_ExtraNullByte_Throws` | Fact | PA-004: Overlong padding — extra null byte after valid 'a' — throws |
| 22 | `PA005_OverlongPadding_ExtraFFByte_Throws` | Fact | PA-005: Overlong padding — extra 0xFF byte after valid 'a' — throws |
| 23 | `PA006_ValidPadding_7Bits` | Fact | PA-006: Valid 7-bit all-ones padding — longest valid padding |
| 24 | `PA007_ZeroBitPadding_ByteAligned_Valid` | Fact | PA-007: Padding of exactly zero bits (symbol fills byte exactly) — valid |
| 25 | `PA008_TwoNullBytes_NoPaddingBits_Throws` | Fact | PA-008: Two-byte all-zero input has no valid padding — throws |
| 26 | `IC001_SingleByte_0x80_IncompletePrefix` | Fact | IC-001: Single byte 0x80 is incomplete prefix — throws |
| 27 | `IC002_SingleByte_0x01_InvalidPadding` | Fact | IC-002: Empty-ish single byte 0x01 is invalid padding — throws |
| 28 | `IC003_TwoBytesOverlongIncomplete_Throws` | Fact | IC-003: Two bytes forming overlong incomplete sequence — throws |
| 29 | `RT001_RoundTrip_HttpStrings` | Theory | RT-001..007: Round-trip various HTTP-relevant strings |
| 30 | `RT008_RoundTrip_HeaderValues` | Theory | RT-008..012: Round-trip header values |
| 31 | `ED001_Encode_PaddingIsAllOnes` | Fact | ED-001: Encode always uses all-ones padding (MSBs of EOS) |
| 32 | `ED002_Encode_CompressesCommonHeaders` | Fact | ED-002: Encode produces output shorter than or equal to input + 1 for common headers |
| 33 | `ED003_Encode_SingleByte_AtMost4Bytes` | Fact | ED-003: Encode of single byte produces at most 1 byte (all symbols <= 30 bits) |
| 34 | `ED004_Encode_WwwExampleCom_MatchesRfc` | Fact | ED-004: Encode 'www.example.com' produces exact RFC 7541 Appendix C bytes |
| 35 | `ED005_Encode_NoCache_MatchesRfc` | Fact | ED-005: Encode 'no-cache' produces exact RFC 7541 Appendix C bytes |

### RFC9110 (41 tests)

#### `03_ContentEncodingIntegrationTests.cs` - `ContentEncodingIntegrationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecompressStackedEncodings_GzipThenBr` | Fact | RFC9110-8.4-stacked-001: Should_DecompressStackedEncodings_GzipThenBr |
| 2 | `Should_DecompressStackedEncodings_DeflateGzipBr` | Fact | RFC9110-8.4-stacked-002: Should_DecompressStackedEncodings_DeflateGzipBr |
| 3 | `Should_DecompressStackedEncodings_RemoveAllHeaders` | Fact | RFC9110-8.4-stacked-003: Should_DecompressStackedEncodings_RemoveAllHeaders |
| 4 | `Should_DecompressStackedEncodings_UpdateContentLength` | Fact | RFC9110-8.4-stacked-004: Should_DecompressStackedEncodings_UpdateContentLength |
| 5 | `Should_AddAcceptEncoding_When_NotAlreadySet` | Fact | RFC9110-8.4-accept-001: Should_AddAcceptEncoding_When_NotAlreadySet |
| 6 | `Should_NotOverrideAcceptEncoding_When_AlreadySet` | Fact | RFC9110-8.4-accept-002: Should_NotOverrideAcceptEncoding_When_AlreadySet |
| 7 | `Should_AddAcceptEncoding_For_Post_With_Body` | Fact | RFC9110-8.4-accept-003: Should_AddAcceptEncoding_For_Post_With_Body |
| 8 | `Should_AddAcceptEncoding_For_Put_Request` | Fact | RFC9110-8.4-accept-004: Should_AddAcceptEncoding_For_Put_Request |
| 9 | `Should_HandleRequestResponseWithCompressionCycle` | Fact | RFC9110-8.4-roundtrip-001: Should_HandleRequestResponseWithCompressionCycle |
| 10 | `Should_PreserveContentOnNoEncoding_WithAcceptEncodingHeader` | Fact | RFC9110-8.4-roundtrip-002: Should_PreserveContentOnNoEncoding_WithAcceptEncodingHeader |
| 11 | `Should_SupportBrotliRoundTrip` | Fact | RFC9110-8.4-roundtrip-003: Should_SupportBrotliRoundTrip |
| 12 | `Should_DecodeStackedEncodingsConsistentlyAcrossVersions` | Fact | RFC9110-8.4-compat-001: Should_DecodeStackedEncodingsConsistentlyAcrossVersions |
| 13 | `Should_HandleEncodingMismatch_DeflateVsGzip` | Fact | RFC9110-8.4-compat-002: Should_HandleEncodingMismatch_DeflateVsGzip |

#### `02_ContentEncodingDeflateTests.cs` - `ContentEncodingDeflateTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecompressDeflate_When_ContentEncodingIsDeflate` | Fact | RFC9110-8.4-deflate-001: Should_DecompressDeflate_When_ContentEncodingIsDeflate |
| 2 | `Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity` | Fact | RFC9110-8.4-deflate-002: Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity |
| 3 | `Should_LeaveBodyUnchanged_When_NoContentEncoding` | Fact | RFC9110-8.4-deflate-003: Should_LeaveBodyUnchanged_When_NoContentEncoding |
| 4 | `Should_ThrowDecompressionFailed_When_UnknownEncoding` | Fact | RFC9110-8.4-deflate-004: Should_ThrowDecompressionFailed_When_UnknownEncoding |
| 5 | `Should_DecompressBrotli_When_ContentEncodingIsBr` | Fact | RFC9110-8.4-br-001: Should_DecompressBrotli_When_ContentEncodingIsBr |
| 6 | `Should_DecompressBrotli_LargeContent` | Fact | RFC9110-8.4-br-002: Should_DecompressBrotli_LargeContent |
| 7 | `Should_DecompressDeflate_In_Http10_Response` | Fact | RFC9110-8.4-deflate-h10-001: Should_DecompressDeflate_In_Http10_Response |
| 8 | `Should_DecompressBrotli_In_Http10_Response` | Fact | RFC9110-8.4-br-h10-001: Should_DecompressBrotli_In_Http10_Response |
| 9 | `Should_DecompressDeflate_In_Http2_Response` | Fact | RFC9110-8.4-deflate-h2-001: Should_DecompressDeflate_In_Http2_Response |
| 10 | `Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding` | Fact | RFC9110-8.4-identity-h2-001: Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding |
| 11 | `Should_NotConfuse_TransferEncoding_WithContentEncoding` | Fact | RFC9110-8.4-distinction-001: Should_NotConfuse_TransferEncoding_WithContentEncoding |

#### `01_ContentEncodingGzipTests.cs` - `ContentEncodingGzipTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecompressGzip_When_ContentEncodingIsGzip` | Fact | RFC9110-8.4-gzip-001: Should_DecompressGzip_When_ContentEncodingIsGzip |
| 2 | `Should_RemoveContentEncodingHeader_After_Decompression` | Fact | RFC9110-8.4-gzip-002: Should_RemoveContentEncodingHeader_After_Decompression |
| 3 | `Should_UpdateContentLength_After_Decompression` | Fact | RFC9110-8.4-gzip-003: Should_UpdateContentLength_After_Decompression |
| 4 | `Should_DecompressGzip_CaseInsensitive` | Fact | RFC9110-8.4-gzip-004: Should_DecompressGzip_CaseInsensitive |
| 5 | `Should_DecompressXGzip_WhenContentEncodingIsXGzip` | Fact | RFC9110-8.4-gzip-005: Should_DecompressXGzip_WhenContentEncodingIsXGzip |
| 6 | `Should_HandleEmptyGzip_WhenEmptyBodyWithGzipEncoding` | Fact | RFC9110-8.4-gzip-006: Should_HandleEmptyGzip_WhenEmptyBodyWithGzipEncoding |
| 7 | `Should_ThrowDecompressionFailed_WhenCorruptGzipData` | Fact | RFC9110-8.4-gzip-007: Should_ThrowDecompressionFailed_WhenCorruptGzipData |
| 8 | `Should_DecompressLargeGzipBody_64KB` | Fact | RFC9110-8.4-gzip-008: Should_DecompressLargeGzipBody_64KB |
| 9 | `Should_DecompressGzip_Utf8_MultibyteContent` | Fact | RFC9110-8.4-gzip-009: Should_DecompressGzip_Utf8_MultibyteCotent |
| 10 | `Should_NotDecompress_204_NoBodyResponse` | Fact | RFC9110-8.4-gzip-010: Should_NotDecompress_204_NoBodyResponse |
| 11 | `Should_DecompressGzip_When_Http10_ContentEncoding` | Fact | RFC9110-8.4-gzip-h10-001: Should_DecompressGzip_When_Http10_ContentEncoding |
| 12 | `Should_RemoveContentEncoding_After_Http10_Decompression` | Fact | RFC9110-8.4-gzip-h10-002: Should_RemoveContentEncoding_After_Http10_Decompression |
| 13 | `Should_UpdateContentLength_After_Http10_Decompression` | Fact | RFC9110-8.4-gzip-h10-003: Should_UpdateContentLength_After_Http10_Decompression |
| 14 | `Should_DecompressGzip_When_Http2_ContentEncoding` | Fact | RFC9110-8.4-gzip-h2-001: Should_DecompressGzip_When_Http2_ContentEncoding |
| 15 | `Should_RemoveContentEncoding_After_Http2_Decompression` | Fact | RFC9110-8.4-gzip-h2-002: Should_RemoveContentEncoding_After_Http2_Decompression |
| 16 | `Should_UpdateContentLength_After_Http2_Decompression` | Fact | RFC9110-8.4-gzip-h2-003: Should_UpdateContentLength_After_Http2_Decompression |
| 17 | `Should_DecompressBrotli_ViaHttp2` | Fact | RFC9110-8.4-gzip-h2-004: Should_DecompressBrotli_ViaHttp2 |

### RFC9111 (67 tests)

#### `04_CacheStoreTests.cs` - `CacheStoreTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `IsCacheable_200_True` | Fact | RFC-9111-§3.1: GET 200 with max-age is cacheable |
| 2 | `IsCacheable_CacheableStatuses` | Theory | RFC-9111-§3.1: cacheable status codes |
| 3 | `IsCacheable_500_False` | Fact | RFC-9111-§3.1: 500 status is not cacheable by default |
| 4 | `ShouldStore_Get200_True` | Fact | RFC-9111-§3: GET 200 with max-age should be stored |
| 5 | `ShouldStore_Post200_False` | Fact | RFC-9111-§3: POST 200 should not be stored (unsafe method) |
| 6 | `ShouldStore_RequestNoStore_False` | Fact | RFC-9111-§5.2.1.5: no-store on request → should not store |
| 7 | `ShouldStore_ResponseNoStore_False` | Fact | RFC-9111-§5.2.2.5: no-store on response → should not store |
| 8 | `Get_EmptyStore_ReturnsNull` | Fact | RFC-9111-§4: Get on empty store returns null |
| 9 | `Put_ThenGet_ReturnsCachedEntry` | Fact | RFC-9111-§3: Put then Get same URI returns entry |
| 10 | `Invalidate_RemovesEntry` | Fact | RFC-9111-§4.4: Invalidate removes entry for URI |
| 11 | `Vary_DifferentAccept_Miss` | Fact | RFC-9111-§4.1: Vary header — different Accept is a cache miss |
| 12 | `Vary_MatchingAccept_Hit` | Fact | RFC-9111-§4.1: Vary header — matching Accept is a cache hit |
| 13 | `Vary_Star_NeverMatches` | Fact | RFC-9111-§4.1: Vary: * never matches |
| 14 | `LruEviction_WhenMaxEntriesExceeded` | Fact | RFC-9111-§3: LRU eviction when MaxEntries exceeded |

#### `05_CacheIntegrationTests.cs` - `CacheIntegrationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `FullCycle_PutThenGet_FreshHit` | Fact | RFC-9111-§4: PUT response then GET same URI → Fresh hit |
| 2 | `FullCycle_Stale_MustRevalidate` | Fact | RFC-9111-§4: PUT response then time passes → Stale → must revalidate |
| 3 | `MustRevalidate_WhenStale_ReturnsMustRevalidate` | Fact | RFC-9111-§5.2.2.8: stale + must-revalidate → MustRevalidate status |
| 4 | `StaleWithoutMustRevalidate_ReturnsStale` | Fact | RFC-9111-§4.2: stale without must-revalidate → Stale status |
| 5 | `NoCache_OnRequest_ForcesMustRevalidateEvenIfFresh` | Fact | RFC-9111-§5.2.1.4: no-cache on request forces revalidation even if fresh |
| 6 | `OnlyIfCached_FreshEntry_ReturnsFresh` | Fact | RFC-9111-§5.2.1.7: only-if-cached + fresh entry → Fresh |
| 7 | `MaxStale_300_AcceptsStaleEntryWithinTolerance` | Fact | RFC-9111-§5.2.1.2: max-stale=300 accepts stale entry within tolerance |
| 8 | `UnsafeMethod_Post_InvalidatesGetEntry` | Fact | RFC-9111-§4.4: unsafe method (POST) invalidates related GET cache entry |

#### `03_ConditionalRequestTests.cs` - `ConditionalRequestTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ETag_AddsIfNoneMatch` | Fact | RFC-9111-§4.3.1: entry with ETag adds If-None-Match header |
| 2 | `LastModified_AddsIfModifiedSince` | Fact | RFC-9111-§4.3.1: entry with Last-Modified adds If-Modified-Since header |
| 3 | `BothETagAndLastModified_AddsBothHeaders` | Fact | RFC-9111-§4.3.1: entry with both ETag and Last-Modified adds both headers |
| 4 | `NoETagNorLastModified_NoConditionalHeaders` | Fact | RFC-9111-§4.3.1: entry with neither ETag nor Last-Modified adds no conditional headers |
| 5 | `ConditionalRequest_PreservesUriAndMethod` | Fact | RFC-9111-§4.3.1: conditional request preserves original URI and method |
| 6 | `CanRevalidate_False_WhenNoValidators` | Fact | RFC-9111-§4.3.2: CanRevalidate returns false for entry without ETag or Last-Modified |
| 7 | `CanRevalidate_True_WhenETag` | Fact | RFC-9111-§4.3.2: CanRevalidate returns true when ETag present |
| 8 | `CanRevalidate_True_WhenLastModified` | Fact | RFC-9111-§4.3.2: CanRevalidate returns true when Last-Modified present |
| 9 | `MergeNotModified_StatusCode_Is200` | Fact | RFC-9111-§4.3.4: merged response StatusCode is 200 (not 304) |
| 10 | `MergeNotModified_Body_IsCachedBody` | Fact | RFC-9111-§4.3.4: merged response body is the cached body |
| 11 | `MergeNotModified_NewHeaderOverridesCached` | Fact | RFC-9111-§4.3.4: 304 ETag header overrides cached ETag in merged response |
| 12 | `MergeNotModified_PreservesVersion` | Fact | RFC-9111-§4.3.4: merged response preserves cached response version |

#### `01_CacheControlParserTests.cs` - `CacheControlParserTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `NullInput_ReturnsNull` | Fact | RFC-9111-§5.2: null input returns null |
| 2 | `EmptyInput_ReturnsNull` | Fact | RFC-9111-§5.2: empty string returns null |
| 3 | `WhitespaceInput_ReturnsNull` | Fact | RFC-9111-§5.2: whitespace-only input returns null |
| 4 | `NoCache_ParsedCorrectly` | Fact | RFC-9111-§5.2: no-cache directive parsed correctly |
| 5 | `NoStore_Parsed` | Fact | RFC-9111-§5.2: no-store directive parsed |
| 6 | `MaxAge_ParsedCorrectly` | Fact | RFC-9111-§5.2: max-age=3600 parsed as TimeSpan |
| 7 | `SMaxAge_ParsedCorrectly` | Fact | RFC-9111-§5.2: s-maxage=600 parsed correctly |
| 8 | `MaxStale_ParsedCorrectly` | Fact | RFC-9111-§5.2: max-stale=300 parsed correctly |
| 9 | `MinFresh_ParsedCorrectly` | Fact | RFC-9111-§5.2: min-fresh=60 parsed correctly |
| 10 | `MustRevalidate_Parsed` | Fact | RFC-9111-§5.2: must-revalidate flag parsed |
| 11 | `Public_Parsed` | Fact | RFC-9111-§5.2: public directive parsed |
| 12 | `Private_Parsed` | Fact | RFC-9111-§5.2: private directive parsed |
| 13 | `Immutable_Parsed` | Fact | RFC-9111-§5.2: immutable flag parsed |
| 14 | `OnlyIfCached_Parsed` | Fact | RFC-9111-§5.2: only-if-cached parsed |
| 15 | `MultipleDirectives_ParsedTogether` | Fact | RFC-9111-§5.2: multiple directives parsed in one header |
| 16 | `NoCache_WithFieldList_ParsedCorrectly` | Fact | RFC-9111-§5.2: no-cache with field list parsed |
| 17 | `UnknownDirective_SilentlyIgnored` | Fact | RFC-9111-§5.2: unknown directive silently ignored |
| 18 | `CaseInsensitive_MaxAge` | Fact | RFC-9111-§5.2: case-insensitive parsing MAX-AGE=3600 |
| 19 | `NoTransform_Parsed` | Fact | RFC-9111-§5.2: no-transform directive parsed |
| 20 | `MaxStale_NoValue_AnyStaleAccepted` | Fact | RFC-9111-§5.2: max-stale without value accepted (any staleness) |

#### `02_CacheFreshnessTests.cs` - `CacheFreshnessTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `MaxAge_FreshnessLifetime_60s` | Fact | RFC-9111-§4.2: max-age=60 → freshness lifetime = 60s |
| 2 | `SMaxAge_OverridesMaxAge_SharedCache` | Fact | RFC-9111-§4.2: s-maxage=120 overrides max-age=60 for shared cache |
| 3 | `SMaxAge_IgnoredForPrivateCache` | Fact | RFC-9111-§4.2: s-maxage ignored for private cache |
| 4 | `ExpiresHeader_UsedWhenNoMaxAge` | Fact | RFC-9111-§5.3: Expires header used when no max-age |
| 5 | `HeuristicFreshness_TenPercentOfAge` | Fact | RFC-9111-§4.2.2: heuristic freshness = 10% of age from Last-Modified |
| 6 | `HeuristicFreshness_CappedAtOneDay` | Fact | RFC-9111-§4.2.2: heuristic freshness capped at 1 day |
| 7 | `NoFreshnessInfo_LifetimeZero` | Fact | RFC-9111-§4.2: no freshness info → lifetime = zero |
| 8 | `CurrentAge_UsesAgeHeader` | Fact | RFC-9111-§4.2.3: current age uses Age header value |
| 9 | `CurrentAge_WithoutAgeHeader_UsesResponseDelay` | Fact | RFC-9111-§4.2.3: current age without Age header uses response delay |
| 10 | `IsFresh_True_WhenFreshnessExceedsAge` | Fact | RFC-9111-§4.2: fresh entry: freshness_lifetime > current_age → IsFresh=true |
| 11 | `IsFresh_False_WhenAgeExceedsFreshness` | Fact | RFC-9111-§4.2: stale entry: freshness_lifetime ≤ current_age → IsFresh=false |
| 12 | `Evaluate_NullEntry_Miss` | Fact | RFC-9111-§4: Evaluate with null entry → Miss |
| 13 | `Evaluate_FreshEntry_Fresh` | Fact | RFC-9111-§4: Evaluate with fresh entry → Fresh |

### RFC9112 (286 tests)

#### `16_RoundTripChunkedTests.cs` - `Http11RoundTripChunkedTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_AssembleChunkedBody_When_ChunkedRoundTrip` | Fact | RFC9112-6: HTTP/1.1 GET → 200 chunked response round-trip |
| 2 | `Should_ConcatenateChunks_When_FiveChunksRoundTrip` | Fact | RFC9112-6: HTTP/1.1 GET → response with 5 chunks round-trip |
| 3 | `Should_AccessTrailer_When_ChunkedWithTrailerRoundTrip` | Fact | RFC9112-6: HTTP/1.1 chunked response with trailer round-trip |
| 4 | `Should_DecodeOneByte_When_SingleByteChunkRoundTrip` | Fact | RFC9112-6: Single 1-byte chunk decoded correctly |
| 5 | `Should_DecodeBody_When_UppercaseHexChunkSizeRoundTrip` | Fact | RFC9112-6: Uppercase hex chunk size decoded correctly |
| 6 | `Should_ConcatenateAllChunks_When_TwentyTinyChunksRoundTrip` | Fact | RFC9112-6: 20 single-character chunks concatenated correctly |
| 7 | `Should_Preserve32KbChunk_When_LargeChunkRoundTrip` | Fact | RFC9112-6: 32KB single chunk decoded correctly |
| 8 | `Should_DecodeBody_When_ChunkHasExtensionRoundTrip` | Fact | RFC9112-6: Chunk with extension token — body decoded correctly |
| 9 | `Should_DecodeBoth_When_ChunkedThenContentLengthPipelined` | Fact | RFC9112-6: Pipelined chunked then Content-Length response decoded |
| 10 | `Should_AccessBothTrailers_When_TwoTrailerHeadersRoundTrip` | Fact | RFC9112-6: Chunked body with two trailer headers round-trip |

#### `17_RoundTripStatusCodeTests.cs` - `Http11RoundTripStatusCodeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Return301WithLocation_When_GetRoundTrip` | Fact | RFC7231-6.3: HTTP/1.1 GET → 301 with Location round-trip |
| 2 | `Should_Return404_When_ResourceMissingRoundTrip` | Fact | RFC7231-6.5: HTTP/1.1 GET → 404 Not Found round-trip |
| 3 | `Should_Return500_When_ServerErrorRoundTrip` | Fact | RFC7231-6.6: HTTP/1.1 GET → 500 Internal Server Error round-trip |
| 4 | `Should_Return503WithRetryAfter_When_ServiceUnavailableRoundTrip` | Fact | RFC7231-6.6: HTTP/1.1 503 Service Unavailable with Retry-After |

#### `18_RoundTripPipeliningTests.cs` - `Http11RoundTripPipeliningTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecodeBothResponses_When_TwoPipelinedRequestsRoundTrip` | Fact | RFC7230-5.1: Two pipelined requests and responses round-trip |
| 2 | `Should_DecodeAllThree_When_ThreePipelinedResponsesRoundTrip` | Fact | RFC7230-5.1: Three pipelined responses decoded in order |
| 3 | `Should_DecodeAllFive_When_FivePipelinedResponsesRoundTrip` | Fact | RFC7230-5.1: Five pipelined responses all decoded correctly |
| 4 | `Should_PreserveStatusCodes_When_MixedStatusPipelined` | Fact | RFC7230-5.1: Pipelined 200 → 404 → 200 — status codes preserved |
| 5 | `Should_SkipContinue_And_Return200_When_100ContinueRoundTrip` | Fact | RFC7230-5.1: HTTP/1.1 1xx status skipped, final status returned |
| 6 | `Should_Skip102_When_FollowedBy200RoundTrip` | Fact | RFC7230-5.1: 102 Processing skipped — only 200 OK returned |
| 7 | `Should_DecodeSecondResponse_When_KeepAliveRoundTrip` | Fact | RFC7230-6.1: Two sequential keep-alive responses decoded correctly |
| 8 | `Should_DecodeAllThree_When_SequentialKeepAliveRoundTrip` | Fact | RFC7230-6.1: Three sequential keep-alive responses decoded correctly |
| 9 | `Should_ReturnConnectionClose_When_ResponseHasConnectionCloseHeader` | Fact | RFC7230-6.1: Connection: close header preserved in decoded response |
| 10 | `Should_DecodeAll_When_MixedEncodingsPipelined` | Fact | RFC7230-6.1: Pipelined chunked → Content-Length → 204 all decoded |

#### `13_DecoderFragmentationTests.cs` - `Http11DecoderFragmentationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `StatusLine_SplitAtByte1_Reassembled` | Fact | RFC9112: Status-line split byte 1 reassembled |
| 2 | `StatusLine_SplitInsideVersion_Reassembled` | Fact | RFC9112: Status-line split inside HTTP/1.1 version |
| 3 | `Header_SplitAtColon_Reassembled` | Fact | RFC9112: Header name:value split at colon |
| 4 | `Split_AtHeaderBodyBoundary_Reassembled` | Fact | RFC9112: Split at CRLFCRLF header-body boundary |
| 5 | `ChunkSize_SplitAcrossReads_Reassembled` | Fact | RFC9112: Chunk-size line split across two reads |
| 6 | `Response_OneByteAtATime_AssemblesCorrectly` | Fact | RFC9112: Response delivered 1 byte at a time assembles correctly |

#### `14_DecoderLegacyTests.cs` - `Http11DecoderLegacyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ParseImfFixdateToDateTimeOffset_When_DateHeaderPresent` | Fact | RFC7231-7.1.1: IMF-fixdate Date header parsed |
| 2 | `Should_ParseRfc850ObsoleteFormat_When_DateHeaderPresent` | Fact | RFC7231-7.1.1: RFC 850 Date format accepted |
| 3 | `Should_ParseAnsiCAsctimeFormat_When_DateHeaderPresent` | Fact | RFC7231-7.1.1: ANSI C asctime Date format accepted |
| 4 | `Should_HandleNonGmtTimezone_When_DateHeaderPresent` | Fact | RFC7231-7.1.1: Non-GMT timezone in Date rejected |
| 5 | `Should_HandleInvalidDateGracefully_When_DateHeaderMalformed` | Fact | RFC7231-7.1.1: Invalid Date header value rejected |
| 6 | `TwoPipelinedResponses_InSameBuffer_BothDecoded` | Fact | RFC7230: Two pipelined responses decoded |
| 7 | `TwoPipelinedResponses_SecondPartial_RemainderBuffered` | Fact | RFC7230: Partial second response held in remainder |
| 8 | `ThreePipelinedResponses_InSameBuffer_DecodedInOrder` | Fact | RFC9113: Three pipelined responses decoded in order |
| 9 | `Test_ContentRange_Accessible` | Fact | RFC7233-4.1: Content-Range: bytes 0-499/1000 accessible |
| 10 | `Test_PartialContent_Decoded` | Fact | RFC7233-4.1: 206 Partial Content with Content-Range decoded |
| 11 | `Test_Multipart_ByteRanges_Decoded` | Fact | RFC7233-4.1: 206 multipart/byteranges body decoded |
| 12 | `Test_ContentRange_UnknownTotal_Accepted` | Fact | RFC7233-4.1: Content-Range: bytes 0-499/* unknown total |

#### `15_RoundTripMethodTests.cs` - `Http11RoundTripMethodTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Return200_When_GetRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 GET → 200 OK round-trip |
| 2 | `Should_Return201Created_When_PostJsonRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 POST JSON → 201 Created round-trip |
| 3 | `Should_Return204NoContent_When_PutRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 PUT → 204 No Content round-trip |
| 4 | `Should_Return200_When_DeleteRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 DELETE → 200 OK round-trip |
| 5 | `Should_Return200_When_PatchRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 PATCH → 200 OK round-trip |
| 6 | `Should_ReturnContentLengthHeader_When_HeadRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 HEAD → Content-Length but no body |
| 7 | `Should_ReturnAllowHeader_When_OptionsRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 OPTIONS → 200 with Allow header |
| 8 | `Should_EncodeQueryString_When_RequestHasQueryStringRoundTrip` | Fact | RFC9112-3.1: HTTP/1.1 request URL with query string preserved |

#### `Http11DecoderChunkExtensionTests.cs` - `Http11DecoderChunkExtensionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecodeBody_When_NoExtensionPresent` | Fact | 9112-chunkext-001: No extension — body decoded correctly |
| 2 | `Should_DecodeBody_When_HexChunkSizeAndNoExtension` | Fact | 9112-chunkext-002: No extension — hex chunk size decoded correctly |
| 3 | `Should_ConcatenateChunks_When_MultipleChunksAndNoExtension` | Fact | 9112-chunkext-003: No extension — multiple chunks concatenated |
| 4 | `Should_DecodeEmptyBody_When_OnlyTerminatorChunk` | Fact | 9112-chunkext-004: No extension — empty body with terminator only |
| 5 | `Should_PreserveTrailerFields_When_NoExtensionAndTrailerPresent` | Fact | 9112-chunkext-005: No extension — trailer fields preserved |
| 6 | `Should_AcceptExtension_When_NameOnlyNoValue` | Fact | 9112-chunkext-006: Name-only extension — accepted and body intact |
| 7 | `Should_AcceptExtension_When_NameEqualsTokenValue` | Fact | 9112-chunkext-007: Extension with token value — accepted |
| 8 | `Should_AcceptExtension_When_NameEqualsQuotedValue` | Fact | 9112-chunkext-008: Extension with quoted string value — accepted |
| 9 | `Should_AcceptExtension_When_EmptyQuotedValue` | Fact | 9112-chunkext-009: Extension with empty quoted value — accepted |
| 10 | `Should_AcceptExtension_When_QuotedValueWithEscape` | Fact | 9112-chunkext-010: Quoted value with backslash escape — accepted |
| 11 | `Should_AcceptExtension_When_BWSBeforeName` | Fact | 9112-chunkext-011: BWS (space) before extension name — accepted |
| 12 | `Should_AcceptExtension_When_BWSAroundEqualsSign` | Fact | 9112-chunkext-012: BWS (spaces) around equals sign — accepted |
| 13 | `Should_AcceptExtension_When_BWSIsTabCharacter` | Fact | 9112-chunkext-013: BWS using tab character — accepted |
| 14 | `Should_AcceptExtension_When_NameStartsWithExclamation` | Fact | 9112-chunkext-014: Extension name starting with '!' token char — accepted |
| 15 | `Should_AcceptExtension_When_NameContainsHashChar` | Fact | 9112-chunkext-015: Extension with '#' token char in name — accepted |
| 16 | `Should_AcceptExtensions_When_TwoNameOnlyExtensions` | Fact | 9112-chunkext-016: Two name-only extensions — accepted |
| 17 | `Should_AcceptExtensions_When_TwoNameValueExtensions` | Fact | 9112-chunkext-017: Two name=value extensions — accepted |
| 18 | `Should_AcceptExtensions_When_ExtensionsOnMultipleChunks` | Fact | 9112-chunkext-018: Extensions on multiple chunks — all accepted |
| 19 | `Should_AcceptExtensions_When_MixedNameOnlyAndNameValue` | Fact | 9112-chunkext-019: Mixed name-only and name=value extensions — accepted |
| 20 | `Should_AcceptExtension_When_ExtensionOnTerminatorChunk` | Fact | 9112-chunkext-020: Extension on terminator chunk (size=0) — accepted |
| 21 | `Should_ThrowInvalidChunkExtension_When_BWSWithNoNameFollowing` | Fact | 9112-chunkext-021: BWS with no name following — rejected |
| 22 | `Should_ThrowInvalidChunkExtension_When_DoubleSemicolon` | Fact | 9112-chunkext-022: Double semicolon (empty name) — rejected |
| 23 | `Should_ThrowInvalidChunkExtension_When_UnclosedQuote` | Fact | 9112-chunkext-023: Unclosed quoted string value — rejected |
| 24 | `Should_ThrowInvalidChunkExtension_When_EmptyTokenValue` | Fact | 9112-chunkext-024: Empty token value after equals — rejected |
| 25 | `Should_ThrowInvalidChunkExtension_When_NameStartsWithEquals` | Fact | 9112-chunkext-025: Extension name starts with '=' — rejected |
| 26 | `Should_ThrowInvalidChunkExtension_When_SpaceEmbeddedInName` | Fact | 9112-chunkext-026: Space embedded in extension name — rejected |
| 27 | `Should_ThrowInvalidChunkExtension_When_AtSignInTokenValue` | Fact | 9112-chunkext-027: '@' character in token value — rejected |
| 28 | `Should_ThrowInvalidChunkExtension_When_TrailingInvalidCharAfterValue` | Fact | 9112-chunkext-028: Trailing invalid char after token value — rejected |
| 29 | `Should_ThrowInvalidChunkExtension_When_AtSignInName` | Fact | 9112-chunkext-029: '@' character in extension name — rejected |
| 30 | `Should_ThrowInvalidChunkExtension_When_SlashInName` | Fact | 9112-chunkext-030: '/' character in extension name — rejected |
| 31 | `Should_ThrowInvalidChunkExtension_When_LeftBracketInName` | Fact | 9112-chunkext-031: '[' character in extension name — rejected |
| 32 | `Should_ThrowInvalidChunkExtension_When_TrailingTextWithNoEqualsOrSemicolon` | Fact | 9112-chunkext-032: Text after name without equals or semicolon — rejected |
| 33 | `Should_ThrowInvalidChunkExtension_When_NulByteInName` | Fact | 9112-chunkext-033: NUL byte in extension name — rejected |
| 34 | `Should_ThrowInvalidChunkExtension_When_SecondExtensionHasInvalidName` | Fact | 9112-chunkext-034: Second extension invalid in multi-extension — rejected |
| 35 | `Should_ThrowInvalidChunkExtension_When_SecondChunkHasInvalidExtension` | Fact | 9112-chunkext-035: Semicolon on second chunk is invalid — rejected |

#### `Http11NegativePathTests.cs` - `Http11NegativePathTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `RFC9112_4_StatusLine_MustRejectHttp20Version` | Fact | RFC9112-4-SL-001: HTTP/2.0 version in status-line rejected |
| 2 | `RFC9112_4_StatusLine_MustRejectNonHttpProtocol` | Fact | RFC9112-4-SL-002: Non-HTTP protocol prefix in status-line rejected |
| 3 | `RFC9112_4_StatusLine_MustRejectDoubleSpaceBeforeStatusCode` | Fact | RFC9112-4-SL-003: Double space between HTTP-version and status code rejected |
| 4 | `RFC9112_4_StatusLine_MustRejectTwoDigitStatusCode` | Fact | RFC9112-4-SL-004: Two-digit status code rejected |
| 5 | `RFC9112_4_StatusLine_MustRejectNonDigitInStatusCode` | Fact | RFC9112-4-SL-005: Non-digit character in status code rejected |
| 6 | `RFC9112_4_StatusLine_BareLineFeedNeverDecodes` | Fact | RFC9112-4-SL-006: Bare LF (no CR) line endings are not recognized as CRLF |
| 7 | `RFC9112_4_StatusLine_OverlongReasonPhraseCaughtByHeaderLimit` | Fact | RFC9112-4-SL-007: Overlong reason phrase hits header section size limit |
| 8 | `RFC9112_5_Header_ChunkedTrailerWithoutColonRejected` | Fact | RFC9112-5-HDR-001: Chunked trailer without colon rejected |
| 9 | `RFC9112_5_Header_ChunkedTrailerEmptyFieldNameRejected` | Fact | RFC9112-5-HDR-002: Chunked trailer with empty field name rejected |
| 10 | `RFC9112_6_TransferEncoding_NonChunkedWithoutContentLength_YieldsEmptyBody` | Fact |  |
| 11 | `RFC9112_6_Body_BytesAfterContentLengthTreatedAsPipelinedResponse` | Fact | RFC9112-6-TE-002: Bytes after Content-Length boundary are not consumed by current response |
| 12 | `RFC9110_15_Response204_AlwaysHasEmptyBody` | Fact |  |
| 13 | `RFC9110_15_Response304_AlwaysHasEmptyBody` | Fact |  |
| 14 | `RFC9112_9_MultipleContentLength_SameValue_Accepted` | Fact | RFC9112-9-SMUG-001: Multiple Content-Length with same value is accepted per RFC 9112 §6.3 |
| 15 | `RFC9112_9_MultipleContentLength_DifferentValues_Rejected` | Fact | RFC9112-9-SMUG-002: Multiple Content-Length with differing values is a parse error |
| 16 | `RFC9112_9_TransferEncodingAndContentLength_Rejected` | Fact | RFC9112-9-SMUG-003: Transfer-Encoding + Content-Length combination rejected |
| 17 | `RFC9112_7_Chunked_ZeroSizeLineWithNonNumericCharactersRejected` | Fact | RFC9112-7-CHK-001: Chunk size of zero with extra data in line rejected as parse error |
| 18 | `RFC9112_7_Chunked_UpperCaseHexChunkSizeAccepted` | Fact | RFC9112-7-CHK-002: Chunked body with all-caps hex chunk size accepted |

#### `Http11SecurityTests.cs` - `Http11SecurityTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Accept100Headers_When_AtDefaultLimit` | Fact | SEC-001a: 100 headers accepted at default limit |
| 2 | `Should_Reject101Headers_When_AboveDefaultLimit` | Fact | SEC-001b: 101 headers rejected above default limit |
| 3 | `Should_RejectAtCustomLimit_When_HeaderCountExceeded` | Fact | SEC-001c: Custom header count limit respected |
| 4 | `Should_AcceptHeaderBlock_When_Below8KBLimit` | Fact | SEC-002a: Header block below 8KB limit accepted |
| 5 | `Should_RejectHeaderBlock_When_Above8KBLimit` | Fact | SEC-002b: Header block above 8KB limit rejected |
| 6 | `Should_RejectSingleHeader_When_ValueExceedsLimit` | Fact | SEC-002c: Single header value exceeding limit rejected |
| 7 | `Should_AcceptBody_When_AtConfigurableLimit` | Fact | SEC-003a: Body at configurable limit accepted |
| 8 | `Should_RejectBody_When_ExceedingLimit` | Fact | SEC-003b: Body exceeding limit rejected |
| 9 | `Should_RejectBody_When_ZeroBodyLimit` | Fact | SEC-003c: Zero body limit rejects any body |
| 10 | `Should_RejectResponse_When_BothTransferEncodingAndContentLengthPresent` | Fact | SEC-005a: Transfer-Encoding + Content-Length rejected |
| 11 | `Should_RejectHeader_When_CrlfInjectedInValue` | Fact | SEC-005b: CRLF injection in header value rejected |
| 12 | `Should_RejectHeader_When_NulByteInValue` | Fact | SEC-005c: NUL byte in decoded header value rejected |
| 13 | `Should_DecodeCleanly_When_ResetAfterPartialHeaders` | Fact | SEC-006a: Reset() after partial headers restores clean state |
| 14 | `Should_DecodeCleanly_When_ResetAfterPartialBody` | Fact | SEC-006b: Reset() after partial body restores clean state |

#### `19_RoundTripNoBodyTests.cs` - `Http11RoundTripNoBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Return304NoBody_When_NotModifiedWithETagRoundTrip` | Fact | RFC7230-3.3: 304 Not Modified with ETag — no body, ETag header preserved |
| 2 | `Should_Return204EmptyBody_When_DeleteReturnsNoContent` | Fact | RFC7230-3.3: 204 No Content after DELETE — empty body |
| 3 | `Should_DecodeBodyOf200_When_304PrecededIt` | Fact | RFC7230-3.3: Pipelined 304 → 200 — body only in 200 decoded |
| 4 | `Should_ReturnEmptyBody_When_204HasContentTypeHeader` | Fact | RFC7230-3.3: 204 with Content-Type header — empty body returned |
| 5 | `Should_DecodeAll_When_PipelineContainsNoBodyResponses` | Fact | RFC7230-3.3: Pipelined 204 → 200 → 204 — no-body responses handled |
| 6 | `Should_ReturnEmptyBody_When_HeadResponseHasContentLength` | Fact | RFC9110-8.3.4: TryDecodeHead — Content-Length present but body not consumed |
| 7 | `Should_Return404EmptyBody_When_HeadResponseIs404` | Fact | RFC9110-8.3.4: TryDecodeHead 404 — empty body returned |
| 8 | `Should_DecodeBothHeads_When_TwoHeadResponsesPipelined` | Fact | RFC9110-8.3.4: Two pipelined HEAD responses via TryDecodeHead |
| 9 | `Should_DecodeGetAfterHead_When_SameDecoderUsedForBoth` | Fact | RFC9110-8.3.4: HEAD 200 then GET 200 on same decoder instance |

#### `20_RoundTripBodyTests.cs` - `Http11RoundTripBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_PreserveBinaryBody_When_PostBinaryRoundTrip` | Fact | RFC7230-3.3: HTTP/1.1 POST binary → 200 binary response round-trip |
| 2 | `Should_Preserve1MbBody_When_LargeBodyRoundTrip` | Fact | RFC7230-3.3: HTTP/1.1 1 MB body round-trip |
| 3 | `Should_PreserveNullBytes_When_BinaryBodyRoundTrip` | Fact | RFC7230-3.3: HTTP/1.1 binary body with null bytes round-trip |
| 4 | `Should_ReturnEmptyBody_When_ContentLengthZeroRoundTrip` | Fact | RFC7230-3.3: Content-Length 0 — empty body decoded |
| 5 | `Should_DecodeUtf8Body_When_ContentLengthMatchesBytes` | Fact | RFC7230-3.3: Content-Length matches UTF-8 byte count exactly |
| 6 | `Should_Preserve64KbBody_When_ContentLengthRoundTrip` | Fact | RFC7230-3.3: 64KB body round-trip with Content-Length |
| 7 | `Should_DecodeAll_When_ThreePipelinedContentLengthRoundTrip` | Fact | RFC7230-3.3: Three pipelined Content-Length responses decoded in order |
| 8 | `Should_DecodeOneByte_When_ContentLengthOneRoundTrip` | Fact | RFC7230-3.3: Content-Length 1 — single byte body decoded |
| 9 | `Should_DecodeAfterReset_When_ContentLengthRoundTrip` | Fact | RFC7230-3.3: Reset decoder — second Content-Length response decoded after reset |
| 10 | `Should_DecodeAllSizes_When_KeepAliveVaryingBodySizes` | Fact | RFC7230-3.3: Keep-alive — varying body sizes all decoded correctly |
| 11 | `Should_PreserveContentType_When_JsonCharsetRoundTrip` | Fact | RFC7230-3.3: Content-Type: application/json; charset=utf-8 preserved |
| 12 | `Should_PreserveUtf8Bytes_When_Utf8BodyRoundTrip` | Fact | RFC7230-3.3: UTF-8 body preserved byte-for-byte round-trip |
| 13 | `Should_PreserveETagAndCacheControl_When_ETagResponseRoundTrip` | Fact | RFC7230-3.3: ETag with quotes and Cache-Control preserved exactly |
| 14 | `Should_PreserveAllHeaders_When_ResponseHasTenCustomHeaders` | Fact | RFC7230-3.3: Response with 10 custom headers — all preserved |

#### `21_RoundTripFragmentationTests.cs` - `Http11RoundTripFragmentationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_AssembleResponse_When_SplitAfterStatusLine` | Fact | RFC9112-4: TCP fragment split after status line CRLF — response assembled |
| 2 | `Should_AssembleResponse_When_SplitAtHeaderBodyBoundary` | Fact | RFC9112-4: TCP fragment split at header-body boundary — response assembled |
| 3 | `Should_AssembleBody_When_SplitMidBody` | Fact | RFC9112-4: TCP fragment split mid-body — body assembled correctly |
| 4 | `Should_AssembleResponse_When_SingleByteTcpDelivery` | Fact | RFC9112-4: Single-byte TCP delivery assembles complete response |
| 5 | `Should_AssembleChunkedBody_When_SplitBetweenChunks` | Fact | RFC9112-6: TCP fragment split between two chunks — body assembled correctly |

#### `04_EncoderConnectionTests.cs` - `Http11EncoderConnectionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Test_Default_Keep_Alive` | Fact | RFC7230-6.1: Connection keep-alive default in HTTP/1.1 |
| 2 | `Test_Connection_Close` | Fact | RFC7230-6.1: Connection close encoded when set |
| 3 | `Test_Multiple_Connection_Tokens` | Fact | RFC7230-6.1: Multiple Connection tokens encoded |
| 4 | `Test_Connection_Specific_Headers_Stripped` | Fact | RFC9112-6.1: Connection-specific headers stripped |
| 5 | `Get_DefaultConnectionHeader_IsKeepAlive` | Fact |  |
| 6 | `Get_ExplicitConnectionClose_IsPreserved` | Fact |  |

#### `05_EncoderBodyTests.cs` - `Http11EncoderBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Test_No_Content_Length_For_GET` | Fact | RFC7230-3.3: No Content-Length for bodyless GET |
| 2 | `Test_Content_Length_For_POST` | Fact | RFC7230-3.3: Content-Length set for POST body |
| 3 | `Test_Methods_With_Body_Get_Content_Length` | Theory | RFC9112-6: {method} with body gets Content-Length [{method}] |
| 4 | `Test_Methods_Without_Body_Omit_Content_Length` | Theory | RFC9112-6: {method} without body omits Content-Length [{method}] |
| 5 | `Test_Empty_Line_Separator` | Fact | RFC9112-6: Empty line separates headers from body |
| 6 | `Test_Binary_Body_Preserved` | Fact | RFC9112-6: Binary body with null bytes preserved |
| 7 | `Test_Chunked_Transfer_Encoding` | Fact | RFC7230-4.1: Chunked Transfer-Encoding for streamed body |
| 8 | `Test_Chunked_Body_Terminator` | Fact | RFC7230-4.1: Chunked body terminated with final 0-chunk |
| 9 | `Test_No_Content_Length_When_Chunked` | Fact | RFC7230-3.3.2: Content-Length absent when Transfer-Encoding is chunked |
| 10 | `Get_EndsWithBlankLine` | Fact |  |
| 11 | `Post_WithJsonBody_SetsContentTypeAndLength` | Fact |  |
| 12 | `Post_WithJsonBody_BodyAppearsAfterBlankLine` | Fact |  |
| 13 | `Post_BufferTooSmallForBody_Throws` | Fact |  |
| 14 | `Encode_BufferTooSmallForHeaders_Throws` | Fact |  |

#### `06_EncoderRangeRequestTests.cs` - `Http11EncoderRangeRequestTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Test_Range_Bytes_Encoded` | Fact | RFC7233-2.1: Range: bytes=0-499 encoded |
| 2 | `Test_Range_Suffix_Encoded` | Fact | RFC7233-2.1: Range: bytes=-500 suffix encoded |
| 3 | `Test_Range_OpenEnded_Encoded` | Fact | RFC7233-2.1: Range: bytes=500- open range encoded |
| 4 | `Test_Range_MultiRange_Encoded` | Fact | RFC7233-2.1: Multi-range bytes=0-499,1000-1499 encoded |
| 5 | `Test_Invalid_Range_Rejected` | Fact | RFC7233-2.1: Invalid range bytes=abc-xyz rejected |

#### `01_EncoderRequestLineTests.cs` - `Http11EncoderRequestLineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_ProducesCorrectRequestLine` | Fact |  |
| 2 | `Test_9112_RequestLine_UsesHttp11` | Fact | RFC9112-4: Request-line uses HTTP/1.1 |
| 3 | `Test_Lowercase_Method_Rejected` | Fact | RFC7230-3.1.1: Lowercase method rejected by HTTP/1.1 encoder |
| 4 | `Test_RequestLine_Ends_With_CRLF` | Fact | RFC7230-3.1.1: Every request-line ends with CRLF |
| 5 | `Test_All_Methods` | Theory | RFC9112-4: All HTTP methods produce correct request-line [{method}] |
| 6 | `Test_OPTIONS_Star` | Fact | RFC9112-4: OPTIONS * HTTP/1.1 encoded correctly |
| 7 | `Test_Absolute_URI_For_Proxy` | Fact | RFC9112-4: Absolute-URI preserved for proxy request |
| 8 | `Test_Missing_Path_Normalized` | Fact | RFC9112-4: Missing path normalized to / |
| 9 | `Test_Query_String_Preserved` | Fact | RFC9112-4: Query string preserved verbatim |
| 10 | `Test_Fragment_Stripped` | Fact | RFC9112-4: Fragment stripped from request-target |
| 11 | `Test_Percent_Encoding_Not_Re_Encoded` | Fact | RFC9112-4: Existing percent-encoding not re-encoded |

#### `02_EncoderHostHeaderTests.cs` - `Http11EncoderHostHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Test_Host_Always_Present` | Fact | RFC9112-5.4: Host header mandatory in HTTP/1.1 |
| 2 | `Test_Host_Emitted_Once` | Fact | RFC9112-5.4: Host header emitted exactly once |
| 3 | `Test_Non_Standard_Port` | Fact | RFC9112-5.4: Host with non-standard port includes port |
| 4 | `Test_IPv6_Bracketed` | Fact | RFC9112-5.4: IPv6 host literal bracketed correctly |
| 5 | `Test_Default_Port_Omitted` | Fact | RFC9112-5.4: Default port 80 omitted from Host header |
| 6 | `Get_ContainsHostHeader_Port80_NoPort` | Fact |  |
| 7 | `Get_ContainsHostHeader_Port443_NoPort` | Fact |  |
| 8 | `Get_NonStandardPort_IncludesPortInHost` | Fact |  |

#### `03_EncoderHeaderTests.cs` - `Http11EncoderHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Test_Header_Format` | Fact | RFC7230-3.2: Header field format is Name: SP value CRLF |
| 2 | `Test_No_Spurious_Whitespace` | Fact | RFC7230-3.2: No spurious whitespace added to header values |
| 3 | `Test_Header_Name_Casing_Preserved` | Fact | RFC7230-3.2: Header name casing preserved in output |
| 4 | `Test_NUL_Byte_Rejected` | Fact | RFC9112-3.2: NUL byte in header value throws exception |
| 5 | `Test_Content_Type_With_Charset` | Fact | RFC9112-3.2: Content-Type with charset parameter preserved |
| 6 | `Test_Custom_Headers_Appear` | Fact | RFC9112-3.2: All custom headers appear in output |
| 7 | `Test_Accept_Encoding` | Fact | RFC9112-3.2: Accept-Encoding gzip,deflate encoded |
| 8 | `Test_Authorization_Preserved` | Fact | RFC9112-3.2: Authorization header preserved verbatim |
| 9 | `BearerToken_SetsAuthorizationHeader` | Fact |  |

#### `10_DecoderBodyTests.cs` - `Http11DecoderBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `IncompleteHeader_NeedMoreData_ReturnsFalse_OnSecondChunk` | Fact |  |
| 2 | `IncompleteBody_NeedMoreData_ReturnsTrue_AfterBodyArrives` | Fact |  |
| 3 | `ContentLength_Body_DecodedToExactByteCount` | Fact | RFC7230-3.3: Content-Length body decoded to exact byte count |
| 4 | `Zero_ContentLength_EmptyBody` | Fact | RFC7230-3.3: Zero Content-Length produces empty body |
| 5 | `TransferEncoding_And_ContentLength_Conflict_Rejected` | Fact | RFC7230-3.3: Transfer-Encoding + Content-Length conflict rejected |
| 6 | `Multiple_ContentLength_DifferentValues_Rejected` | Fact | RFC7230-3.3: Multiple Content-Length values rejected |
| 7 | `Negative_ContentLength_HandledGracefully` | Fact | RFC7230-3.3: Negative Content-Length is parse error |
| 8 | `NoBodyFraming_EmptyBody` | Fact | RFC7230-3.3: Response without body framing has empty body |
| 9 | `LargeBody_10MB_DecodedCorrectly` | Fact | RFC9112-6: 10 MB body decoded with correct Content-Length |
| 10 | `BinaryBody_WithNullBytes_Intact` | Fact | RFC9112-6: Binary body with null bytes intact |
| 11 | `Decode_ConflictingHeaders_BothTeAndCl_Rejected` | Fact |  |
| 12 | `Decode_MultipleContentLength_DifferentValues_Throws` | Fact |  |
| 13 | `Decode_NegativeContentLength_HandledGracefully` | Fact |  |
| 14 | `Decode_NoBodyIndicator_EmptyBody` | Fact |  |

#### `11_DecoderChunkedTests.cs` - `Http11DecoderChunkedTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ChunkedBody_Decodes_Correctly` | Fact |  |
| 2 | `ChunkedBody_WithExtensions_Ignored` | Fact |  |
| 3 | `ChunkedBody_Incomplete_NeedMoreData` | Fact |  |
| 4 | `Decode_InvalidChunkSize_ReturnsError` | Fact |  |
| 5 | `Decode_ChunkSizeTooLarge_ReturnsError` | Fact |  |
| 6 | `Decode_ChunkedWithTrailer_TrailerHeadersPresent` | Fact |  |
| 7 | `SingleChunk_Decoded` | Fact | RFC7230-4.1: Single chunk body decoded |
| 8 | `MultipleChunks_Concatenated` | Fact | RFC7230-4.1: Multiple chunks concatenated |
| 9 | `ChunkExtension_SilentlyIgnored` | Fact | RFC7230-4.1: Chunk extension silently ignored |
| 10 | `TrailerFields_AfterFinalChunk_Accessible` | Fact | RFC7230-4.1: Trailer fields after final chunk |
| 11 | `NonHex_ChunkSize_IsError` | Fact | RFC7230-4.1: Non-hex chunk size is parse error |
| 12 | `MissingFinalChunk_NeedMoreData` | Fact | RFC7230-4.1: Missing final chunk is NeedMoreData |
| 13 | `ZeroChunk_TerminatesChunkedBody` | Fact | RFC7230-4.1: 0\\r\\n\\r\\n terminates chunked body |
| 14 | `ChunkSize_Overflow_IsError` | Fact | RFC7230-4.1: Chunk size overflow is parse error |
| 15 | `OneByte_Chunk_Decoded` | Fact | RFC9112-6: 1-byte chunk decoded |
| 16 | `Uppercase_HexChunkSize_Accepted` | Fact | RFC9112-6: Uppercase hex chunk size accepted |
| 17 | `EmptyChunk_BeforeTerminator_Accepted` | Fact | RFC9112-6: Empty chunk (0 data bytes) before terminator accepted |

#### `12_DecoderNoBodyTests.cs` - `Http11DecoderNoBodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Response_204_NoContent_EmptyBody` | Fact | RFC9110: 204 No Content has empty body |
| 2 | `Response_304_NotModified_EmptyBody` | Fact | RFC9110: 304 Not Modified has empty body |
| 3 | `NoBodyStatuses_AlwaysEmptyBody` | Theory | RFC9110: Status {code} always has empty body |
| 4 | `HEAD_Response_HasContentLength_ButEmptyBody` | Fact | RFC9110: HEAD response has Content-Length header but empty body |
| 5 | `Connection_Close_Signals_ConnectionClose` | Fact | RFC7230-6.1: Connection: close signals connection close |
| 6 | `Connection_KeepAlive_Signals_Reuse` | Fact | RFC7230-6.1: Connection: keep-alive signals reuse |
| 7 | `Http11_DefaultConnection_IsKeepAlive` | Fact | RFC7230-6.1: HTTP/1.1 default connection is keep-alive |
| 8 | `Http10_DefaultConnection_IsClose` | Fact | RFC7230-6.1: HTTP/1.0 connection defaults to close |
| 9 | `Multiple_ConnectionTokens_AllRecognized` | Fact | RFC7230-6.1: Multiple Connection tokens all recognized |
| 10 | `TwoResponses_InSameBuffer_BothDecoded` | Fact |  |

#### `07_EncoderLegacyTests.cs` - `Http11EncoderLegacyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_ProducesCorrectRequestLine` | Fact |  |
| 2 | `Get_WithQueryParams_EncodesQueryString` | Fact |  |
| 3 | `Post_WithJsonBody_SetsContentTypeAndLength` | Fact |  |
| 4 | `Post_WithJsonBody_BodyAppearsAfterBlankLine` | Fact |  |
| 5 | `Get_EndsWithBlankLine` | Fact |  |
| 6 | `BearerToken_SetsAuthorizationHeader` | Fact |  |
| 7 | `Get_ContainsHostHeader_Port80_NoPort` | Fact |  |
| 8 | `Get_ContainsHostHeader_Port443_NoPort` | Fact |  |
| 9 | `Get_NonStandardPort_IncludesPortInHost` | Fact |  |
| 10 | `Get_DefaultConnectionHeader_IsKeepAlive` | Fact |  |
| 11 | `Get_ExplicitConnectionClose_IsPreserved` | Fact |  |
| 12 | `Post_BufferTooSmallForBody_Throws` | Fact |  |
| 13 | `Encode_BufferTooSmallForHeaders_Throws` | Fact |  |
| 14 | `Post_WithJsonBody_BodyAppearsAfterBlankLine_Alt` | Fact |  |

#### `08_DecoderStatusLineTests.cs` - `Http11DecoderStatusLineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `SimpleOk_WithContentLength_Decodes` | Fact |  |
| 2 | `KnownStatusCodes_ParseCorrectly` | Theory |  |
| 3 | `All_2xx_StatusCodes_ParseCorrectly` | Theory | RFC7231-6.1: 2xx status code {code} parsed correctly |
| 4 | `All_3xx_StatusCodes_ParseCorrectly` | Theory | RFC7231-6.1: 3xx status code {code} parsed correctly |
| 5 | `All_4xx_StatusCodes_ParseCorrectly` | Theory | RFC7231-6.1: 4xx status code {code} parsed correctly |
| 6 | `All_5xx_StatusCodes_ParseCorrectly` | Theory | RFC7231-6.1: 5xx status code {code} parsed correctly |
| 7 | `Informational_1xx_HasNoBody` | Fact | RFC7231-6.1: 1xx Informational response has no body |
| 8 | `Each_1xx_Code_ParsedWithNoBody` | Theory | RFC9110: 1xx code {code} parsed with no body |
| 9 | `Continue_100_Before_200_DecodedCorrectly` | Fact | RFC7231-6.1: 100 Continue before 200 OK decoded correctly |
| 10 | `Multiple_1xx_Then_200_AllProcessed` | Fact | RFC9110: Multiple 1xx interim responses before 200 |
| 11 | `Custom_Status_599_Parsed` | Fact | RFC7231-6.1: Custom status code 599 parsed |
| 12 | `Status_GreaterThan_599_IsError` | Fact | RFC7231-6.1: Status code >599 is a parse error |
| 13 | `Empty_ReasonPhrase_IsValid` | Fact | RFC7231-6.1: Empty reason phrase is valid |
| 14 | `Response204_NoContent_NoBody` | Fact |  |
| 15 | `Informational_100Continue_IsSkipped` | Fact |  |
| 16 | `Response304_NoBody_ParsedCorrectly` | Fact |  |

#### `09_DecoderHeaderTests.cs` - `Http11DecoderHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ResponseWithCustomHeaders_PreservesHeaders` | Fact |  |
| 2 | `Decode_HeaderWithoutColon_ThrowsHttpDecoderException` | Fact |  |
| 3 | `Standard_HeaderField_Parsed` | Fact | RFC7230-3.2: Standard header field Name: value parsed |
| 4 | `OWS_TrimmedFromHeaderValue` | Fact | RFC7230-3.2: OWS trimmed from header value |
| 5 | `Empty_HeaderValue_Accepted` | Fact | RFC7230-3.2: Empty header value accepted |
| 6 | `Multiple_SameName_Headers_Preserved` | Fact | RFC7230-3.2: Multiple same-name headers both accessible |
| 7 | `ObsFold_RejectedInHttp11` | Fact | RFC7230-3.2: Obs-fold rejected in HTTP/1.1 |
| 8 | `Header_WithoutColon_IsError` | Fact | RFC7230-3.2: Header without colon is parse error |
| 9 | `HeaderName_Lookup_CaseInsensitive` | Fact | RFC7230-3.2: Header name lookup case-insensitive |
| 10 | `Tab_InHeaderValue_Accepted` | Fact | RFC9112-3.2: Tab character in header value accepted |
| 11 | `QuotedString_HeaderValue_Parsed` | Fact | RFC9112-3.2: Quoted-string header value parsed |
| 12 | `ContentType_WithParameters_Parsed` | Fact | RFC9112-3.2: Content-Type: text/html; charset=utf-8 accessible |
| 13 | `Decode_Header_OWS_Trimmed` | Fact |  |
| 14 | `Decode_Header_EmptyValue_Accepted` | Fact |  |
| 15 | `Decode_Header_CaseInsensitiveName` | Fact |  |
| 16 | `Decode_Header_MultipleValuesForSameName_Preserved` | Fact |  |
| 17 | `Decode_Header_ObsFold_Http11_IsError` | Fact |  |

### RFC9113 (659 tests)

#### `21_RequestEncoderFrameTests.cs` - `Http2RequestEncoderFrameTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Encode_GetRequest_ProducesHeadersFrameWithEndStream` | Fact | 9113-8.1-001: GET request produces HEADERS frame with END_STREAM and END_HEADERS |
| 2 | `Encode_PostRequest_ProducesHeadersThenData` | Fact | 9113-8.1-002: POST request produces HEADERS frame (no END_STREAM) followed by DATA |
| 3 | `Encode_GetRequest_HeaderBlockContainsPseudoHeaders` | Fact | 9113-8.3.1-001: Encoded header block contains required HTTP/2 pseudo-headers |
| 4 | `Encode_RequestWithQuery_PathIncludesQuery` | Fact | 9113-8.3.1-002: Path includes query string in :path pseudo-header |
| 5 | `Encode_ConnectionHeaders_AreStripped` | Fact | 9113-8.2.2-001: Connection-specific headers are stripped from encoded output |
| 6 | `Encode_LargeHeaderBlock_UsesContinuationFrames` | Fact | 9113-6.10-002: Header block larger than max frame size uses CONTINUATION frames |
| 7 | `Encode_PostRequest_AllFramesHaveSameStreamId` | Fact | 9113-5.1.1-001: All frames for a request share the same stream ID |

#### `Http2CrossComponentValidationTests.cs` - `Http2CrossComponentValidationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `MalformedHpackBytes_ThrowsCompressionError` | Fact | CC-001: Malformed HPACK byte sequence → CompressionError connection error |
| 2 | `OutOfRangeDynamicIndex_ThrowsCompressionError` | Fact | CC-002: Out-of-range dynamic index in HPACK block → CompressionError connection error |
| 3 | `HpackCompressionError_IsConnectionLevel_NotStreamLevel` | Fact | CC-003: HPACK CompressionError has IsConnectionError = true (not stream error) |
| 4 | `HpackEmptyHeaderName_ThrowsCompressionError` | Fact | CC-004: HPACK empty header name → CompressionError connection error |
| 5 | `HpackFailureOnAnyStream_IsConnectionError` | Fact | CC-005: HPACK failure on stream 5 is still a connection error (affects all streams) |
| 6 | `ConnectionWindow_TrackedIndependentlyFromHpack` | Fact | CC-006: Connection window tracked independently from HPACK state |
| 7 | `StreamWindows_AreIndependent_AcrossStreams` | Fact | CC-007: Stream windows are independent across multiple streams |
| 8 | `FlowControlErrorOnStream1_DoesNotCorruptStream3` | Fact | CC-008: Flow control error on stream 1 does not corrupt stream 3 window |
| 9 | `WindowUpdateOnStream1_DoesNotAffectStream3` | Fact | CC-009: WINDOW_UPDATE on stream 1 does not affect stream 3 send window |
| 10 | `ConnectionWindowUpdate_IncreasesOnlyConnectionSendWindow` | Fact | CC-010: Connection WINDOW_UPDATE increases only the connection send window |
| 11 | `RstStream_DecrementsActiveStreamCount` | Fact | CC-011: RST_STREAM decrements active stream count |
| 12 | `RstStream_MarksStreamLifecycleAsClosed` | Fact | CC-012: RST_STREAM marks stream lifecycle as Closed |
| 13 | `AfterRstStream_DataOnResetStream_IsStreamClosedError` | Fact | CC-013: After RST_STREAM, subsequent DATA on that stream is stream STREAM_CLOSED error |
| 14 | `RstStream_Result_CarriesErrorCode` | Fact | CC-014: RST_STREAM result carries the error code from the frame payload |
| 15 | `AfterGoAway_IsGoingAway_IsTrue` | Fact | CC-015: After receiving GOAWAY, IsGoingAway = true |
| 16 | `NewHeadersAfterGoAway_IsRejected` | Fact | CC-016: New HEADERS after GOAWAY is rejected with PROTOCOL_ERROR |
| 17 | `GoAway_SetsLastStreamId` | Fact | CC-017: GOAWAY sets GoAwayLastStreamId correctly |
| 18 | `GoAway_CleansUpStreamsAboveLastStreamId` | Fact | CC-018: GOAWAY cleans up streams with ID > lastStreamId |
| 19 | `InvalidHpackIndex_CannotInjectHeaders_ThrowsCompressionError` | Fact | CC-019: Invalid HPACK index cannot inject arbitrary headers (CompressionError) |
| 20 | `HpackEncodedUppercaseHeaderName_IsRejectedByValidation` | Fact | CC-020: HPACK-encoded uppercase header name is caught by response validation |

#### `Http2EncoderPseudoHeaderValidationTests.cs` - `Http2RequestEncoderPseudoHeaderValidationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Validate_AllFourPseudoHeaders_Passes` | Fact | 7540-8.1.2.1-c001: All four required pseudo-headers passes validation |
| 2 | `Validate_PseudoThenRegular_Passes` | Fact | 7540-8.1.2.1-c002: Pseudo-headers followed by regular headers passes |
| 3 | `Validate_PseudoThenMultipleRegular_Passes` | Fact | 7540-8.1.2.1-c003: Multiple regular headers after pseudo-headers passes |
| 4 | `Validate_OnlyPseudoHeaders_Passes` | Fact | 7540-8.1.2.1-c004: No regular headers (only pseudo-headers) passes |
| 5 | `Validate_MissingMethod_ThrowsHttp2Exception` | Fact | 7540-8.1.2.1-c005: Missing :method throws Http2Exception |
| 6 | `Validate_MissingPath_ThrowsHttp2Exception` | Fact | 7540-8.1.2.1-c006: Missing :path throws Http2Exception |
| 7 | `Validate_MissingScheme_ThrowsHttp2Exception` | Fact | 7540-8.1.2.1-c007: Missing :scheme throws Http2Exception |
| 8 | `Validate_MissingAuthority_ThrowsHttp2Exception` | Fact | 7540-8.1.2.1-c008: Missing :authority throws Http2Exception |
| 9 | `Validate_EmptyHeaders_ThrowsWithAllMissing` | Fact | 7540-8.1.2.1-c009: Empty header list throws with all missing pseudo-headers |
| 10 | `Validate_MultipleMissing_AllListedInMessage` | Fact | 7540-8.1.2.1-c010: Multiple missing pseudo-headers listed together in message |
| 11 | `Validate_DuplicateMethod_Throws` | Fact | 7540-8.1.2.1-c011: Duplicate :method throws Http2Exception |
| 12 | `Validate_DuplicatePath_Throws` | Fact | 7540-8.1.2.1-c012: Duplicate :path throws Http2Exception |
| 13 | `Validate_DuplicateScheme_Throws` | Fact | 7540-8.1.2.1-c013: Duplicate :scheme throws Http2Exception |
| 14 | `Validate_DuplicateAuthority_Throws` | Fact | 7540-8.1.2.1-c014: Duplicate :authority throws Http2Exception |
| 15 | `Validate_StatusPseudo_ThrowsForRequest` | Fact | 7540-8.1.2.1-c015: Unknown pseudo-header :status throws Http2Exception |
| 16 | `Validate_CustomPseudo_Throws` | Fact | 7540-8.1.2.1-c016: Unknown pseudo-header :custom throws Http2Exception |
| 17 | `Validate_PseudoAfterRegular_Throws` | Fact | 7540-8.1.2.1-c017: Pseudo-header after regular header throws Http2Exception |
| 18 | `Validate_PseudoAfterRegular_MessageContainsPositions` | Fact | 7540-8.1.2.1-c018: Pseudo-after-regular error message contains indices |
| 19 | `Validate_InterleavedPseudoAndRegular_Throws` | Fact | 7540-8.1.2.1-c019: All pseudo-headers interleaved with regular headers throws |
| 20 | `Validate_PseudoAfterRegular_ErrorCode_IsProtocolError` | Fact | 7540-8.1.2.1-c020: Error code on pseudo-after-regular is ProtocolError |
| 21 | `Encode_StandardMethods_Succeed` | Theory | 7540-8.1.2.1-i001: Encode succeeds for [{method}] requests |
| 22 | `Encode_HttpsRequest_SchemeIsHttps` | Fact | 7540-8.1.2.1-i002: Encode HTTPS request encodes :scheme as 'https' |
| 23 | `Encode_HttpRequest_SchemeIsHttp` | Fact | 7540-8.1.2.1-i003: Encode HTTP request encodes :scheme as 'http' |
| 24 | `Encode_WithQueryString_PathIncludesQuery` | Fact | 7540-8.1.2.1-i004: Encode encodes query string in :path |
| 25 | `Encode_RootPath_EncodesSlash` | Fact | 7540-8.1.2.1-i005: Root path encodes :path as '/' |
| 26 | `Encode_LongPath_EncodesCorrectly` | Fact | 7540-8.1.2.1-i006: Long path encodes correctly in :path |
| 27 | `Encode_StandardHttpsPort_AuthorityExcludesPort` | Fact | 7540-8.1.2.1-i007: Standard port not included in :authority |
| 28 | `Encode_NonStandardPort_AuthorityIncludesPort` | Fact | 7540-8.1.2.1-i008: Non-standard port included in :authority |
| 29 | `Encode_AllFourPseudoHeaders_Present` | Fact | 7540-8.1.2.1-i009: All four pseudo-headers present in encoded output |
| 30 | `Encode_PseudoHeaders_PrecedeRegular` | Fact | 7540-8.1.2.1-i010: Pseudo-headers precede regular headers in output |
| 31 | `Encode_NoDuplicatePseudoHeaders` | Fact | 7540-8.1.2.1-i011: No duplicate pseudo-headers in encoded output |
| 32 | `Encode_NoUnknownPseudoHeaders` | Fact | 7540-8.1.2.1-i012: No unknown pseudo-headers in encoded output |
| 33 | `Encode_WithCustomHeaders_PseudoHeadersUnaffected` | Fact | 7540-8.1.2.1-i013: Custom headers do not displace pseudo-headers |
| 34 | `Encode_ConnectionHeadersStripped_PseudoHeadersPreserved` | Fact | 7540-8.1.2.1-i014: Connection-specific headers stripped but pseudo-headers preserved |
| 35 | `Encode_MultipleRequests_EachHasValidPseudoHeaders` | Fact | 7540-8.1.2.1-i015: Multiple requests each have valid pseudo-headers |
| 36 | `Encode_MethodValue_MatchesRequestMethod` | Fact | 7540-8.1.2.1-i016: :method value matches request method |
| 37 | `Encode_PathValue_IncludesPathAndQuery` | Fact | 7540-8.1.2.1-i017: :path value includes path and query string |
| 38 | `Encode_AuthorityValue_MatchesUriHost` | Fact | 7540-8.1.2.1-i018: :authority value matches URI host |
| 39 | `Encode_PostWithBody_AllPseudoHeadersPresent` | Fact | 7540-8.1.2.1-i019: POST request with body includes all pseudo-headers |
| 40 | `Encode_Post_MethodIsPOST` | Fact | 7540-8.1.2.1-i020: POST :method value is POST |
| 41 | `Encode_Decode_RoundTrip_MethodPreserved` | Fact | 7540-8.1.2.1-i021: Encode-decode round trip preserves :method value |
| 42 | `Encode_Decode_RoundTrip_SchemePreserved` | Fact | 7540-8.1.2.1-i022: Encode-decode round trip preserves :scheme value |
| 43 | `Encode_GetRequest_ExactlyFourPseudoHeaders` | Fact | 7540-8.1.2.1-i023: Exactly four pseudo-headers in encoded GET request |
| 44 | `Encode_NestedPath_FullPathEncoded` | Fact | 7540-8.1.2.1-i024: :path for nested path encodes full path |
| 45 | `Encode_WithHuffman_PseudoHeadersValid` | Fact | 7540-8.1.2.1-i025: Encode with Huffman compression still produces valid pseudo-headers |

#### `20_EncoderStreamSettingsTests.cs` - `Http2EncoderStreamSettingsTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Settings_Parameter_EncodedCorrectly` | Theory | enc5-set-001: SETTINGS parameter {param} encoded correctly |
| 2 | `SettingsAck_HasCorrectTypeAndFlags` | Fact | enc5-set-002: SETTINGS ACK frame has type=0x04 flags=0x01 stream=0 |
| 3 | `StreamId_FirstRequest_IsOne` | Fact | 7540-5.1-001: First request uses stream ID 1 |
| 4 | `StreamId_NeverEven` | Fact | enc5-sid-001: Client never produces even stream IDs |
| 5 | `StreamId_Near2Pow31_ThrowsGracefully` | Fact | enc5-sid-002: Stream ID approaching 2^31 handled gracefully |
| 6 | `FlowControl_InitialWindow_LimitsToDefault` | Fact | 7540-5.2-enc-001: Encoder does not exceed initial 65535-byte window |
| 7 | `FlowControl_WindowUpdate_AllowsMoreData` | Fact | 7540-5.2-enc-002: WINDOW_UPDATE allows more DATA to be sent |
| 8 | `FlowControl_ZeroWindow_BlocksData` | Fact | 7540-5.2-enc-005: Encoder blocks when window is zero |
| 9 | `FlowControl_ConnectionWindow_LimitsTotalData` | Fact | 7540-5.2-enc-006: Connection-level window limits total DATA |
| 10 | `FlowControl_PerStreamWindow_LimitsStreamData` | Fact | 7540-5.2-enc-007: Per-stream window limits DATA on that stream |

#### `17_RoundTripHpackTests.cs` - `Http2RoundTripHpackTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_UseIndexedRepresentation_When_Status200Encoded` | Fact | RT-2-039: Well-known :status 200 uses static table indexed reference |
| 2 | `Should_DecodeResponse_When_FragmentedAtFrameBoundary` | Fact | RT-2-040: Response fragmented at frame boundary requires two TryDecode calls |
| 3 | `Should_DecodeResponse_When_FragmentedMidFrameHeader` | Fact | RT-2-041: Response fragmented mid-frame header accumulates correctly |
| 4 | `Should_DecodeResponse_When_DeliveredOneByteAtATime` | Fact | RT-2-042: Single-byte delivery accumulates a complete frame over many calls |
| 5 | `Should_ReturnEmptyBody_When_HeadersFrameHasEndStream` | Fact | RT-2-043: HEADERS with endStream=true produces response with empty body |
| 6 | `Should_IgnoreUnknownFrameType_When_UnknownTypeReceived` | Fact | RT-2-044: Unknown frame type (0x0F) is silently ignored |
| 7 | `Should_ThrowRefusedStream_When_MaxConcurrentStreamsExceeded` | Fact | RT-2-045: MAX_CONCURRENT_STREAMS enforced, exceeding limit throws |
| 8 | `Should_IncrementStreamIdsAsOddNumbers_When_MultipleRequestsEncoded` | Fact | RT-2-046: Stream IDs increment as odd numbers: 1, 3, 5, 7, 9 |
| 9 | `Should_AcceptNewStreams_When_DecoderResetBetweenConnections` | Fact | RT-2-047: Reset decoder between connections clears all state |
| 10 | `Should_DecodeGoAwayDebugMessage_When_GoAwayHasDebugData` | Fact | RT-2-048: GOAWAY debug message decoded correctly |
| 11 | `Should_HaveIndependentStreamIds_When_TwoEncodersCreated` | Fact | RT-2-049: Two independent encoders maintain separate stream ID sequences |
| 12 | `Should_Decode301_When_RedirectResponseReceived` | Fact | RT-2-050: 301 Moved Permanently response decoded with Location header |
| 13 | `Should_PreserveContentHeaders_When_ResponseHasContentHeaders` | Fact | RT-2-051: Content-Type and Content-Length headers round-trip correctly |
| 14 | `Should_HaveEmptyBody_When_ResponseHasNoDataFrame` | Fact | RT-2-052: HEAD-like response (HEADERS only, endStream=true) produces no body |
| 15 | `Should_ReturnTrue_When_ServerPrefaceHasValidSettingsFrame` | Fact | RT-2-053: ValidateServerPreface accepts valid SETTINGS frame on stream 0 |
| 16 | `Should_StripConnectionHeaders_When_RequestHasForbiddenHeaders` | Fact | RT-2-054: Encoder strips Connection-specific headers (TE, Keep-Alive, etc.) |
| 17 | `Should_PreserveAllEntityHeaders_When_ResponseDecodedWithEntityHeaders` | Fact | RT-2-056: Entity headers (content-language, etc.) preserved in response.Content.Headers |
| 18 | `Should_MaintainHpackState_When_MultipleFramesSentAcrossDecodeCallBatches` | Fact | RT-2-055: HPACK decoder state survives across multiple TryDecode calls on same connection |

#### `18_EncoderBaselineTests.cs` - `Http2EncoderBaselineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `BuildConnectionPreface_StartsWithMagic` | Fact |  |
| 2 | `BuildConnectionPreface_ContainsSettingsFrame` | Fact |  |
| 3 | `EncodeRequest_Get_ProducesHeadersFrame` | Fact |  |
| 4 | `EncodeRequest_Get_HeadersFrame_HasEndStream` | Fact |  |
| 5 | `EncodeRequest_Get_NoBannedHeaders` | Fact |  |
| 6 | `EncodeRequest_Get_ContainsPseudoHeaders` | Fact |  |
| 7 | `EncodeRequest_Get_NonStandardPort_AuthorityIncludesPort` | Fact |  |
| 8 | `EncodeRequest_Post_HasDataFrame` | Fact |  |
| 9 | `EncodeRequest_Post_HeadersFrame_NoEndStream` | Fact |  |
| 10 | `EncodeRequest_Post_DataFrame_HasEndStream` | Fact |  |
| 11 | `EncodeRequest_Post_ContentHeadersPresent` | Fact |  |
| 12 | `EncodeRequest_Post_EmptyBody_ProducesEmptyDataFrame` | Fact |  |
| 13 | `EncodeSettingsAck_ProducesAckFrame` | Fact |  |
| 14 | `EncodeSettings_ProducesSettingsFrame` | Fact |  |
| 15 | `EncodePing_ProducesPingFrame` | Fact |  |
| 16 | `EncodePingAck_ProducesPingAckFrame` | Fact |  |
| 17 | `EncodeWindowUpdate_ProducesWindowUpdateFrame` | Fact |  |
| 18 | `EncodeRstStream_ProducesRstStreamFrame` | Fact |  |
| 19 | `EncodeGoAway_WithDebugMessage_ProducesGoAwayFrame` | Fact |  |
| 20 | `EncodeGoAway_WithoutDebugMessage_ProducesGoAwayFrame` | Fact |  |
| 21 | `ApplyServerSettings_MaxFrameSize_UpdatesEncoder` | Fact |  |
| 22 | `ApplyServerSettings_OtherParameter_IsIgnored` | Fact |  |
| 23 | `EncodeRequest_LargeHeaders_ProducesContinuationFrames` | Fact |  |

#### `19_EncoderRfcTaggedTests.cs` - `Http2EncoderRfcTaggedTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Preface_MagicBytes_MatchSpec` | Fact | 7540-3.5-001: Client preface is PRI * HTTP/2.0 SM |
| 2 | `Preface_SettingsFrame_ImmediatelyFollowsMagic` | Fact | 7540-3.5-003: SETTINGS frame immediately follows client preface |
| 3 | `PseudoHeaders_AllFourEmitted` | Fact | 7540-8.1-001: All four pseudo-headers emitted |
| 4 | `PseudoHeaders_PrecedeRegularHeaders` | Fact | 7540-8.1-002: Pseudo-headers precede regular headers |
| 5 | `PseudoHeaders_NoDuplicates` | Fact | 7540-8.1-003: No duplicate pseudo-headers |
| 6 | `Http2_ConnectionSpecificHeaders_Absent` | Fact | 7540-8.1-004: Connection-specific headers absent in HTTP/2 |
| 7 | `PseudoHeader_Method_CorrectForAllMethods` | Theory | enc5-ph-001: :method pseudo-header correct for [{method}] |
| 8 | `PseudoHeader_Scheme_ReflectsUriScheme` | Fact | enc5-ph-002: :scheme reflects request URI scheme |
| 9 | `HeadersFrame_HasCorrect9ByteHeader_TypeByte` | Fact | 7540-6.2-001: HEADERS frame has correct 9-byte header and payload |
| 10 | `HeadersFrame_EndStream_SetForGet` | Fact | 7540-6.2-002: END_STREAM flag set on HEADERS for GET |
| 11 | `HeadersFrame_EndHeaders_SetOnSingleFrame` | Fact | 7540-6.2-003: END_HEADERS flag set on single HEADERS frame |
| 12 | `LargeHeaders_SplitIntoContinuation` | Fact | 7540-6.9-001: Headers exceeding max frame size split into CONTINUATION |
| 13 | `ContinuationFrame_FinalHasEndHeaders` | Fact | 7540-6.9-002: END_HEADERS on final CONTINUATION frame |
| 14 | `VeryLargeHeaders_MultipleContinuationFrames` | Fact | 7540-6.9-003: Multiple CONTINUATION frames for very large headers |
| 15 | `DataFrame_EndStream_SetOnFinalFrame` | Fact | 7540-6.1-enc-002: END_STREAM set on final DATA frame |
| 16 | `Get_EndStream_OnHeadersNotData` | Fact | 7540-6.1-enc-003: GET END_STREAM on HEADERS frame |
| 17 | `DataFrame_TypeByte_IsZero` | Fact | enc5-data-001: DATA frame has type byte 0x00 |
| 18 | `DataFrame_CarriesCorrectStreamId` | Fact | enc5-data-002: DATA frame carries correct stream ID |
| 19 | `DataFrame_LargeBody_SplitIntoMultipleFrames` | Fact | enc5-data-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames |

#### `Http2MaxConcurrentStreamsTests.cs` - `Http2MaxConcurrentStreamsTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `DefaultMaxConcurrentStreams_IsIntMaxValue` | Fact | MCS-API-001: Default MaxConcurrentStreams is int.MaxValue |
| 2 | `DefaultActiveStreamCount_IsZero` | Fact | MCS-API-002: Default ActiveStreamCount is zero |
| 3 | `GetMaxConcurrentStreams_BeforeSettings_ReturnsIntMaxValue` | Fact | MCS-API-003: GetMaxConcurrentStreams returns int.MaxValue before any settings |
| 4 | `GetActiveStreamCount_BeforeFrames_ReturnsZero` | Fact | MCS-API-004: GetActiveStreamCount returns zero before any frames |
| 5 | `Reset_RestoresMaxConcurrentStreams_ToIntMaxValue` | Fact | MCS-API-005: Reset restores MaxConcurrentStreams to int.MaxValue |
| 6 | `Reset_RestoresActiveStreamCount_ToZero` | Fact | MCS-API-006: Reset restores ActiveStreamCount to zero |
| 7 | `Settings_MaxConcurrentStreams1_SetsLimitTo1` | Fact | MCS-API-007: SETTINGS with MaxConcurrentStreams=1 sets limit to 1 |
| 8 | `Settings_MaxConcurrentStreams0_SetsLimitTo0` | Fact | MCS-API-008: SETTINGS with MaxConcurrentStreams=0 sets limit to 0 |
| 9 | `Settings_MaxConcurrentStreams100_SetsLimitTo100` | Fact | MCS-API-009: SETTINGS with MaxConcurrentStreams=100 sets limit to 100 |
| 10 | `ActiveStreamCount_BeforeAnyStream_IsZero` | Fact | MCS-API-010: ActiveStreamCount is zero before any stream is opened |
| 11 | `ActiveStreamCount_AfterHeadersWithoutEndStream_IsOne` | Fact | MCS-API-011: ActiveStreamCount increments when HEADERS opens stream without EndStream |
| 12 | `ActiveStreamCount_AfterHeadersWithEndStream_IsZero` | Fact | MCS-API-012: ActiveStreamCount is zero after single-frame HEADERS with EndStream |
| 13 | `ActiveStreamCount_AfterDataWithEndStream_Decrements` | Fact | MCS-API-013: ActiveStreamCount decrements after DATA with EndStream |
| 14 | `ActiveStreamCount_MultipleConcurrentStreams_Tracked` | Fact | MCS-API-014: ActiveStreamCount tracks multiple concurrent streams |
| 15 | `ActiveStreamCount_AfterRstStream_Decrements` | Fact | MCS-API-015: ActiveStreamCount decrements after RST_STREAM |
| 16 | `ExceedingLimit_ThrowsHttp2Exception` | Fact | MCS-API-016: Exceeding MaxConcurrentStreams throws Http2Exception |
| 17 | `ExceedingLimit_UsesRefusedStreamErrorCode` | Fact | MCS-API-017: Exceeded limit uses RefusedStream error code |
| 18 | `ExceedingLimit_MessageIncludesStreamId` | Fact | MCS-API-018: Exceeded limit message includes stream ID |
| 19 | `ExceedingLimit_MessageIncludesLimit` | Fact | MCS-API-019: Exceeded limit message references MaxConcurrentStreams limit |
| 20 | `AfterStreamCloses_NewStreamAccepted_WithExactLimit` | Fact | MCS-API-020: After stream closes, new stream is accepted (limit exact) |
| 21 | `SingleStream_UnderDefaultLimit_Succeeds` | Fact | MCS-INT-001: Single stream under default limit succeeds |
| 22 | `MultipleStreams_UnderLimit_AllSucceed` | Fact | MCS-INT-002: Multiple streams under limit all succeed |
| 23 | `StreamAtExactLimit_IsRefused` | Fact | MCS-INT-003: Stream at exact limit is refused |
| 24 | `LimitEnforcement_DoesNotAffectExistingStreams` | Fact | MCS-INT-004: Limit enforcement applies only to new streams (existing unaffected) |
| 25 | `CounterDecrement_OnEndStreamData_AllowsNewStream` | Fact | MCS-INT-005: Counter decrements on EndStream DATA allowing new stream |
| 26 | `SettingsFrame_UpdatesLimit` | Fact | MCS-INT-006: SETTINGS frame updates MaxConcurrentStreams limit |
| 27 | `SecondSettingsFrame_UpdatesLimitAgain` | Fact | MCS-INT-007: Second SETTINGS frame updates limit again |
| 28 | `RstStream_DecrementsActiveCount` | Fact | MCS-INT-008: RST_STREAM decrements active stream counter |
| 29 | `Limit1_AllowsSequentialStreams` | Fact | MCS-INT-009: Limit of 1 allows sequential streams |
| 30 | `Limit0_RefusesAllStreams` | Fact | MCS-INT-010: Limit of 0 refuses all new streams |
| 31 | `MultipleStreamsOverLimit_AllThrowRefusedStream` | Fact | MCS-INT-011: Multiple streams over limit all throw RefusedStream |
| 32 | `HeadersOnlyResponse_EndStreamInHeaders_ZeroActive` | Fact | MCS-INT-012: Headers-only response (EndStream in HEADERS) counts as zero active |
| 33 | `HeadersPlusData_DecrementsCountCorrectly` | Fact | MCS-INT-013: Headers+DATA response decrements count correctly |
| 34 | `ContinuationFrames_DoNotDoubleCountStream` | Fact | MCS-INT-014: Continuation frames do not double-count stream |
| 35 | `SettingsChange_WhileStreamsActive_DoesNotCloseExistingStreams` | Fact | MCS-INT-015: SETTINGS change while streams active doesn't close existing streams |
| 36 | `IncreasingLimit_AllowsMoreStreams` | Fact | MCS-INT-016: Increasing limit allows more streams |
| 37 | `DecreasingLimit_BelowActiveCount_ExistingStreamsCanComplete` | Fact | MCS-INT-017: Decreasing limit below active count allows existing streams to complete |
| 38 | `Reset_AllowsReconnectionWithFreshLimits` | Fact | MCS-INT-018: Reset allows reconnection with fresh limits |
| 39 | `ActiveStreamCount_AccurateAcrossOpenCloseCycles` | Fact | MCS-INT-019: ActiveStreamCount is accurate across multiple open/close cycles |
| 40 | `RstStream_OnUnknownStream_DoesNotDecrementBelowZero` | Fact | MCS-INT-020: RST_STREAM on unknown stream does not decrement counter below zero |
| 41 | `SettingsAck_DoesNotAffectMaxConcurrentStreams` | Fact | MCS-INT-021: SETTINGS ACK frame does not affect MaxConcurrentStreams |
| 42 | `SettingsFrame_WithMultipleParams_AppliesMaxConcurrentStreams` | Fact | MCS-INT-022: SETTINGS frame with multiple parameters applies MaxConcurrentStreams |
| 43 | `ExceedingLimit_MessageReferencesRfc` | Fact | MCS-INT-023: Limit enforcement message references RFC 7540 §6.5.2 |
| 44 | `AllStreams_CloseViaEndStreamHeaders_CountIsZero` | Fact | MCS-INT-024: ActiveStreamCount zero after all streams close via EndStream headers |
| 45 | `MaxConcurrentStreams_LargeValue_AppliedCorrectly` | Fact | MCS-INT-025: MaxConcurrentStreams limit of uint.MaxValue boundary handled |

#### `Http2ResourceExhaustionTests.cs` - `Http2ResourceExhaustionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ThrowHttp2Exception_When_101SettingsFramesReceived` | Fact | RE-010: 101st non-ACK SETTINGS frame triggers EnhanceYourCalm flood protection |
| 2 | `Should_Accept100SettingsFrames_WithoutException` | Fact | RE-011: Exactly 100 non-ACK SETTINGS frames are accepted without error |
| 3 | `Should_NotCountSettingsAck_TowardFloodThreshold` | Fact | RE-012: SETTINGS ACK frames do NOT count toward the flood threshold |
| 4 | `Should_ClearSettingsCount_OnReset` | Fact | RE-013: Reset() clears the SETTINGS flood counter |
| 5 | `Should_ThrowHttp2Exception_When_101RstStreamReceived` | Fact | RE-020: 101st RST_STREAM triggers rapid-reset ProtocolError (CVE-2023-44487) |
| 6 | `Should_Accept100RstStreamFrames_WithoutException` | Fact | RE-021: Exactly 100 RST_STREAM frames are accepted without error |
| 7 | `Should_IncludeCveReference_InRapidResetMessage` | Fact | RE-022: Rapid-reset exception message references CVE-2023-44487 |
| 8 | `Should_ClearRstCount_OnReset` | Fact | RE-023: Reset() clears the RST_STREAM flood counter |
| 9 | `Should_ThrowHttp2Exception_When_1000ContinuationFramesReceived` | Fact | RE-030: 1000th CONTINUATION frame triggers ProtocolError flood protection |
| 10 | `Should_Accept999ContinuationFrames_WithoutException` | Fact | RE-031: 999 CONTINUATION frames after HEADERS are accepted without error |
| 11 | `Should_ThrowHttp2Exception_When_1001PingFramesReceived` | Fact | RE-040: 1001st non-ACK PING frame triggers EnhanceYourCalm flood protection |
| 12 | `Should_Accept1000PingFrames_WithoutException` | Fact | RE-041: Exactly 1000 non-ACK PING frames are accepted without error |
| 13 | `Should_NotCountPingAck_TowardFloodThreshold` | Fact | RE-042: PING ACK frames do NOT count toward the flood threshold |
| 14 | `Should_ClearPingCount_OnReset` | Fact | RE-043: Reset() clears the PING flood counter |
| 15 | `Should_TrackPingCountAccurately` | Fact | RE-044: GetPingCount() tracks exactly the number of non-ACK PINGs received |
| 16 | `Should_IncludeContextInPingFloodMessage` | Fact | RE-045: PING flood exception message mentions excessive PING frames |
| 17 | `Should_KeepDynamicTableWithinLimit_WhenAddingManyHeaders` | Fact | RE-050: HPACK dynamic table stays within HEADER_TABLE_SIZE limit |
| 18 | `Should_EvictAllEntries_WhenTableSizeSetToZero` | Fact | RE-051: HPACK table size update to 0 evicts all entries |
| 19 | `Should_PreventTableGrowth_WhenMaxAllowedTableSizeIsZero` | Fact | RE-052: SetMaxAllowedTableSize(0) prevents any dynamic table entries |
| 20 | `Should_ThrowHttp2Exception_When_ClosedStreamIdCapExceeded` | Fact | RE-060: Exceeding 10000 closed stream IDs triggers ProtocolError stream-ID exhaustion |
| 21 | `Should_TrackClosedStreamIdCountAccurately` | Fact | RE-061: GetClosedStreamIdCount() accurately tracks closed streams |
| 22 | `Should_ClearClosedStreamIds_OnReset` | Fact | RE-062: Reset() clears the closed-stream-ID set |
| 23 | `Should_TrackClosedStreamsBeyond10000_WithoutError` | Fact | RE-063: Stream tracking remains correct beyond 10000 closed streams — no artificial cap |
| 24 | `Should_Accept10000ClosedStreams_WithoutException` | Fact | RE-064: Exactly 10000 streams can be closed without exhaustion error |
| 25 | `Should_ThrowHttp2Exception_When_10001EmptyDataFramesReceived` | Fact | RE-070: 10001st zero-length DATA frame triggers ProtocolError exhaustion protection |
| 26 | `Should_Accept10000EmptyDataFrames_WithoutException` | Fact | RE-071: Exactly 10000 zero-length DATA frames are accepted without error |
| 27 | `Should_InitializeAllCountersToZero_OnNewDecoder` | Fact | RE-080: A new decoder instance has all flood counters at zero |
| 28 | `Should_ResetAllCountersToZero_AfterReset` | Fact | RE-081: Reset() resets all flood counters to zero |
| 29 | `Should_TrackPingAndSettingsCountersIndependently` | Fact | RE-082: PING flood and SETTINGS flood counters are independent |
| 30 | `Should_DetectEachFloodIndependently_WhenMultipleAttacksInterleaved` | Fact | RE-083: Multiple attack vectors simultaneously do not interfere with each other |

#### `Http2SecurityTests.cs` - `Http2SecurityTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ThrowHttp2Exception_When_1000ContinuationFramesReceived` | Fact | SEC-h2-003: Excessive CONTINUATION frames rejected |
| 2 | `Should_ThrowHttp2Exception_When_101RstStreamFramesReceived` | Fact | SEC-h2-004: Rapid RST_STREAM cycling triggers protection (CVE-2023-44487) |
| 3 | `Should_ThrowHttp2Exception_When_10001EmptyDataFramesReceived` | Fact | SEC-h2-005: Excessive zero-length DATA frames rejected |
| 4 | `Should_ThrowHttp2Exception_When_EnablePushExceedsOne` | Fact | SEC-h2-006: SETTINGS_ENABLE_PUSH value >1 causes PROTOCOL_ERROR |
| 5 | `Should_ThrowHttp2Exception_When_InitialWindowSizeExceedsMax` | Fact | SEC-h2-007: SETTINGS_INITIAL_WINDOW_SIZE >2^31-1 causes FLOW_CONTROL_ERROR |
| 6 | `Should_NotThrow_When_UnknownSettingsIdReceived` | Fact | SEC-h2-008: Unknown SETTINGS ID silently ignored |

#### `Http2HighConcurrencyTests.cs` - `Http2HighConcurrencyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Handle1000SequentialStreams_WithEndStreamHeaders` | Fact | HC-001: 1000 sequential streams with END_STREAM HEADERS — all close cleanly |
| 2 | `Should_Decode1000Responses_From1000Streams` | Fact | HC-002: 1000 streams with END_STREAM HEADERS produce exactly 1000 decoded responses |
| 3 | `Should_AcceptExactly500ConcurrentStreams_WhenLimitIs500` | Fact | HC-003: MAX_CONCURRENT_STREAMS=500 accepts exactly 500 simultaneous open streams |
| 4 | `Should_RecycleStreamCapacity_AfterBulkDataClose` | Fact | HC-004: Bulk open-close cycle: 100 streams opened and DATA-closed, then 100 fresh streams opened |
| 5 | `Should_TrackAllClosedStreams_WithNoCapOnClosedStreamCount` | Fact | HC-005: 10001 sequential streams all close correctly — unbounded closed-stream tracking |
| 6 | `Should_Decode50IndependentSessionInstances_InParallel_WithoutException` | Fact | HC-006: 50 independent sessions decode same HEADERS frame in parallel — no exceptions |
| 7 | `Should_Handle100SessionInstances_EachDecoding20Streams_InParallel` | Fact | HC-007: 100 independent sessions each decode 20 streams in parallel — all active counts are zero |
| 8 | `Should_MaintainIsolatedStreamState_AcrossParallelSessionInstances` | Fact | HC-008: Independent session instances maintain isolated stream counts under parallel load |
| 9 | `Should_ProduceIdenticalHpackOutput_When50EncoderInstancesRunInParallel` | Fact | HC-009: 50 independent HpackEncoder instances encode the same headers in parallel — identical output |
| 10 | `Should_ProduceConsistentClosedStreamCount_WhenParallelMatchesSequential` | Fact | HC-010: Parallel sessions produce the same closed-stream count as sequential baseline |
| 11 | `Should_AcceptData_WhenTotalBytesDoNotExceedConnectionWindow` | Fact | HC-011: Three sequential DATA frames totalling 45000 bytes stay within 65535-byte connection window |
| 12 | `Should_ThrowFlowControlError_WhenDataExceedsConnectionReceiveWindow` | Fact | HC-012: DATA exceeding the connection receive window triggers FlowControlError |
| 13 | `Should_AcceptFurtherData_AfterConnectionWindowRestored` | Fact | HC-013: SetConnectionReceiveWindow restores capacity so subsequent DATA frames succeed |
| 14 | `Should_EnforcePerStreamWindow_WithoutAffectingOtherStreams` | Fact | HC-014: Per-stream window saturation is independent — other streams remain unaffected |
| 15 | `Should_HandleSequentialOpenSendCloseCycles_WithCorrectFinalState` | Fact | HC-015: Five sequential open-send-close cycles all succeed with correct final state |
| 16 | `Should_ClearActiveStreamCount_AfterResetFollowing1000OpenStreams` | Fact | HC-016: Reset() after 1000 open streams clears active count to zero |
| 17 | `Should_ClearGoAwayState_OnReset` | Fact | HC-017: Reset() after GOAWAY clears IsGoingAway and resets GoAway last stream ID to int.MaxValue |
| 18 | `Should_ZeroAllSecurityCounters_OnReset` | Fact | HC-018: Reset() zeroes security counters so full flood thresholds are available again |
| 19 | `Should_BeIdempotent_WhenResetCalledMultipleTimes` | Fact | HC-019: Multiple sequential Reset() calls are idempotent — state is fully cleared each time |
| 20 | `Should_DecodeNewStreams_AfterReset_WithoutPriorStateInterference` | Fact | HC-020: Reset() then immediate decode of fresh streams succeeds without prior-state interference |

#### `Http2EncoderSensitiveHeaderTests.cs` - `Http2EncoderSensitiveHeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_EncodeAsNeverIndexed_When_AuthorizationHeader` | Fact | 7541-7.1.3-s001: Authorization header encoded as NeverIndexed |
| 2 | `Should_EncodeAsNeverIndexed_When_ProxyAuthorizationHeader` | Fact | 7541-7.1.3-s002: Proxy-Authorization header encoded as NeverIndexed |
| 3 | `Should_EncodeAsNeverIndexed_When_CookieHeader` | Fact | 7541-7.1.3-s003: Cookie header encoded as NeverIndexed |
| 4 | `Should_EncodeAsNeverIndexed_When_SetCookieHeader` | Fact | 7541-7.1.3-s004: Set-Cookie header encoded as NeverIndexed |
| 5 | `Should_EncodeAsNeverIndexed_When_AuthorizationHeaderUppercase` | Fact | 7541-7.1.3-s005: Authorization detection is case-insensitive |
| 6 | `Should_EncodeAsNeverIndexed_When_AuthorizationValueIsEmpty` | Fact | 7541-7.1.3-s006: Authorization with empty value is still NeverIndexed |
| 7 | `Should_EncodeAsNeverIndexed_When_AuthorizationValueIsLong` | Fact | 7541-7.1.3-s007: Authorization with long value is still NeverIndexed |
| 8 | `Should_EncodeAsNeverIndexed_When_CookieHasComplexValue` | Fact | 7541-7.1.3-s008: Cookie with complex multi-part value is still NeverIndexed |
| 9 | `Should_NotBeNeverIndexed_When_XApiKeyHeader` | Fact | 7541-7.1.3-s009: x-api-key header is NOT NeverIndexed |
| 10 | `Should_NotBeNeverIndexed_When_UserAgentHeader` | Fact | 7541-7.1.3-s010: User-Agent header is NOT NeverIndexed |
| 11 | `Should_NotBeNeverIndexed_When_CustomHeader` | Fact | 7541-7.1.3-s011: X-Request-Id header is NOT NeverIndexed |
| 12 | `Should_NotBeNeverIndexed_When_PseudoHeaders` | Fact | 7541-7.1.3-s012: Pseudo-headers (:method, :path, etc.) are NOT NeverIndexed |
| 13 | `Should_NotBeNeverIndexed_When_AcceptHeader` | Fact | 7541-7.1.3-s013: Accept header is NOT NeverIndexed |
| 14 | `Should_NotReduceHpackSize_When_AuthorizationEncodedTwice` | Fact | 7541-7.1.3-s014: Authorization encoded twice produces same-size HPACK output (no caching) |
| 15 | `Should_ReduceHpackSize_When_NonSensitiveHeaderEncodedTwice` | Fact | 7541-7.1.3-s015: Non-sensitive header encoded twice is smaller the second time (caching works) |
| 16 | `Should_NeverUseIndexedReference_When_AuthorizationEncodedRepeatedly` | Fact | 7541-7.1.3-s016: Authorization never appears as indexed reference across repeated encodings |
| 17 | `Should_NeverUseIndexedReference_When_CookieEncodedRepeatedly` | Fact | 7541-7.1.3-s017: Cookie never appears as indexed reference across repeated encodings |
| 18 | `Should_PreserveValue_When_AuthorizationRoundTrip` | Fact | 7541-7.1.3-s018: Authorization value preserved through encode/decode round-trip |
| 19 | `Should_PreserveValue_When_ProxyAuthorizationRoundTrip` | Fact | 7541-7.1.3-s019: Proxy-Authorization value preserved through encode/decode round-trip |
| 20 | `Should_PreserveValue_When_CookieRoundTrip` | Fact | 7541-7.1.3-s020: Cookie value preserved through encode/decode round-trip |
| 21 | `Should_PreserveValue_When_SetCookieRoundTrip` | Fact | 7541-7.1.3-s021: Set-Cookie value preserved through encode/decode round-trip |
| 22 | `Should_EncodeBothCorrectly_When_MixedSensitiveAndNonSensitiveHeaders` | Fact | 7541-7.1.3-s022: Mixed request: sensitive and non-sensitive headers both encoded correctly |
| 23 | `Should_EncodeAllAsNeverIndexed_When_MultipleSensitiveHeaders` | Fact | 7541-7.1.3-s023: Multiple sensitive headers in one request are all NeverIndexed |
| 24 | `Should_EncodeAsNeverIndexed_When_HpackHeaderNeverIndexTrue` | Fact | 7541-7.1.3-s024: HpackHeader with NeverIndex=true is encoded as NeverIndexed |
| 25 | `Should_UseIncrementalIndexing_When_HpackHeaderNeverIndexFalse` | Fact | 7541-7.1.3-s025: HpackHeader with NeverIndex=false for non-sensitive name uses IncrementalIndexing |
| 26 | `Should_AutoUpgradeToNeverIndexed_When_SensitiveNameRegardlessOfFlag` | Fact | 7541-7.1.3-s026: Sensitive header name auto-upgraded to NeverIndexed even if NeverIndex=false |
| 27 | `Should_NotAddToDynamicTable_When_NeverIndexedHeaderEncoded` | Fact | 7541-7.1.3-s027: NeverIndexed header not added to dynamic table (two encodings same size) |
| 28 | `Should_ProduceNeverIndexedFrame_When_Http2GetRequestWithAuthorization` | Fact | 7541-7.1.3-s028: Full HTTP/2 GET frame with Authorization: decoded NeverIndex=true |
| 29 | `Should_PreserveSensitiveHeader_When_PostRequestWithBodyAndAuthorization` | Fact | 7541-7.1.3-s029: Full HTTP/2 POST frame with Authorization and body: NeverIndexed preserved |
| 30 | `Should_HaveNoNeverIndexedHeaders_When_NoSensitiveHeaders` | Fact | 7541-7.1.3-s030: Request without sensitive headers has no NeverIndexed entries |
| 31 | `Should_PreserveNeverIndexed_When_HuffmanEncodingEnabled` | Fact | 7541-7.1.3-s031: Huffman encoding preserves NeverIndexed flag for Authorization |
| 32 | `Should_EncodeAllFourSensitiveHeaderTypes_When_AllPresent` | Fact | 7541-7.1.3-s032: All four sensitive header types NeverIndexed in single request |
| 33 | `Should_HaveNeverIndexedEncoding_When_AuthorizationEncodedRaw` | Fact | 7541-7.1.3-s033: Authorization raw HPACK bytes use NeverIndexed encoding (walker verified) |
| 34 | `Should_HaveNeverIndexedEncoding_When_CookieEncodedRaw` | Fact | 7541-7.1.3-s034: Cookie raw HPACK bytes use NeverIndexed encoding (walker verified) |
| 35 | `Should_HaveIncrementalIndexingEncoding_When_NonSensitiveHeaderEncodedRaw` | Fact | 7541-7.1.3-s035: Non-sensitive header raw HPACK bytes use IncrementalIndexing (walker verified) |

#### `Http2FrameTests.cs` - `Http2FrameTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `SettingsFrame_Serialize_CorrectFormat` | Fact |  |
| 2 | `SettingsAck_Serialize_EmptyPayload` | Fact |  |
| 3 | `PingFrame_Serialize_8BytePayload` | Fact |  |
| 4 | `WindowUpdateFrame_Serialize_CorrectIncrement` | Fact |  |
| 5 | `DataFrame_Serialize_WithEndStream` | Fact |  |
| 6 | `GoAwayFrame_Serialize_WithDebugData` | Fact |  |

#### `Http2FuzzHarnessTests.cs` - `Http2FuzzHarnessTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_HandleRandomHeadersDataSequences_WithoutCrashing` | Fact | FZ-001: Random valid HEADERS + DATA sequences never crash the decoder |
| 2 | `Should_HandleRandomRstStreamFrames_WithoutCrashing` | Fact | FZ-002: Random RST_STREAM frames for unknown streams never produce unhandled exceptions |
| 3 | `Should_HandleRandomWindowUpdateFrames_WithoutCrashing` | Fact | FZ-003: Random WINDOW_UPDATE frames on arbitrary streams never produce unhandled exceptions |
| 4 | `Should_HandleInterleavedFrameTypes_WithoutCrashing` | Fact | FZ-004: Interleaved PING and SETTINGS frames in random order never produce unhandled exceptions |
| 5 | `Should_IgnoreUnknownFrameTypes_PerRfc9113` | Fact | FZ-005: Unknown frame types (0x0A..0xFF) are tolerated per RFC 9113 §5.5 |
| 6 | `Should_RejectOversizedFrame_WithFrameSizeError` | Fact | FZ-006: Frame with payload length exceeding MaxFrameSize throws Http2Exception |
| 7 | `Should_BufferTruncatedFrame_WithoutCrashing` | Fact | FZ-007: Truncated frame (buffer smaller than declared length) is buffered without crashing |
| 8 | `Should_RejectPingWithWrongPayloadLength_WithFrameSizeError` | Fact | FZ-008: PING with wrong payload length throws Http2Exception(FrameSizeError) |
| 9 | `Should_RejectSettingsWithNonMultipleOf6Payload_WithFrameSizeError` | Fact | FZ-009: SETTINGS with payload length not a multiple of 6 throws Http2Exception(FrameSizeError) |
| 10 | `Should_HandleCorruptedFrameHeaders_WithoutUnhandledExceptions` | Fact | FZ-010: Random corrupted frame headers never produce unhandled exceptions |
| 11 | `Should_HandleRandomHpackBytes_WithoutCrashing` | Fact | FZ-011: Fully random bytes fed as HPACK header block never crash the decoder |
| 12 | `Should_HandleValidHpackPrefixWithGarbage_WithoutCrashing` | Fact | FZ-012: Valid HPACK prefix followed by garbage bytes never crashes the decoder |
| 13 | `Should_HandleHpackOversizedStringLength_WithoutCrashing` | Fact | FZ-013: HPACK literal with oversized declared string length never crashes the decoder |
| 14 | `Should_HandleOutOfRangeHpackIndex_WithoutCrashing` | Fact | FZ-014: HPACK index beyond static + dynamic table never crashes the decoder |
| 15 | `Should_HandleInvalidHuffmanBitstream_WithoutCrashing` | Fact | FZ-015: Huffman-flagged header with invalid bitstream never crashes the decoder |
| 16 | `Should_RejectConnectionWindowOverflow_WithFlowControlError` | Fact | FZ-016: WINDOW_UPDATE that overflows connection send window throws Http2Exception(FlowControlError) |
| 17 | `Should_RejectStreamWindowOverflow_WithFlowControlError` | Fact | FZ-017: WINDOW_UPDATE that overflows stream send window throws Http2Exception(FlowControlError) |
| 18 | `Should_RejectZeroIncrementWindowUpdate_WithProtocolError` | Fact | FZ-018: Zero-increment WINDOW_UPDATE throws Http2Exception(ProtocolError) |
| 19 | `Should_AcceptSettingsInitialWindowSizeAtMax_WithoutException` | Fact | FZ-019: SETTINGS INITIAL_WINDOW_SIZE at maximum valid value (2^31-1) is accepted |
| 20 | `Should_RejectSettingsInitialWindowSizeAboveMax_WithFlowControlError` | Fact | FZ-020: SETTINGS INITIAL_WINDOW_SIZE exceeding 2^31-1 throws Http2Exception(FlowControlError) |
| 21 | `Should_HandleRepeatedTableSizeOscillation_WithoutCrashing` | Fact | FZ-021: Repeated HpackDecoder table size oscillation between 0 and 256 never crashes |
| 22 | `Should_EvictAllEntries_WhenTableSizeReducedToZero_AfterFilling` | Fact | FZ-022: Filling dynamic table then resizing to 0 evicts all entries without crashing |
| 23 | `Should_HandleRapidHeaderTableSizeChanges_WithoutCrashing` | Fact | FZ-023: Rapid SETTINGS HEADER_TABLE_SIZE changes with random values never crash Http2ProtocolSession |
| 24 | `Should_HandleHeaderTableSizeZero_FollowedByNormalHeaders` | Fact | FZ-024: SETTINGS HEADER_TABLE_SIZE=0 followed by normal HPACK headers is handled correctly |
| 25 | `Should_SurviveExtendedRandomFrameSequence_WithoutUnhandledExceptions` | Fact | FZ-025: Extended random frame sequence (1000 iterations) never produces unhandled exceptions |

#### `16_RoundTripMethodTests.cs` - `Http2RoundTripMethodTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_EncodeDeleteWithNoBody_When_DeleteRequestEncoded` | Fact | RT-2-020: DELETE request encodes correctly with no body |
| 2 | `Should_EncodePutWithBody_When_PutRequestEncoded` | Fact | RT-2-021: PUT request encodes with body (HEADERS + DATA) |
| 3 | `Should_EncodePatchWithBody_When_PatchRequestEncoded` | Fact | RT-2-022: PATCH request encodes with body (HEADERS + DATA) |
| 4 | `Should_UseThreeContinuationFrames_When_MaxFrameSizeVerySmall` | Fact | RT-2-023: Three CONTINUATION frames required for tiny max frame size |
| 5 | `Should_DecodeResponse_When_HeadersSplitAcrossMultipleContinuations` | Fact | RT-2-024: Response with multiple CONTINUATION frames decoded correctly |
| 6 | `Should_NeverIndexSetCookie_When_SetCookieHeaderEncoded` | Fact | RT-2-025: set-cookie response header NeverIndexed does not appear in dynamic table |
| 7 | `Should_NeverIndexProxyAuthorization_When_ProxyAuthHeaderEncoded` | Fact | RT-2-026: proxy-authorization request header NeverIndexed |
| 8 | `Should_HandleMixedHeaders_When_SensitiveAndNonSensitiveCombined` | Fact | RT-2-027: Mixed sensitive and non-sensitive headers round-trip correctly |
| 9 | `Should_IndexCustomHeader_When_NonSensitiveHeaderEncoded` | Fact | RT-2-028: Non-sensitive custom header is indexed and produces shorter second encoding |
| 10 | `Should_ReturnFiveResponses_When_FiveConcurrentStreams` | Fact | RT-2-029: Five concurrent streams all complete successfully |
| 11 | `Should_DecodeCorrectStatusCodes_When_ConcurrentStreamsHaveDifferentStatuses` | Fact | RT-2-030: Mixed status codes across concurrent streams decoded correctly |
| 12 | `Should_DecodeResponses_When_DataFramesInterleaved` | Fact | RT-2-031: Interleaved DATA frames for two concurrent streams decoded correctly |
| 13 | `Should_DecodeLargeBody_When_ResponseBodyIs32KB` | Fact | RT-2-032: Large body response (32 KB) decoded correctly |
| 14 | `Should_AssembleBody_When_MultipleDataFramesForSingleStream` | Fact | RT-2-033: Multiple DATA frames for a single stream assembled into one body |
| 15 | `Should_AcceptMaxIncrement_When_WindowUpdateWithMaxValue` | Fact | RT-2-034: WINDOW_UPDATE that brings stream window to exactly 2^31-1 is accepted |
| 16 | `Should_TrackWindowUpdates_When_ConnectionAndStreamUpdatesReceived` | Fact | RT-2-035: Connection and stream WINDOW_UPDATE increments tracked separately |
| 17 | `Should_DecodeResponse_When_HuffmanEncodingEnabled` | Fact | RT-2-036: HTTP/2 round-trip with Huffman encoding enabled |
| 18 | `Should_ProduceSmallerBlock_When_HuffmanEnabledForKnownHeaders` | Fact | RT-2-037: HPACK encoder with Huffman produces smaller encoding for well-known headers |
| 19 | `Should_DecodeIndexedHeader_When_HpackDynamicTableContainsEntry` | Fact | RT-2-038: HPACK sync: decoder correctly interprets indexed reference after dynamic table update |

#### `05_FlowControlTests.cs` - `Http2FlowControlTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_HaveDefaultConnectionReceiveWindow_When_DecoderCreated` | Fact | FC-001: Initial connection receive window is 65535 |
| 2 | `Should_DecrementConnectionReceiveWindow_When_DataFrameReceived` | Fact | FC-002: Connection receive window decrements after DATA received |
| 3 | `Should_ThrowFlowControlError_When_DataExceedsConnectionWindow` | Fact | FC-003: FlowControlError when DATA exceeds connection receive window |
| 4 | `Should_ThrowFlowControlError_When_DataExceedsStreamWindowButNotConnectionWindow` | Fact | FC-004: FlowControlError when DATA exceeds stream window but not connection window |
| 5 | `Should_AcceptData_When_DataEqualsConnectionWindow` | Fact | FC-005: No FlowControlError when DATA is exactly at connection window limit |
| 6 | `Should_HaveDefaultStreamReceiveWindow_When_StreamOpened` | Fact | FC-006: Initial stream receive window is 65535 |
| 7 | `Should_DecrementConnectionWindowByDataSize_When_LargeDataFrameReceived` | Fact | FC-007: Connection receive window decrements by DATA size |
| 8 | `Should_ThrowFlowControlError_When_DataExceedsStreamWindow` | Fact | FC-008: FlowControlError when DATA exceeds stream receive window |
| 9 | `Should_UpdateStreamWindow_When_SetStreamReceiveWindowCalled` | Fact | FC-009: SetStreamReceiveWindow updates stream window correctly |
| 10 | `Should_Return65535_When_StreamReceiveWindowRequestedForUnknownStream` | Fact | FC-010: Unknown stream receive window defaults to 65535 |
| 11 | `Should_HaveDefaultConnectionSendWindow_When_DecoderCreated` | Fact | FC-011: Initial connection send window is 65535 |
| 12 | `Should_IncreaseConnectionSendWindow_When_WindowUpdateReceivedOnStream0` | Fact | FC-012: WINDOW_UPDATE on stream 0 increases connection send window |
| 13 | `Should_AccumulateConnectionSendWindow_When_MultipleWindowUpdatesReceived` | Fact | FC-013: Multiple WINDOW_UPDATEs on stream 0 accumulate |
| 14 | `Should_ThrowFlowControlError_When_ConnectionSendWindowWouldOverflow` | Fact | FC-014: FlowControlError when connection send window overflows 2^31-1 |
| 15 | `Should_AcceptWindowUpdate_When_ConnectionSendWindowReachesMaxExactly` | Fact | FC-015: Connection send window at exactly 2^31-1 is accepted |
| 16 | `Should_ReturnInitialWindowSize_When_StreamSendWindowRequestedBeforeAnyUpdate` | Fact | FC-016: Initial stream send window defaults to SETTINGS_INITIAL_WINDOW_SIZE (65535) |
| 17 | `Should_IncreaseStreamSendWindow_When_WindowUpdateReceivedForStream` | Fact | FC-017: WINDOW_UPDATE on stream N increases that stream's send window |
| 18 | `Should_TrackSendWindowsIndependently_When_MultipleStreamsUpdated` | Fact | FC-018: Multiple stream WINDOW_UPDATEs accumulate independently per stream |
| 19 | `Should_ThrowFlowControlError_When_StreamSendWindowWouldOverflow` | Fact | FC-019: FlowControlError when stream send window overflows 2^31-1 |
| 20 | `Should_AcceptStreamWindowUpdate_When_StreamSendWindowReachesMaxExactly` | Fact | FC-020: Stream send window at exactly 2^31-1 is accepted |
| 21 | `Should_ThrowProtocolError_When_ConnectionWindowUpdateIncrementIsZero` | Fact | FC-021: Zero-increment WINDOW_UPDATE on stream 0 is a PROTOCOL_ERROR |
| 22 | `Should_ThrowProtocolError_When_StreamWindowUpdateIncrementIsZero` | Fact | FC-022: Zero-increment WINDOW_UPDATE on stream N is a PROTOCOL_ERROR |
| 23 | `Should_ThrowFrameSizeError_When_WindowUpdatePayloadIsNot4Bytes` | Fact | FC-023: WINDOW_UPDATE with wrong payload size is a FRAME_SIZE_ERROR |
| 24 | `Should_DecrementBothReceiveWindows_When_DataFrameReceived` | Fact | FC-024: Connection and stream receive windows decrement after DATA received |
| 25 | `Should_DecrementConnectionWindowByExactPayloadSize_When_DataReceived` | Fact | FC-025: Connection receive window decrements by exact DATA payload size |
| 26 | `Should_DecrementStreamWindowByExactPayloadSize_When_DataReceived` | Fact | FC-026: Stream receive window decrements by exact DATA payload size |
| 27 | `Should_NotDecrementReceiveWindows_When_ZeroLengthDataReceived` | Fact | FC-027: Zero-length DATA does not decrement receive windows |
| 28 | `Should_CumulativelyDecrementReceiveWindows_When_MultipleDataFramesReceived` | Fact | FC-028: Multiple DATA frames cumulatively decrement receive windows |
| 29 | `Should_UseNewInitialWindowSize_When_SettingsUpdatesInitialWindowSize` | Fact | FC-029: SETTINGS_INITIAL_WINDOW_SIZE updates default send window for unknown streams |
| 30 | `Should_ApplyDeltaToOpenStreams_When_InitialWindowSizeSettingChanges` | Fact | FC-030: SETTINGS_INITIAL_WINDOW_SIZE applies delta to existing open streams |
| 31 | `Should_IncreaseOpenStreamsWindowByDelta_When_InitialWindowSizeIncreased` | Fact | FC-031: SETTINGS_INITIAL_WINDOW_SIZE increase applies positive delta to open streams |
| 32 | `Should_ThrowFlowControlError_When_InitialWindowSizeExceedsMax` | Fact | FC-032: SETTINGS_INITIAL_WINDOW_SIZE value > 2^31-1 causes FLOW_CONTROL_ERROR |
| 33 | `Should_RestoreConnectionReceiveWindowTo65535_When_ResetCalled` | Fact | FC-033: Reset restores connection receive window to 65535 |
| 34 | `Should_RestoreConnectionSendWindowTo65535_When_ResetCalled` | Fact | FC-034: Reset restores connection send window to 65535 |
| 35 | `Should_ClearStreamSendWindows_When_ResetCalled` | Fact | FC-035: Reset clears stream send windows back to default |
| 36 | `Should_ReportWindowUpdateInResult_When_WindowUpdateFrameReceived` | Fact | FC-036: Received WINDOW_UPDATE is reported in session.WindowUpdates |
| 37 | `Should_TrackConnectionAndStreamWindowUpdatesSeparately_When_MultipleWindowUpdateFramesReceived` | Fact | FC-037: Connection WINDOW_UPDATE tracked separately from stream WINDOW_UPDATEs |
| 38 | `Should_NotDecrementReceiveWindows_When_ZeroLengthDataWithEndStreamReceived` | Fact | FC-038: Zero-length DATA with END_STREAM does not decrement receive windows |

#### `06_HeadersTests.cs` - `Http2DecoderHeadersValidationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_Accept_When_ValidMinimalResponse` | Fact | HV-001: Valid response with only :status is accepted |
| 2 | `Should_Accept_When_StatusFollowedByRegularHeaders` | Fact | HV-002: Valid response with :status then regular headers is accepted |
| 3 | `Should_Throw_When_StatusPseudoHeaderMissing` | Fact | HV-003: Missing :status pseudo-header is PROTOCOL_ERROR |
| 4 | `Should_Throw_When_StatusPseudoHeaderDuplicated` | Fact | HV-004: Duplicate :status pseudo-header is PROTOCOL_ERROR |
| 5 | `Should_Throw_When_MethodPseudoHeaderInResponse` | Fact | HV-005: Request pseudo-header :method in response is PROTOCOL_ERROR |
| 6 | `Should_Throw_When_PathPseudoHeaderInResponse` | Fact | HV-006: Request pseudo-header :path in response is PROTOCOL_ERROR |
| 7 | `Should_Throw_When_SchemePseudoHeaderInResponse` | Fact | HV-007: Request pseudo-header :scheme in response is PROTOCOL_ERROR |
| 8 | `Should_Throw_When_AuthorityPseudoHeaderInResponse` | Fact | HV-008: Request pseudo-header :authority in response is PROTOCOL_ERROR |
| 9 | `Should_Throw_When_UnknownPseudoHeaderInResponse` | Fact | HV-009: Unknown pseudo-header is PROTOCOL_ERROR |
| 10 | `Should_Throw_When_PseudoHeaderAfterRegularHeader` | Fact | HV-010: Pseudo-header :status after regular header is PROTOCOL_ERROR |
| 11 | `Should_Throw_When_UppercaseHeaderName` | Fact | HV-011: Uppercase header name is PROTOCOL_ERROR (RFC 9113 §8.2) |
| 12 | `Should_Throw_When_UppercaseInPseudoHeaderName` | Fact | HV-012: Uppercase in pseudo-header name itself is PROTOCOL_ERROR |
| 13 | `Should_Throw_When_ConnectionHeaderPresent` | Fact | HV-013: 'connection' header is PROTOCOL_ERROR in HTTP/2 |
| 14 | `Should_Throw_When_KeepAliveHeaderPresent` | Fact | HV-014: 'keep-alive' header is PROTOCOL_ERROR in HTTP/2 |
| 15 | `Should_Throw_When_ProxyConnectionHeaderPresent` | Fact | HV-015: 'proxy-connection' header is PROTOCOL_ERROR in HTTP/2 |
| 16 | `Should_Throw_When_TransferEncodingHeaderPresent` | Fact | HV-016: 'transfer-encoding' header is PROTOCOL_ERROR in HTTP/2 |
| 17 | `Should_Throw_When_UpgradeHeaderPresent` | Fact | HV-017: 'upgrade' header is PROTOCOL_ERROR in HTTP/2 |
| 18 | `Should_Accept_When_MultipleRegularHeadersAfterStatus` | Fact | HV-018: Valid response with :status and multiple regular headers is accepted |
| 19 | `Should_Accept_When_Status404` | Fact | HV-019: Valid 404 response is accepted |
| 20 | `Should_Accept_When_Status301WithLocationHeader` | Fact | HV-020: Valid 301 redirect response with location header is accepted |
| 21 | `Should_IncludeHeaderName_In_UppercaseErrorMessage` | Fact | HV-021: PROTOCOL_ERROR message for uppercase includes the offending header name |
| 22 | `Should_IncludeHeaderName_In_ConnectionSpecificErrorMessage` | Fact | HV-022: PROTOCOL_ERROR message for connection-specific includes the header name |
| 23 | `Should_Throw_When_UppercaseInContinuationHeaderBlock` | Fact | HV-023: Validation applies to reassembled headers from CONTINUATION frames |
| 24 | `Should_Throw_On_SecondStream_When_SecondStreamHasMissingStatus` | Fact | HV-024: Each stream's HEADERS block is validated independently |
| 25 | `Should_Accept_When_Status100Informational` | Fact | HV-025: Valid 100 Continue response (HEADERS with endStream=false) is accepted |
| 26 | `Should_Accept_When_AllLowercaseCustomHeader` | Fact | HV-026: All-lowercase custom header name is accepted |
| 27 | `Should_Throw_When_UppercaseInMiddleOfHeaderName` | Fact | HV-027: Header name with uppercase in the middle is PROTOCOL_ERROR |
| 28 | `Should_Throw_When_HeaderBlockIsEmpty` | Fact | HV-028: Empty header block with no :status is PROTOCOL_ERROR |

#### `07_ErrorHandlingTests.cs` - `Http2ErrorMappingTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Http2Exception_Default_IsConnectionScope` | Fact | EM-001: Http2Exception defaults to Connection scope |
| 2 | `Http2Exception_StreamScope_IsConnectionError_False` | Fact | EM-002: Http2Exception stream scope sets IsConnectionError=false |
| 3 | `Http2Exception_ErrorCode_PreservedWithScope` | Fact | EM-003: Http2Exception preserves ErrorCode when scope is set |
| 4 | `Http2Exception_ConnectionScope_StreamIdIsZero` | Fact | EM-004: Http2Exception connection scope has StreamId=0 |
| 5 | `DataOnStream0_IsConnectionProtocolError` | Fact | EM-005: DATA on stream 0 is connection PROTOCOL_ERROR |
| 6 | `DataOnIdleStream_IsConnectionProtocolError` | Fact | EM-006: DATA on idle stream is connection PROTOCOL_ERROR |
| 7 | `ContinuationWithoutHeaders_IsConnectionProtocolError` | Fact | EM-007: CONTINUATION without preceding HEADERS is connection PROTOCOL_ERROR |
| 8 | `PingWrongLength_IsConnectionFrameSizeError` | Fact | EM-008: PING with wrong payload size is connection FRAME_SIZE_ERROR |
| 9 | `SettingsWrongLength_IsConnectionFrameSizeError` | Fact | EM-009: SETTINGS with non-multiple-of-6 length is connection FRAME_SIZE_ERROR |
| 10 | `DataExceedingConnectionWindow_IsConnectionFlowControlError` | Fact | EM-010: DATA exceeding connection receive window is connection FLOW_CONTROL_ERROR |
| 11 | `WindowUpdateConnectionOverflow_IsConnectionFlowControlError` | Fact | EM-011: WINDOW_UPDATE connection overflow is connection FLOW_CONTROL_ERROR |
| 12 | `DataExceedingStreamWindow_IsStreamFlowControlError` | Fact | EM-012: DATA exceeding stream receive window is stream FLOW_CONTROL_ERROR |
| 13 | `StreamFlowControlError_CarriesStreamId` | Fact | EM-013: Stream FLOW_CONTROL_ERROR carries the affected stream ID |
| 14 | `WindowUpdateStreamOverflow_IsStreamFlowControlError` | Fact | EM-014: WINDOW_UPDATE stream overflow is stream FLOW_CONTROL_ERROR |
| 15 | `DataOnClosedStream_IsStreamStreamClosedError` | Fact | EM-015: DATA on closed stream is stream STREAM_CLOSED error |
| 16 | `DataOnClosedStream_CarriesClosedStreamId` | Fact | EM-016: STREAM_CLOSED stream error carries the closed stream's ID |
| 17 | `ExceedMaxConcurrentStreams_IsStreamRefusedStreamError` | Fact | EM-017: Exceeding MAX_CONCURRENT_STREAMS is stream REFUSED_STREAM error |
| 18 | `RefusedStream_CarriesStreamId` | Fact | EM-018: REFUSED_STREAM carries the refused stream's ID |
| 19 | `HeadersOnClosedStream_IsConnectionStreamClosedError` | Fact | EM-019: HEADERS on closed stream is connection STREAM_CLOSED error |
| 20 | `HeadersOnClosedStream_ConnectionScope_NotStreamScope` | Fact | EM-020: HEADERS closed-stream error is connection-scoped (RFC 7540 §6.2) |
| 21 | `RstStreamWrongLength_IsConnectionFrameSizeError` | Fact | EM-021: RST_STREAM with wrong payload length is connection FRAME_SIZE_ERROR |
| 22 | `WindowUpdateZeroIncrement_IsProtocolError` | Fact | EM-022: WINDOW_UPDATE with increment=0 is PROTOCOL_ERROR |
| 23 | `SettingsAckWithPayload_IsFrameSizeError` | Fact | EM-023: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR |
| 24 | `Http2Exception_Scope_IsMutuallyExclusive` | Fact | EM-024: Stream and connection errors are mutually exclusive |
| 25 | `StreamAndConnectionFlowControlError_HaveDifferentScopes` | Fact | EM-025: Stream-level FLOW_CONTROL_ERROR and connection-level have different scopes |

#### `04_SettingsTests.cs` - `Http2SettingsSynchronizationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Preface_IncludesSettingsFrame` | Fact | RFC7540-3.5-SS-001: BuildConnectionPreface produces magic + SETTINGS frame |
| 2 | `Preface_SettingsIsOnStreamZero` | Fact | RFC7540-3.5-SS-002: Connection preface SETTINGS is on stream 0 |
| 3 | `Preface_SettingsContainsHeaderTableSize4096` | Fact | RFC7540-3.5-SS-003: Connection preface SETTINGS contains HeaderTableSize=4096 |
| 4 | `Preface_SettingsContainsEnablePush0` | Fact | RFC7540-3.5-SS-004: Connection preface SETTINGS contains EnablePush=0 |
| 5 | `Preface_SettingsContainsMaxFrameSize16384` | Fact | RFC7540-3.5-SS-005: Connection preface SETTINGS contains MaxFrameSize=16384 |
| 6 | `Settings_MaxFrameSize_BelowMin_ThrowsProtocolError` | Fact | RFC7540-6.5.2-SS-006: MaxFrameSize=16383 is PROTOCOL_ERROR |
| 7 | `Settings_MaxFrameSize_AboveMax_ThrowsProtocolError` | Fact | RFC7540-6.5.2-SS-007: MaxFrameSize=16777216 is PROTOCOL_ERROR |
| 8 | `Settings_MaxFrameSize_Update_AllowsLargerFrames` | Fact | RFC7540-6.5.2-SS-008: After MaxFrameSize update, larger frames are accepted |
| 9 | `Settings_InitialWindowSize_Overflow_ThrowsFlowControlError` | Fact | RFC7540-6.5.2-SS-009: InitialWindowSize above 2^31-1 is FLOW_CONTROL_ERROR |
| 10 | `Settings_InitialWindowSize_MaxValid_Accepted` | Fact | RFC7540-6.5.2-SS-010: InitialWindowSize of exactly 2^31-1 is accepted |
| 11 | `Settings_EnablePush_Zero_Accepted` | Fact | RFC7540-6.5.2-SS-011: EnablePush=0 is accepted |
| 12 | `Settings_EnablePush_One_Accepted` | Fact | RFC7540-6.5.2-SS-012: EnablePush=1 is accepted |
| 13 | `Settings_EnablePush_Two_ThrowsProtocolError` | Fact | RFC7540-6.5.2-SS-013: EnablePush=2 is PROTOCOL_ERROR |
| 14 | `Settings_MaxConcurrentStreams_Zero_BlocksAllStreams` | Fact | RFC7540-6.5.2-SS-014: MaxConcurrentStreams=0 blocks all new streams |
| 15 | `Settings_MaxConcurrentStreams_One_AllowsOneStream` | Fact | RFC7540-6.5.2-SS-015: MaxConcurrentStreams=1 allows exactly one stream |
| 16 | `Settings_HeaderTableSize_Zero_Accepted` | Fact | RFC7541-4.2-SS-016: HeaderTableSize=0 accepted and applied to HPACK decoder |
| 17 | `Settings_HeaderTableSize_1024_Accepted` | Fact | RFC7541-4.2-SS-017: HeaderTableSize=1024 accepted and applied |
| 18 | `Settings_HeaderTableSize_Default_Accepted` | Fact | RFC7541-4.2-SS-018: HeaderTableSize=4096 (default) accepted and applied |
| 19 | `HandleSettings_NonAck_ProducesSettingsAck` | Fact | RFC7540-6.5-SS-019: Non-ACK SETTINGS produces one SETTINGS ACK to send |
| 20 | `HandleSettings_AckFrame_ProducesNoAck` | Fact | RFC7540-6.5-SS-020: SETTINGS ACK frame produces no new ACK in return |
| 21 | `HandleSettings_ThreeSettings_ProducesThreeAcks` | Fact | RFC7540-6.5-SS-021: Three SETTINGS frames produce three ACKs to send |
| 22 | `HandleSettings_EmptyPayload_ProducesAck` | Fact | RFC7540-6.5-SS-022: Empty SETTINGS frame (zero parameters) produces ACK |
| 23 | `EncodeSettingsAck_ProducesValidAckFrame` | Fact | RFC7540-6.5-SS-023: Encoded SETTINGS ACK is a valid 9-byte frame |
| 24 | `Settings_FloodProtection_ThrowsAfterLimit` | Fact | RFC7540-security-SS-024: SETTINGS flood above 100 frames throws EnhanceYourCalm |
| 25 | `Settings_AckFrames_DoNotCountTowardFloodLimit` | Fact | RFC7540-security-SS-025: SETTINGS ACK frames do not count toward flood limit |
| 26 | `Settings_FloodCounter_ClearedOnReset` | Fact | RFC7540-security-SS-026: Reset clears SETTINGS flood counter |
| 27 | `Settings_UnknownParameterId_Ignored` | Fact | RFC7540-6.5-SS-027: Unknown SETTINGS parameter ID is silently ignored |
| 28 | `Settings_MultipleParameters_AllApplied` | Fact | RFC7540-6.5-SS-028: Multiple parameters in one SETTINGS frame are all applied |
| 29 | `Settings_InitialWindowSize_IncreaseOverflowsOpenStreamWindow_ThrowsFlowControlError` | Fact | RFC7540-6.9.2-SS-029: InitialWindowSize increase overflows open stream send window is FLOW_CONTROL_ERROR |

#### `01_ConnectionPrefaceTests.cs` - `Http2ConnectionPrefaceTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ClientPreface_MagicOctets_MatchRfc9113Spec` | Fact | RFC9113-3.4-CP-001: Client preface starts with exact magic octets |
| 2 | `ClientPreface_Magic_IsExactly24Bytes` | Fact | RFC9113-3.4-CP-002: Client preface magic is exactly 24 bytes |
| 3 | `ClientPreface_SettingsFrame_ImmediatelyFollowsMagic` | Fact | RFC9113-3.4-CP-003: SETTINGS frame follows magic immediately at byte 24 |
| 4 | `ClientPreface_SettingsFrame_StreamIdIsZero` | Fact | RFC9113-3.4-CP-004: SETTINGS frame in client preface uses stream ID 0 |
| 5 | `ClientPreface_Length_IsMagicPlusSettingsFrame` | Fact | RFC9113-3.4-CP-005: Client preface total length is magic + SETTINGS frame |
| 6 | `ClientPreface_SettingsPayload_LengthIsMultipleOf6` | Fact | RFC9113-3.4-CP-006: SETTINGS frame payload length is a multiple of 6 |
| 7 | `ClientPreface_SettingsFrame_FlagsAreZero` | Fact | RFC9113-3.4-CP-007: SETTINGS frame flags are 0 (not ACK) |
| 8 | `ClientPreface_Magic_SpellsCorrectAsciiString` | Fact | RFC9113-3.4-CP-008: Magic bytes spell 'PRI * HTTP/2.0 SM' as ASCII |
| 9 | `ServerPreface_ValidSettingsFrame_ReturnsTrue` | Fact | RFC9113-3.4-SP-001: Valid SETTINGS frame on stream 0 is accepted |
| 10 | `ServerPreface_FewerThan9Bytes_ReturnsFalse` | Fact | RFC9113-3.4-SP-002: Fewer than 9 bytes returns false (need more data) |
| 11 | `ServerPreface_DataFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-003: DATA frame as first frame throws PROTOCOL_ERROR |
| 12 | `ServerPreface_HeadersFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-004: HEADERS frame as first frame throws PROTOCOL_ERROR |
| 13 | `ServerPreface_PingFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-005: PING frame as first frame throws PROTOCOL_ERROR |
| 14 | `ServerPreface_GoAwayFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-006: GOAWAY frame as first frame throws PROTOCOL_ERROR |
| 15 | `ServerPreface_RstStreamFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-007: RST_STREAM frame as first frame throws PROTOCOL_ERROR |
| 16 | `ServerPreface_WindowUpdateFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-008: WINDOW_UPDATE frame as first frame throws PROTOCOL_ERROR |
| 17 | `ServerPreface_SettingsFrameOnNonZeroStream_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-009: SETTINGS frame on non-zero stream throws PROTOCOL_ERROR |
| 18 | `ServerPreface_Exactly9BytesOfSettingsOnStream0_ReturnsTrue` | Fact | RFC9113-3.4-SP-010: Exactly 9 bytes of SETTINGS on stream 0 is accepted |
| 19 | `ServerPreface_MultipleDecoders_ValidateIndependently` | Fact | RFC9113-3.4-SP-011: Multiple decoders each validate their own preface independently |
| 20 | `ServerPreface_ContinuationFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-012: CONTINUATION frame as first frame throws PROTOCOL_ERROR |
| 21 | `ServerPreface_PriorityFrame_ThrowsProtocolError` | Fact | RFC9113-3.4-SP-013: PRIORITY frame as first frame throws PROTOCOL_ERROR |
| 22 | `ClientPreface_FollowedByServerSettingsAck_ValidatesCorrectly` | Fact | RFC9113-3.4-RT-001: Encoder preface passes validation if server echoes SETTINGS |
| 23 | `ClientPreface_SettingsEntries_AreEach6Bytes` | Fact | RFC9113-3.4-RT-002: Client preface SETTINGS payload entries are each 6 bytes |
| 24 | `FrameHeader_Valid9Bytes_DecodedCorrectly` | Fact | 7540-4.1-001: Valid 9-byte frame header decoded correctly |
| 25 | `FrameHeader_LargePayload_24BitLengthParsed` | Fact | 7540-4.1-002: Frame length uses 24-bit field |
| 26 | `FrameType_AllKnownTypes_DispatchedWithoutCrash` | Theory | 7540-4.1-003: Frame type {0} dispatched to correct handler |
| 27 | `FrameType_Unknown0x0A_SilentlyIgnored` | Fact | 7540-4.1-004: Unknown frame type 0x0A — silently ignored per RFC 9113 §5.5 |
| 28 | `FrameHeader_RBitSetInGoAway_LastStreamIdMasked` | Fact | 7540-4.1-005: R-bit masked out when reading GoAway last-stream-id |
| 29 | `FrameHeader_RBitSetInStreamId_StrippedSilently` | Fact | 7540-4.1-006: R-bit in stream ID is silently stripped by Http2FrameDecoder |
| 30 | `FrameHeader_PayloadExceedsMaxFrameSize_ProcessedByFrameDecoder` | Fact | 7540-4.1-007: Oversized DATA frame — Http2FrameDecoder does not enforce MAX_FRAME_SIZE |
| 31 | `DataFrame_Payload_DecodedCorrectly` | Fact | 7540-6.1-001: DATA frame received — response available on stream |
| 32 | `DataFrame_EndStream_MarksStreamClosed` | Fact | 7540-6.1-002: END_STREAM on DATA marks stream closed |
| 33 | `DataFrame_Padded_PaddingStripped` | Fact | 7540-6.1-003: Padded DATA frame processed — response status correct |
| 34 | `DataFrame_Stream0_ThrowsProtocolError` | Fact | 7540-6.1-004: DATA on stream 0 is PROTOCOL_ERROR |
| 35 | `DataFrame_ClosedStream_ThrowsStreamClosed` | Fact | 7540-6.1-005: DATA on closed stream causes STREAM_CLOSED |
| 36 | `DataFrame_EmptyWithEndStream_ResponseComplete` | Fact | 7540-6.1-006: Empty DATA frame with END_STREAM valid |
| 37 | `HeadersFrame_ResponseHeaders_Decoded` | Fact | 7540-6.2-001: HEADERS frame decoded into response headers |
| 38 | `HeadersFrame_EndStream_StreamClosedImmediately` | Fact | 7540-6.2-002: END_STREAM on HEADERS closes stream immediately |
| 39 | `HeadersFrame_EndHeaders_HeaderBlockComplete` | Fact | 7540-6.2-003: END_HEADERS on HEADERS marks complete block |
| 40 | `HeadersFrame_Padded_PaddingStripped` | Fact | 7540-6.2-004: Padded HEADERS padding stripped |
| 41 | `HeadersFrame_PriorityFlag_ConsumedCorrectly` | Fact | 7540-6.2-005: PRIORITY flag in HEADERS consumed correctly |
| 42 | `HeadersFrame_WithoutEndHeaders_WaitsForContinuation` | Fact | 7540-6.2-006: HEADERS without END_HEADERS waits for CONTINUATION |
| 43 | `HeadersFrame_Stream0_ThrowsProtocolError` | Fact | 7540-6.2-007: HEADERS on stream 0 is PROTOCOL_ERROR |
| 44 | `ContinuationFrame_AppendedToHeaders_HeaderBlockMerged` | Fact | 7540-6.9-001: CONTINUATION appended to HEADERS block |
| 45 | `ContinuationFrame_EndHeaders_CompletesBlock` | Fact | 7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block |
| 46 | `ContinuationFrame_Multiple_AllMerged` | Fact | 7540-6.9-003: Multiple CONTINUATION frames all merged |
| 47 | `ContinuationFrame_WrongStream_ThrowsProtocolError` | Fact | 7540-6.9-004: CONTINUATION on wrong stream is PROTOCOL_ERROR |
| 48 | `ContinuationFrame_NonContinuationAfterHeaders_ThrowsProtocolError` | Fact | 7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR |
| 49 | `ContinuationFrame_Stream0_ThrowsProtocolError` | Fact | 7540-6.9-006: CONTINUATION on stream 0 is PROTOCOL_ERROR |
| 50 | `ContinuationFrame_WithoutPrecedingHeaders_ThrowsProtocolError` | Fact | dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR |

#### `02_FrameParsingTests.cs` - `Http2FrameParsingCoreTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `FrameHeader_ZeroBytes_ReturnsFalse` | Fact | RFC7540-4.1-FP-001: Zero bytes returns empty (NeedMoreData) |
| 2 | `FrameHeader_EightBytes_ReturnsFalse` | Fact | RFC7540-4.1-FP-002: 8 bytes (one short of frame header) returns empty |
| 3 | `FrameHeader_Exactly9BytesEmptyPayload_IsDecoded` | Fact | RFC7540-4.1-FP-003: Exactly 9 bytes with zero-length payload is decoded |
| 4 | `FrameHeader_ZeroLengthField_IsAccepted` | Fact | RFC7540-4.1-FP-004: Frame with 0 payload length field accepted |
| 5 | `FrameHeader_FragmentedAcrossCallsReassembled` | Fact | RFC7540-4.1-FP-005: Frame buffered across two decode calls (fragmented) |
| 6 | `FrameHeader_LargePayloadUses24BitLength` | Fact | RFC7540-4.1-FP-006: Length field uses all 24 bits (payload > 65535) |
| 7 | `FrameSize_DefaultMaxIs16384` | Fact | RFC7540-4.2-FP-007: Default MAX_FRAME_SIZE is 16384 (2^14) |
| 8 | `FrameSize_OneBeyondMax_ThrowsFrameSizeError` | Fact | RFC7540-4.2-FP-008: Frame 1 byte over MAX_FRAME_SIZE causes FRAME_SIZE_ERROR |
| 9 | `FrameSize_AfterSettingsUpdate_LargerFrameAccepted` | Fact | RFC7540-4.2-FP-009: Larger frames can be sent if SETTINGS permits |
| 10 | `FrameSize_MaxFrameSizeBelowMin_ThrowsProtocolError` | Fact | RFC7540-4.2-FP-010: SETTINGS_MAX_FRAME_SIZE below 16384 is PROTOCOL_ERROR |
| 11 | `FrameSize_MaxFrameSizeAboveMax_ThrowsProtocolError` | Fact | RFC7540-4.2-FP-011: SETTINGS_MAX_FRAME_SIZE above 16777215 is PROTOCOL_ERROR |
| 12 | `FrameSize_MaxFrameSizeAtMaxBoundary_IsAccepted` | Fact | RFC7540-4.2-FP-012: SETTINGS_MAX_FRAME_SIZE of exactly 16777215 is accepted |
| 13 | `FrameType_Unknown0x0F_IsIgnored` | Fact | RFC7540-4.1-FP-013: Unknown frame type is decoded |
| 14 | `FrameType_MultipleUnknown_AllIgnored` | Fact | RFC7540-4.1-FP-014: Multiple unknown frame types in sequence are decoded |
| 15 | `FrameType_UnknownWithLargePayload_IsIgnored` | Fact | RFC7540-4.1-FP-015: Unknown frame type with maximum payload is handled |
| 16 | `Settings_OnNonZeroStream_ThrowsProtocolError` | Fact | RFC7540-6.5-FP-016: SETTINGS on non-zero stream causes PROTOCOL_ERROR |
| 17 | `Ping_OnNonZeroStream_ThrowsProtocolError` | Fact | RFC7540-6.7-FP-017: PING on non-zero stream causes PROTOCOL_ERROR |
| 18 | `GoAway_OnNonZeroStream_ThrowsProtocolError` | Fact | RFC7540-6.8-FP-018: GOAWAY on non-zero stream causes PROTOCOL_ERROR |
| 19 | `WindowUpdate_OnStream0_IsAccepted` | Fact | RFC7540-6.9-FP-019: WINDOW_UPDATE on stream 0 (connection-level) is accepted |
| 20 | `WindowUpdate_OnNonZeroStream_IsAccepted` | Fact | RFC7540-6.9-FP-020: WINDOW_UPDATE on non-zero stream (stream-level) is accepted |
| 21 | `Settings_NonMultipleOf6Payload_ThrowsFrameSizeError` | Fact | RFC7540-6.5-FP-021: SETTINGS payload not multiple of 6 is FRAME_SIZE_ERROR |
| 22 | `Settings_AckWithNonEmptyPayload_ThrowsFrameSizeError` | Fact | RFC7540-6.5-FP-022: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR |
| 23 | `Ping_SevenBytePayload_ThrowsFrameSizeError` | Fact | RFC7540-6.7-FP-023: PING with 7-byte payload is FRAME_SIZE_ERROR |
| 24 | `Ping_NineBytePayload_ThrowsFrameSizeError` | Fact | RFC7540-6.7-FP-024: PING with 9-byte payload is FRAME_SIZE_ERROR |
| 25 | `WindowUpdate_ThreeBytePayload_ThrowsFrameSizeError` | Fact | RFC7540-6.9-FP-025: WINDOW_UPDATE with 3-byte payload is FRAME_SIZE_ERROR |
| 26 | `RstStream_ThreeBytePayload_ThrowsFrameSizeError` | Fact | RFC7540-6.4-FP-026: RST_STREAM with 3-byte payload is FRAME_SIZE_ERROR |
| 27 | `RstStream_FiveBytePayload_ThrowsFrameSizeError` | Fact | RFC7540-6.4-FP-027: RST_STREAM with 5-byte payload is FRAME_SIZE_ERROR |
| 28 | `Settings_UnknownFlagBits_AreIgnored` | Fact | RFC7540-4.1-FP-028: SETTINGS with unknown flag bits set is processed normally |
| 29 | `Ping_UnknownFlagBitsOnAck_AreIgnored` | Fact | RFC7540-4.1-FP-029: PING ACK with unknown flag bits set is processed normally |
| 30 | `GoAway_WithDebugData_ParsedCorrectly` | Fact | RFC7540-4.1-FP-030: GoAway frame with debug data parsed correctly |
| 31 | `Continuation_WithoutPrecedingHeaders_ThrowsProtocolError` | Fact | RFC7540-5.1-FP-031: CONTINUATION without preceding HEADERS causes PROTOCOL_ERROR |
| 32 | `NonContinuation_AfterHeadersWithoutEndHeaders_ThrowsProtocolError` | Fact |  |

#### `03_StreamStateMachineTests.cs` - `Http2StreamLifecycleTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `GetLifecycleState_UnknownStream_ReturnsIdle` | Fact | RFC9113-5.1-SS-001: Unknown stream ID reports Idle state |
| 2 | `Headers_NoEndStream_StreamBecomesOpen` | Fact | RFC9113-5.1-SS-002: HEADERS frame (no END_STREAM) moves stream from Idle to Open |
| 3 | `Headers_WithEndStream_StreamBecomesClosed` | Fact | RFC9113-5.1-SS-003: HEADERS+END_STREAM moves stream directly to Closed |
| 4 | `Data_WithEndStream_AfterHeaders_StreamBecomesClosed` | Fact | RFC9113-5.1-SS-004: DATA+END_STREAM after HEADERS closes the stream |
| 5 | `RstStream_MovesStream_ToClosed` | Fact | RFC9113-5.1-SS-005: RST_STREAM closes the stream |
| 6 | `Data_OnIdleStream_ThrowsConnectionError` | Fact | RFC9113-5.1-SS-006: DATA on idle stream (no HEADERS) is a connection error |
| 7 | `Data_OnClosedStream_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-007: DATA on closed stream is STREAM_CLOSED error |
| 8 | `Headers_OnClosedStream_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-008: HEADERS on closed stream is STREAM_CLOSED error (RFC 7540 §6.2) |
| 9 | `AutoClose_HeadersEndStream_ProducesResponse` | Fact | RFC9113-5.1-SS-009: Auto-close: HEADERS+END_STREAM produces response immediately |
| 10 | `AutoClose_DataEndStream_ClosesStream` | Fact | RFC9113-5.1-SS-010: Auto-close: DATA+END_STREAM closes stream |
| 11 | `AutoClose_MultipleStreams_EachClosedIndependently` | Fact | RFC9113-5.1-SS-011: Auto-close: multiple streams closed independently via END_STREAM |
| 12 | `StreamIsOpen_WhileDataAccumulates_ClosedOnEndStream` | Fact | RFC9113-5.1-SS-012: Stream open while DATA accumulates, then closed on END_STREAM |
| 13 | `MultipleStreams_IndependentLifecycleStates` | Fact | RFC9113-5.1-SS-013: Different streams have independent lifecycle states |
| 14 | `CloseOneStream_OtherRemainsOpen` | Fact | RFC9113-5.1-SS-014: Closing one stream does not affect other open streams |
| 15 | `RstStream_OnOneStream_DoesNotAffectOthers` | Fact | RFC9113-5.1-SS-015: RST_STREAM on open stream does not affect other streams |
| 16 | `Data_AfterRstStream_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-016: DATA after RST_STREAM on same stream throws STREAM_CLOSED |
| 17 | `Headers_AfterRstStream_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-017: HEADERS after RST_STREAM on same stream is STREAM_CLOSED error (RFC 7540 §6.2) |
| 18 | `Data_OnHalfClosedRemoteStream_ViaData_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-018: DATA on stream with only DATA+END_STREAM received (half-closed-remote) throws STREAM_CLOSED |
| 19 | `Reset_ClearsAllLifecycleStates` | Fact | RFC9113-5.1-SS-019: New session clears all lifecycle states back to Idle |
| 20 | `AfterReset_StreamIdsCanBeReused` | Fact | RFC9113-5.1-SS-020: After new session, stream IDs can be reused (back to Idle) |
| 21 | `Data_OnStream0_ThrowsProtocolError` | Fact | RFC9113-5.1-SS-021: DATA on stream 0 is PROTOCOL_ERROR (stream 0 is for control only) |
| 22 | `Headers_OnStream0_ThrowsProtocolError` | Fact | RFC9113-5.1-SS-022: HEADERS on stream 0 is PROTOCOL_ERROR |
| 23 | `Data_OnDifferentIdleStream_ThrowsConnectionError` | Fact | RFC9113-5.1-SS-023: DATA on a different idle stream than already-open streams is a connection error |
| 24 | `FiveStreams_DistinctLifecycles_AllTrackedCorrectly` | Fact | RFC9113-5.1-SS-024: Five streams each with distinct lifecycle states are tracked correctly |
| 25 | `Data_OnRstStreamClosedStream_ThrowsStreamClosed` | Fact | RFC9113-5.1-SS-025: DATA on a known closed stream reports STREAM_CLOSED after RST_STREAM |

#### `13_DecoderStreamFlowControlTests.cs` - `Http2DecoderStreamFlowControlTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `FlowControl_InitialConnectionReceiveWindow_Is65535` | Fact | 7540-5.2-dec-001: New stream initial window is 65535 |
| 2 | `FlowControl_WindowUpdateDecoded_WindowUpdated` | Fact | 7540-5.2-dec-002: WINDOW_UPDATE decoded and window updated |
| 3 | `FlowControl_PeerDataExceedsReceiveWindow_ThrowsFlowControlError` | Fact | 7540-5.2-dec-003: Peer DATA beyond window causes FLOW_CONTROL_ERROR |
| 4 | `FlowControl_WindowUpdateOverflow_ThrowsFlowControlError` | Fact | 7540-5.2-dec-004: WINDOW_UPDATE overflow causes FLOW_CONTROL_ERROR |
| 5 | `FlowControl_WindowUpdateIncrementZero_ThrowsProtocolError` | Fact | 7540-5.2-dec-008: WINDOW_UPDATE increment=0 causes PROTOCOL_ERROR |

#### `14_DecoderErrorCodeTests.cs` - `Http2DecoderErrorCodeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ErrorCode_NoError_InGoAway_Decoded` | Fact | 7540-err-000: NO_ERROR (0x0) in GOAWAY decoded |
| 2 | `ErrorCode_ProtocolError_InRstStream_Decoded` | Fact | 7540-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded |
| 3 | `ErrorCode_InternalError_InGoAway_Decoded` | Fact | 7540-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded |
| 4 | `ErrorCode_FlowControlError_InGoAway_Decoded` | Fact | 7540-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded |
| 5 | `ErrorCode_SettingsTimeout_InGoAway_Decoded` | Fact | 7540-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded |
| 6 | `ErrorCode_StreamClosed_InRstStream_Decoded` | Fact | 7540-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded |
| 7 | `ErrorCode_FrameSizeError_InRstStream_Decoded` | Fact | 7540-err-006: FRAME_SIZE_ERROR (0x6) decoded |
| 8 | `ErrorCode_RefusedStream_InRstStream_Decoded` | Fact | 7540-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded |
| 9 | `ErrorCode_Cancel_InRstStream_Decoded` | Fact | 7540-err-008: CANCEL (0x8) in RST_STREAM decoded |
| 10 | `ErrorCode_CompressionError_InGoAway_Decoded` | Fact | 7540-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded |
| 11 | `ErrorCode_ConnectError_InRstStream_Decoded` | Fact | 7540-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded |
| 12 | `ErrorCode_EnhanceYourCalm_InGoAway_Decoded` | Fact | 7540-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded |
| 13 | `ErrorCode_InadequateSecurity_InRstStream_Decoded` | Fact | 7540-err-00c: INADEQUATE_SECURITY (0xc) decoded |
| 14 | `ErrorCode_Http11Required_InGoAway_Decoded` | Fact | 7540-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded |

#### `15_RoundTripHandshakeTests.cs` - `Http2RoundTripHandshakeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ContainMagicAndSettings_When_ConnectionPrefaceBuilt` | Fact | RT-2-001: HTTP/2 connection preface + SETTINGS exchange |
| 2 | `Should_Return200_When_Http2GetRoundTrip` | Fact | RT-2-002: HTTP/2 GET → 200 on stream 1 |
| 3 | `Should_Return201_When_Http2PostRoundTrip` | Fact | RT-2-003: HTTP/2 POST → HEADERS+DATA → 201 response |
| 4 | `Should_ReturnThreeResponses_When_ThreeConcurrentStreams` | Fact | RT-2-004: HTTP/2 three concurrent streams each complete independently |
| 5 | `Should_ProduceSmallerHpackBlock_When_DynamicTableReuseAcrossRequests` | Fact | RT-2-005: HTTP/2 HPACK dynamic table reused across three requests |
| 6 | `Should_ApplyServerSettings_When_SettingsReceivedAndAcked` | Fact | RT-2-006: HTTP/2 server SETTINGS → client ACK → both sides updated |
| 7 | `Should_ReturnPingAckWithSamePayload_When_ServerPingReceived` | Fact | RT-2-007: HTTP/2 server PING → client PONG with same payload |
| 8 | `Should_SignalGoAway_When_GoAwayFrameReceived` | Fact | RT-2-008: HTTP/2 GOAWAY received → no new requests sent |
| 9 | `Should_DropStream1AndCompleteStream3_When_RstStreamReceived` | Fact | RT-2-009: HTTP/2 RST_STREAM → stream dropped, other streams continue |
| 10 | `Should_NeverIndexAuthorization_When_AuthorizationHeaderEncoded` | Fact | RT-2-010: Authorization header NeverIndexed in HTTP/2 round-trip |
| 11 | `Should_NeverIndexCookie_When_CookieHeaderEncoded` | Fact | RT-2-011: Cookie header NeverIndexed in HTTP/2 round-trip |
| 12 | `Should_UseContinuationFrames_When_HeadersExceedMaxFrameSize` | Fact | RT-2-012: HTTP/2 request with headers exceeding frame size uses CONTINUATION |
| 13 | `Should_DecodePromisedStream_When_PushPromiseReceived` | Fact | RT-2-013: HTTP/2 server PUSH_PROMISE decoded, pushed response received |
| 14 | `Should_UpdateSendWindow_When_ServerWindowUpdateReceived` | Fact | RT-2-014: HTTP/2 POST body larger than initial window uses WINDOW_UPDATE |
| 15 | `Should_Return404_When_Http2StreamReturnsNotFound` | Fact | RT-2-015: HTTP/2 request → 404 response on stream decoded |
| 16 | `Should_ReturnFalse_When_ServerPrefaceIncomplete` | Fact | RT-2-016: ValidateServerPreface returns false for incomplete frame (<9 bytes) |
| 17 | `Should_ThrowProtocolError_When_ServerPrefaceHasWrongFrameType` | Fact | RT-2-017: ValidateServerPreface throws on non-SETTINGS first frame |
| 18 | `Should_QueueOneAckPerSettingsFrame_When_MultipleSettingsReceived` | Fact | RT-2-018: Multiple sequential SETTINGS frames each produce one ACK |
| 19 | `Should_ConsumeSettingsAck_When_SettingsAckFrameReceived` | Fact | RT-2-019: SETTINGS ACK frame consumed silently, no response queued |

#### `11_DecoderStreamValidationTests.cs` - `Http2DecoderStreamValidationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `StreamState_EndStreamOnData_StreamCompleted` | Fact | 7540-5.1-003: END_STREAM on incoming DATA moves stream to half-closed remote |
| 2 | `StreamState_EndStreamOnHeaders_StreamFullyClosed` | Fact | 7540-5.1-004: Both sides END_STREAM closes stream |
| 3 | `StreamState_PushPromise_ReservesStream` | Fact | 7540-5.1-005: PUSH_PROMISE moves pushed stream to reserved remote |
| 4 | `StreamState_DataOnClosedStream_ThrowsStreamClosed` | Fact | 7540-5.1-006: DATA on closed stream causes STREAM_CLOSED error |
| 5 | `StreamState_ReuseClosedStreamId_ThrowsStreamClosed` | Fact | 7540-5.1-007: HEADERS on closed stream is connection error STREAM_CLOSED (RFC 7540 §6.2) |
| 6 | `StreamState_EvenStreamIdWithoutPushPromise_ThrowsProtocolError` | Fact | 7540-5.1-008: Client even stream ID causes PROTOCOL_ERROR |
| 7 | `StreamState_DataOnStream0_ThrowsProtocolError` | Fact | 7540-5.1-009: DATA on stream 0 is PROTOCOL_ERROR |
| 8 | `StreamState_HeadersOnStream0_ThrowsProtocolError` | Fact | 7540-5.1-010: HEADERS on stream 0 is PROTOCOL_ERROR |

#### `08_GoAwayTests.cs` - `Http2GoAwayRstStreamTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `GetGoAwayLastStreamId_BeforeGoAway_ReturnsIntMaxValue` | Fact | RFC7540-6.8-GA-001: GetGoAwayLastStreamId returns int.MaxValue before GOAWAY |
| 2 | `IsGoingAway_BeforeGoAway_ReturnsFalse` | Fact | RFC7540-6.8-GA-002: IsGoingAway returns false before GOAWAY |
| 3 | `GetGoAwayLastStreamId_AfterGoAway_ReturnsLastStreamId` | Fact | RFC7540-6.8-GA-003: After GOAWAY received, GetGoAwayLastStreamId returns lastStreamId |
| 4 | `IsGoingAway_AfterGoAway_ReturnsTrue` | Fact | RFC7540-6.8-GA-004: After GOAWAY received, IsGoingAway returns true |
| 5 | `GetGoAwayLastStreamId_LastStreamIdZero_RecordedCorrectly` | Fact | RFC7540-6.8-GA-005: GOAWAY with lastStreamId=0 recorded correctly |
| 6 | `NewStream_AfterGoAway_ThrowsProtocolError` | Fact | RFC7540-6.8-GA-006: New stream HEADERS after GOAWAY throws PROTOCOL_ERROR |
| 7 | `NewStream_AfterGoAwayLastStreamIdZero_ThrowsProtocolError` | Fact | RFC7540-6.8-GA-007: GOAWAY with lastStreamId=0 blocks all new streams |
| 8 | `SecondGoAway_UpdatesLastStreamId` | Fact | RFC7540-6.8-GA-008: Second GOAWAY updates lastStreamId |
| 9 | `StreamsAboveLastStreamId_AfterGoAway_AreClosed` | Fact | RFC7540-6.8-GA-009: Streams with ID > lastStreamId are moved to Closed after GOAWAY |
| 10 | `ActiveStreamCount_AfterGoAway_DecrementedForCancelledStreams` | Fact | RFC7540-6.8-GA-010: Active stream count decremented for cleaned-up streams |
| 11 | `Data_ForCleanedUpStream_AfterGoAway_IsRejected` | Fact | RFC7540-6.8-GA-011: DATA for stream > lastStreamId after GOAWAY is rejected |
| 12 | `Data_ForStreamAtOrBelowLastStreamId_AfterGoAway_IsProcessed` | Fact | RFC7540-6.8-GA-012: DATA for stream ≤ lastStreamId after GOAWAY is still processed |
| 13 | `GoAway_LastStreamIdMaxValue_NothingCleaned` | Fact | RFC7540-6.8-GA-013: GOAWAY with lastStreamId=int.MaxValue cleans up nothing |
| 14 | `Reset_ClearsIsGoingAway` | Fact | RFC7540-6.8-GA-014: Reset() clears IsGoingAway flag |
| 15 | `Reset_RestoresGetGoAwayLastStreamId` | Fact | RFC7540-6.8-GA-015: Reset() restores GetGoAwayLastStreamId to int.MaxValue |
| 16 | `AfterReset_NewStreamsCanBeOpened` | Fact | RFC7540-6.8-GA-016: After Reset(), new streams can be opened again |
| 17 | `GoAway_OnNonZeroStream_ThrowsProtocolError` | Fact | RFC7540-6.8-GA-017: GOAWAY on non-zero stream throws PROTOCOL_ERROR |
| 18 | `GoAway_WithDebugData_StreamCleanupStillWorks` | Fact | RFC7540-6.8-GA-018: GOAWAY with debug data does not affect stream cleanup |
| 19 | `GoAway_DecodeResult_ContainsGoAwayDetails` | Fact | RFC7540-6.8-GA-019: GOAWAY result contains GoAway frame details |
| 20 | `GoAway_MultiplePendingStreams_AllAboveLastStreamIdCleaned` | Fact | RFC7540-6.8-GA-020: GOAWAY cleans up multiple streams above lastStreamId |

#### `09_ContinuationFrameTests.cs` - `Http2ContinuationFrameTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ProduceResponse_When_HeadersHasEndHeadersSet` | Fact | RFC9113-6.10-CF-001: HEADERS with END_HEADERS completes immediately without CONTINUATION |
| 2 | `Should_ProduceNoResponse_When_HeadersLacksEndHeaders` | Fact | RFC9113-6.10-CF-002: HEADERS without END_HEADERS produces no response until CONTINUATION |
| 3 | `Should_ProduceResponse_When_ContinuationHasEndHeaders` | Fact | RFC9113-6.10-CF-003: Single CONTINUATION with END_HEADERS completes header block |
| 4 | `Should_ProduceNoResponse_When_ContinuationLacksEndHeaders` | Fact | RFC9113-6.10-CF-004: CONTINUATION without END_HEADERS produces no response |
| 5 | `Should_ProduceResponse_When_ThreeContinuationFramesComplete` | Fact | RFC9113-6.10-CF-005: Three CONTINUATION frames with last having END_HEADERS produces response |
| 6 | `Should_PreserveHeaderValues_When_SplitAcrossContinuationFrames` | Fact | RFC9113-6.10-CF-006: Header values preserved across multiple CONTINUATION fragments |
| 7 | `Should_ThrowProtocolError_When_DataFrameInterleavesContinuation` | Fact | RFC9113-6.10-CF-007: DATA frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 8 | `Should_ThrowProtocolError_When_PingInterleavesContinuation` | Fact | RFC9113-6.10-CF-008: PING frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 9 | `Should_ThrowProtocolError_When_SettingsInterleavesContinuation` | Fact | RFC9113-6.10-CF-009: SETTINGS frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 10 | `Should_ThrowProtocolError_When_RstStreamInterleavesContinuation` | Fact | RFC9113-6.10-CF-010: RST_STREAM frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 11 | `Should_ThrowProtocolError_When_WindowUpdateInterleavesContinuation` | Fact | RFC9113-6.10-CF-011: WINDOW_UPDATE frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 12 | `Should_ThrowProtocolError_When_GoAwayInterleavesContinuation` | Fact | RFC9113-6.10-CF-012: GOAWAY frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR |
| 13 | `Should_ThrowProtocolError_When_HeadersForOtherStreamInterleavesContinuation` | Fact | RFC9113-6.10-CF-013: HEADERS frame for a different stream while awaiting CONTINUATION is PROTOCOL_ERROR |
| 14 | `Should_ThrowProtocolError_When_ContinuationOnStream0` | Fact | RFC9113-6.10-CF-014: CONTINUATION on stream 0 is PROTOCOL_ERROR |
| 15 | `Should_ThrowProtocolError_When_ContinuationOnDifferentStream` | Fact | RFC9113-6.10-CF-015: CONTINUATION on different stream than HEADERS is PROTOCOL_ERROR |
| 16 | `Should_ThrowProtocolError_When_ContinuationWithoutPrecedingHeaders` | Fact | RFC9113-6.10-CF-016: CONTINUATION without preceding HEADERS is PROTOCOL_ERROR |
| 17 | `Should_ThrowProtocolError_When_ContinuationAfterCompletedHeaderBlock` | Fact | RFC9113-6.10-CF-017: CONTINUATION after completed header block is PROTOCOL_ERROR |
| 18 | `Should_ProduceResponse_When_HeadersAndContinuationDeliveredTogether` | Fact | RFC9113-6.10-CF-018: HEADERS and CONTINUATION in same Process call are processed |
| 19 | `Should_ProduceResponse_When_ThreeFramesDeliveredTogether` | Fact | RFC9113-6.10-CF-019: HEADERS + three CONTINUATION frames in single Process call |
| 20 | `Should_BufferPartialContinuation_When_TcpFragmented` | Fact | RFC9113-6.10-CF-020: Fragmented CONTINUATION (partial frame bytes) buffered and completed |
| 21 | `Should_ClearPendingContinuation_When_Reset` | Fact | RFC9113-6.10-CF-021: Reset clears pending CONTINUATION state |
| 22 | `Should_IncludeStreamIdInErrorMessage_When_ContinuationOnWrongStream` | Fact | RFC9113-6.10-CF-022: Error message includes offending stream ID when CONTINUATION on wrong stream |
| 23 | `Should_ThrowProtocolError_When_ContinuationFloodExceeds1000Frames` | Fact | RFC9113-6.10-CF-023: CONTINUATION flood protection triggers at 1000 frames |
| 24 | `Should_CarryEndStream_When_ContinuationCompletesHeaderBlock` | Fact | RFC9113-6.10-CF-024: END_STREAM on HEADERS is carried through to reassembled response |
| 25 | `Should_WaitForDataFrame_When_HeadersLacksEndStream` | Fact | RFC9113-6.10-CF-025: Without END_STREAM on HEADERS, response awaits DATA frame |

#### `10_DecoderBasicFrameTests.cs` - `Http2FrameDecoderBasicTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Decode_SettingsFrame_ExtractsParameters` | Fact | 9113-6.5-001: SETTINGS frame parameters are decoded correctly |
| 2 | `Decode_SettingsAck_IsAckTrue` | Fact | 9113-6.5-002: SETTINGS ACK flag is preserved |
| 3 | `Decode_PingRequest_ReturnsCorrectData` | Fact | 9113-6.7-001: PING request frame data is decoded correctly |
| 4 | `Decode_PingAck_IsAckTrue` | Fact | 9113-6.7-002: PING ACK flag is preserved |
| 5 | `Decode_WindowUpdate_ReturnsIncrement` | Fact | 9113-6.9-001: WINDOW_UPDATE frame increment is decoded correctly |
| 6 | `Decode_RstStream_ReturnsErrorCode` | Fact | 9113-6.4-001: RST_STREAM error code is decoded correctly |
| 7 | `Decode_GoAway_ParsedCorrectly` | Fact | 9113-6.8-001: GOAWAY last-stream-id and error code are decoded correctly |
| 8 | `Decode_FrameFragmented_ReassembledCorrectly` | Fact | 9113-4.1-001: Frame split across two TCP segments is reassembled |
| 9 | `Decode_MultipleFrames_AllProcessed` | Fact | 9113-4.1-002: Multiple frames in one TCP segment are all decoded |
| 10 | `Decode_HeadersAndData_CorrectFrameObjects` | Fact | 9113-6.1-001: HEADERS and DATA frames are decoded with correct flags |
| 11 | `Decode_HeadersWithEndStream_FlagsCorrect` | Fact | 9113-6.2-001: HEADERS with END_STREAM flag is decoded correctly |
| 12 | `Decode_ContinuationFrames_DecodedSeparately` | Fact | 9113-6.10-001: HEADERS + CONTINUATION frames are decoded as separate frame objects |
| 13 | `Decode_AfterReset_PartialBufferCleared` | Fact | 9113-4.1-003: Reset clears buffered partial data |


---

## TurboHttp.StreamTests (Stream Tests) - 127 tests

### Client (15 tests)

#### `TurboHttpClientSendAsyncTests.cs` - `FakePool`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `CLI_001_SingleRequest_ReturnsResponse` | Fact | CLI-001: Single request → single response returned |
| 2 | `CLI_002_BaseAddress_Applied_UriIsAbsoluteAtPool` | Fact | CLI-002: BaseAddress applied before request enters pipeline — URI is absolute at pool |
| 3 | `CLI_003_DefaultRequestVersion_Applied` | Fact | CLI-003: DefaultRequestVersion applied → captured request has correct version |
| 4 | `CLI_004_DefaultRequestHeaders_Merged` | Fact | CLI-004: DefaultRequestHeaders merged → header present in captured request |
| 5 | `CLI_005_ExplicitHeaders_NotOverridden` | Fact | CLI-005: Explicit headers on request not overridden by DefaultRequestHeaders |
| 6 | `CLI_006_Timeout_ThrowsTimeoutException` | Fact | CLI-006: Timeout expires before response → TimeoutException thrown |
| 7 | `CLI_007_CancellationToken_Cancelled_ThrowsTaskCanceledException` | Fact | CLI-007: CancellationToken cancelled → TaskCanceledException thrown |
| 8 | `CLI_008_FiveSequentialRequests_AllComplete` | Fact | CLI-008: 5 sequential requests all complete in order |
| 9 | `CLI_009_TenConcurrentRequests_AllComplete` | Fact | CLI-009: 10 concurrent requests all complete |
| 10 | `CLI_010_CancelPendingRequests_InFlightTasksCancelled` | Fact | CLI-010: CancelPendingRequests → all in-flight SendAsync tasks throw OperationCanceledException |
| 11 | `CLI_011_AfterCancelPendingRequests_NewSendAsyncWorks` | Fact | CLI-011: After CancelPendingRequests(), new SendAsync works normally |

#### `TurboClientStreamManagerTests.cs` - `FakePool`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `MGR_001_ManagerCreatesSuccessfully` | Fact | MGR-001: Manager creates without throwing; Requests and Responses are non-null |
| 2 | `MGR_002_RequestEnrichedWithBaseAddress_WhenWrittenToChannel` | Fact | MGR-002: Writing a request with relative URI → enriched to absolute URI at pool |
| 3 | `MGR_003_ResponseCallback_WritesToResponsesChannel` | Fact | MGR-003: Response callback → response appears on Responses channel |
| 4 | `MGR_004_BoundedChannel_TryWriteReturnsFalseWhenFull` | Fact | MGR-004: Bounded channel returns false on TryWrite when full (backpressure semantics) |

### Http10 (22 tests)

#### `Http10EngineTests.cs` - `Http10EngineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_10_ENG_001_SimpleGet_Returns200_WithVersion10` | Fact | RFC-1945-§6.1: ST-10-ENG-001: Simple GET returns 200 with HTTP/1.0 version |
| 2 | `ST_10_ENG_002_Post_WithBody_Returns200` | Fact | RFC-1945-§8.3: ST-10-ENG-002: POST with body returns 200 |
| 3 | `ST_10_ENG_003_ResponseBody_ReadableViaContent` | Fact | RFC-1945-§7.2: ST-10-ENG-003: Response body readable via Content.ReadAsByteArrayAsync |
| 4 | `ST_10_ENG_004_NotFound_DecodedCorrectly` | Fact | RFC-1945-§6.1: ST-10-ENG-004: 404 response decoded to HttpStatusCode.NotFound |
| 5 | `ST_10_ENG_005_ThreeSequentialRequests_AllReturn200` | Fact | RFC-1945-§5: ST-10-ENG-005: Three sequential requests each return 200 |
| 6 | `ST_10_ENG_006_CustomHeader_PassesThroughToWire` | Fact | RFC-1945-§7.1: ST-10-ENG-006: Request with custom header passes through to wire |

#### `Http10WireComplianceTests.cs` - `Http10WireComplianceTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_10_WIRE_001_RequestLine_ExactBytes` | Fact | RFC-1945-§5.1: ST-10-WIRE-001: GET /path HTTP/1.0 CRLF exact bytes |
| 2 | `ST_10_WIRE_002_NoHeaderFolding` | Fact | RFC-1945-§7.1: ST-10-WIRE-002: Header folding absent — each header on its own line |
| 3 | `ST_10_WIRE_003_QueryString_PreservedInRequestUri` | Fact | RFC-1945-§5.1: ST-10-WIRE-003: Query string included in Request-URI |
| 4 | `ST_10_WIRE_004_RequestTarget_IsPathOnly_NotAbsoluteUri` | Fact | RFC-1945-§D.1: ST-10-WIRE-004: Wire target is path+query only, not scheme or host |
| 5 | `ST_10_WIRE_005_ContentLength_MatchesBodyByteCount` | Fact | RFC-1945-§7.2: ST-10-WIRE-005: Content-Length matches actual body byte count |

#### `Http10DecoderStageTests.cs` - `Http10DecoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_10_DEC_001_StatusLine_Decoded` | Fact | RFC-1945-§6.1: Status-Line decoded to StatusCode and Version |
| 2 | `ST_10_DEC_002_ResponseHeader_Decoded` | Fact | RFC-1945-§7.1: Response header decoded to response.Headers |
| 3 | `ST_10_DEC_003_ContentLength_Body_Decoded` | Fact | RFC-1945-§7.2: Body delimited by Content-Length decoded correctly |
| 4 | `ST_10_DEC_004_NotFound_StatusCode` | Fact | RFC-1945-§6.1: 404 response decoded to HttpStatusCode.NotFound |
| 5 | `ST_10_DEC_005_Fragmented_Reassembled` | Fact | RFC-1945-§7.2: Response split across two TCP chunks reassembled |

#### `Http10EncoderStageTests.cs` - `Http10EncoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_10_ENC_001_RequestLine_Format` | Fact | RFC-1945-§5.1: Request-Line is METHOD SP path SP HTTP/1.0 CRLF |
| 2 | `ST_10_ENC_002_CustomHeader_Forwarded` | Fact | RFC-1945-§7.1: Custom header is forwarded verbatim |
| 3 | `ST_10_ENC_003_NoHostHeader` | Fact | RFC-1945-§D.1: No Host header emitted |
| 4 | `ST_10_ENC_004_ConnectionHeader_Suppressed` | Fact | RFC-1945-§7.1: No Connection header emitted even when set on request |
| 5 | `ST_10_ENC_005_PostBody_FollowsHeaders` | Fact | RFC-1945-§D.1: POST body bytes follow headers after double-CRLF |
| 6 | `ST_10_ENC_006_ContentLength_PresentForPostBody` | Fact | RFC-1945-§D.1: Content-Length header present for POST body |

### Http11 (28 tests)

#### `Http11ResponseCorrelationTests.cs` - `Http11ResponseCorrelationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Single_Request_Response_Correlation` | Fact | REQ-001: Single request/response pair — response.RequestMessage set to the originating request |
| 2 | `Five_Sequential_Requests_InOrder_Correlation` | Fact | REQ-002: 5 sequential requests — each response.RequestMessage matches correct in-order request |
| 3 | `RequestMessage_Is_Same_Reference` | Fact | REQ-003: response.RequestMessage is the exact same object instance (reference equality) |
| 4 | `Http11Engine_FakeTcp_CorrelationPreserved` | Fact | REQ-004: Http11Engine flow with fake TCP — correlation preserved end-to-end |

#### `Http11WireComplianceTests.cs` - `Http11WireComplianceTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_11_WIRE_001_RequestLine_ExactBytes` | Fact | RFC-9112-§3.1: ST-11-WIRE-001: GET /path HTTP/1.1 CRLF exact bytes |
| 2 | `ST_11_WIRE_002_HostHeader_PresentAndCorrect` | Fact | RFC-9112-§7.2: ST-11-WIRE-002: Host header is present and correct |
| 3 | `ST_11_WIRE_003_KeepAlive_HopByHop_NotForwarded` | Fact | RFC-9112-§7.6.1: ST-11-WIRE-003: Hop-by-hop Keep-Alive header absent on outbound |
| 4 | `ST_11_WIRE_004_ChunkedEncoding_FirstChunkHeader_Format` | Fact | RFC-9112-§6.1: ST-11-WIRE-004: Chunked encoding first chunk header is hex-size CRLF |
| 5 | `ST_11_WIRE_005_HeaderSection_EndsWith_DoubleCrlf` | Fact | RFC-9112-§2.1: ST-11-WIRE-005: Header section ends with double CRLF before body |
| 6 | `ST_11_WIRE_006_Method_PreservedVerbatim` | Theory | RFC-9112-§3.1: ST-11-WIRE-006: Method preserved verbatim on outbound wire |

#### `Http11EngineTests.cs` - `Http11EngineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Simple_GET_Returns_200` | Fact | RFC-9112-§4: ST-11-001: Simple GET returns 200 with HTTP/1.1 version |
| 2 | `Simple_GET_Encodes_Request_Line` | Fact | RFC-9112-§3.1: ST-11-002: Simple GET encodes request line |
| 3 | `GET_Contains_Host_Header` | Fact | RFC-9112-§7.2: ST-11-003: GET contains mandatory Host header |
| 4 | `POST_With_Body_Uses_Chunked_Or_Content_Length` | Fact | RFC-9112-§6.1: ST-11-004: POST with body uses chunked or Content-Length framing |
| 5 | `Response_With_Body_Is_Decoded` | Fact | RFC-9112-§6.1: ST-11-005: Response with Content-Length body is decoded |
| 6 | `Custom_Header_Is_Forwarded` | Fact | RFC-9112-§5: ST-11-006: Custom request header is forwarded verbatim |
| 7 | `Multiple_Pipelined_Requests_All_Return_200` | Fact | RFC-9112-§9.3: ST-11-007: Multiple pipelined requests all return 200 |

#### `Http11DecoderStageTests.cs` - `Http11DecoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_11_DEC_001_StatusLine_Decoded` | Fact | RFC-9112-§4: Status-Line decoded to StatusCode and Version11 |
| 2 | `ST_11_DEC_002_ContentLength_Body_Decoded` | Fact | RFC-9112-§6.1: Content-Length body decoded correctly |
| 3 | `ST_11_DEC_003_ChunkedBody_Decoded` | Fact | RFC-9112-§7.1: Chunked body decoded correctly |
| 4 | `ST_11_DEC_004_Pipelined_Responses_Decoded` | Fact | RFC-9112-§4: Two pipelined responses decoded as two messages |
| 5 | `ST_11_DEC_005_ResponseHeader_Decoded` | Fact | RFC-9112-§4: Response header decoded to response.Headers |
| 6 | `ST_11_DEC_006_Fragmented_ThreeChunks_Reassembled` | Fact | RFC-9112-§6.1: Response split across three TCP chunks reassembled |

#### `Http11EncoderStageTests.cs` - `Http11EncoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_11_ENC_001_RequestLine_Format` | Fact | RFC-9112-§3.1: Request-Line is METHOD SP path SP HTTP/1.1 CRLF |
| 2 | `ST_11_ENC_002_HostHeader_Emitted` | Fact | RFC-9112-§7.2: Host header is emitted for HTTP/1.1 requests |
| 3 | `ST_11_ENC_003_PostBody_HasFramingHeader` | Fact | RFC-9112-§6.1: POST with known body has Content-Length or Transfer-Encoding: chunked |
| 4 | `ST_11_ENC_004_HopByHop_Headers_Stripped` | Fact | RFC-9112-§7.6.1: Hop-by-hop connection-specific headers are stripped from wire |
| 5 | `ST_11_ENC_005_CustomHeader_Forwarded` | Fact | RFC-9112-§3.1: Custom request header forwarded verbatim |

### Http20 (32 tests)

#### `Http20WireComplianceTests.cs` - `Http2WireComplianceTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_20_WIRE_001_Connection_Preface_Magic_First24Bytes` | Fact | RFC-9113-§3.5: ST-20-WIRE-001: First 24 bytes equal connection preface magic verbatim |
| 2 | `ST_20_WIRE_002_Settings_Frame_Follows_Preface_At_Offset_24` | Fact | RFC-9113-§3.5: ST-20-WIRE-002: SETTINGS frame immediately follows preface at byte offset 24 |
| 3 | `ST_20_WIRE_003_Hpack_PseudoHeaders_All_Present` | Fact | RFC-9113-§8.3.1: ST-20-WIRE-003: HPACK block contains :method :path :scheme :authority |
| 4 | `ST_20_WIRE_004_All_Frame_Length_Fields_Consistent` | Fact | RFC-9113-§4.1: ST-20-WIRE-004: All frame length fields consistent with actual payload sizes |
| 5 | `ST_20_WIRE_005_First_Request_StreamId_Is_1` | Fact | RFC-9113-§5.1.1: ST-20-WIRE-005: First request stream ID is 1 |
| 6 | `ST_20_WIRE_006_Settings_Ack_Flags_Byte_Is_0x01` | Fact | RFC-9113-§6.5: ST-20-WIRE-006: SETTINGS ACK flags byte is 0x01 |

#### `PrependPrefaceStageTests.cs` - `PrependPrefaceStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_20_PRE_001_Preface_Magic_First24Bytes` | Fact | RFC-9113-§3.5: First 24 bytes are exactly the connection preface magic |
| 2 | `ST_20_PRE_002_Preface_Settings_FrameHeader` | Fact | RFC-9113-§3.5: Bytes 24..32 are a SETTINGS frame header (type=0x4, stream=0) |
| 3 | `ST_20_PRE_003_PassThrough_After_Preface` | Fact | RFC-9113-§3.5: Second element passed through unchanged after preface emitted |
| 4 | `ST_20_PRE_004_Preface_Emitted_Once` | Fact | RFC-9113-§3.5: Preface emitted exactly once (not repeated for second demand) |

#### `Request2FrameStageTests.cs` - `Request2FrameStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_20_REQ_001_Headers_Frame_Contains_Method_PseudoHeader` | Fact | RFC-9113-§8.3.1: Emits HEADERS frame with :method pseudo-header |
| 2 | `ST_20_REQ_002_Headers_Frame_Contains_All_Four_Pseudo_Headers` | Fact | RFC-9113-§8.3.1: Emits :path, :scheme, :authority pseudo-headers |
| 3 | `ST_20_REQ_003_StreamIds_Are_Odd_And_Ascending` | Fact | RFC-9113-§8.1: Stream IDs are odd and strictly ascending (1, 3, 5…) |
| 4 | `ST_20_REQ_004_Post_Request_Emits_Headers_Then_Data_Frame` | Fact | RFC-9113-§8.1: POST request emits HEADERS then DATA frame |
| 5 | `ST_20_REQ_005_Get_Request_Has_EndStream_On_Headers_Frame` | Fact | RFC-9113-§8.3.1: GET request has END_STREAM flag set on HEADERS frame |

#### `Http20DecoderStageTests.cs` - `Http20DecoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_20_FDEC_001_Single_Complete_Frame_Decoded` | Fact | RFC-9113-§4.1: Single complete frame decoded correctly |
| 2 | `ST_20_FDEC_002_Frame_Split_Across_Two_Chunks_Reassembled` | Fact | RFC-9113-§4.1: Frame split across two TCP chunks reassembled |
| 3 | `ST_20_FDEC_003_Two_Frames_In_One_Chunk_Decoded` | Fact | RFC-9113-§4.1: Two frames in one TCP chunk each decoded |
| 4 | `ST_20_FDEC_004_Settings_Frame_Decoded` | Fact | RFC-9113-§4.1: SETTINGS frame (stream 0) decoded |
| 5 | `ST_20_FDEC_005_Data_Frame_Decoded_With_StreamId_And_Payload` | Fact | RFC-9113-§4.1: DATA frame decoded with correct stream ID and payload |

#### `Http20EncoderStageTests.cs` - `Http20EncoderStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ST_20_FENC_001_Headers_Frame_Has_9Byte_Header_And_Payload` | Fact | RFC-9113-§4.1: HEADERS frame has 9-byte header + HPACK payload |
| 2 | `ST_20_FENC_002_Data_Frame_Has_9Byte_Header_And_Body` | Fact | RFC-9113-§4.1: DATA frame has 9-byte header + body payload |
| 3 | `ST_20_FENC_003_StreamId_Encoded_BigEndian_In_Bytes5To8` | Fact | RFC-9113-§4.1: Stream ID field is encoded big-endian in bytes 5–8 |
| 4 | `ST_20_FENC_004_Payload_Length_Field_Matches_Actual_Payload_Size` | Fact | RFC-9113-§4.2: Payload length field matches actual payload size |

#### `Http20EngineTests.cs` - `Http20EngineTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Simple_GET_Returns_200` | Fact | RFC-9113-§8.1: ST-20-001: Simple GET returns 200 |
| 2 | `Request_Encodes_HPACK_Pseudo_Headers` | Fact | RFC-9113-§8.3: ST-20-002: Request encodes HPACK pseudo-headers |
| 3 | `POST_With_Body_Sends_DATA_Frame_After_HEADERS` | Fact | RFC-9113-§8.1: ST-20-003: POST with body sends DATA frame after HEADERS |
| 4 | `Response_With_Body_Is_Decoded` | Fact | RFC-9113-§8.1: ST-20-004: Response with body is decoded |
| 5 | `Gzip_Response_Is_Decompressed` | Fact | RFC-9110-§8.4: ST-20-005: Content-Encoding gzip response is decompressed |
| 6 | `Multiple_Streams_Processed_In_Order` | Fact | RFC-9113-§5.1.1: ST-20-006: Multiple concurrent streams processed in order |
| 7 | `Server_Settings_Is_Acked` | Fact | RFC-9113-§6.5: ST-20-007: SETTINGS frame from server is ACKed |
| 8 | `Connection_Preface_Is_Sent_First` | Fact | RFC-9113-§3.4: ST-20-008: Connection preface is sent first |

### Pool (8 tests)

#### `HostConnectionPoolTests.cs` - `RequestCounts`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Http10_Request_Through_Pool_Returns_Correct_Status_And_Version` | Fact | ST-POOL-001: HTTP/1.0 request through pool returns correct status and version |
| 2 | `Http11_Request_Through_Pool_Returns_Correct_Status_And_Version` | Fact | ST-POOL-002: HTTP/1.1 request through pool returns correct status and version |
| 3 | `Http20_Request_Through_Pool_Returns_Correct_Status_And_Version` | Fact | ST-POOL-003: HTTP/2.0 request through pool returns correct status and version |
| 4 | `Mixed_Version_Batch_Via_Pool_Each_Response_Version_Matches_Request` | Fact | ST-POOL-004: Mixed-version batch via pool: each response version matches request |
| 5 | `Http10_Bytes_Only_Reach_Http10_Fake_Connection` | Fact | ST-POOL-005: HTTP/1.0 bytes only reach HTTP/1.0 fake connection |
| 6 | `Http11_Bytes_Only_Reach_Http11_Fake_Connection` | Fact | ST-POOL-006: HTTP/1.1 bytes only reach HTTP/1.1 fake connection |
| 7 | `Http20_Bytes_Only_Reach_Http20_Fake_Connection` | Fact | ST-POOL-007: HTTP/2.0 bytes only reach HTTP/2.0 fake connection |
| 8 | `Backpressure_Queue_Of_256_Requests_Does_Not_Deadlock` | Fact | ST-POOL-008: Backpressure: queue of 256 requests does not deadlock |

### Streams (22 tests)

#### `RequestEnricherStageTests.cs` - `RequestEnricherStageTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ENR_001_NullUri_WithBaseAddress_BecomesBaseAddress` | Fact | ENR-001: Null URI + BaseAddress → RequestUri becomes BaseAddress root |
| 2 | `ENR_002_RelativeUri_WithBaseAddress_Combined` | Fact | ENR-002: Relative URI \ |
| 3 | `ENR_003_AbsoluteUri_NotChanged` | Fact | ENR-003: Absolute URI → RequestUri unchanged even when BaseAddress is set |
| 4 | `ENR_004_NullUri_NullBaseAddress_Fails` | Fact | ENR-004: Null URI, null BaseAddress → stage fails with InvalidOperationException |
| 5 | `ENR_005_RelativeUri_NullBaseAddress_Fails` | Fact | ENR-005: Relative URI, null BaseAddress → stage fails with InvalidOperationException |
| 6 | `ENR_006_DefaultVersion_11_DefaultIs20_BecomesV20` | Fact | ENR-006: request.Version == 1.1 (default), defaultVersion == 2.0 → version becomes 2.0 |
| 7 | `ENR_007_DefaultVersion_11_DefaultIs11_Unchanged` | Fact | ENR-007: request.Version == 1.1 (default), defaultVersion == 1.1 → version unchanged |
| 8 | `ENR_008_ExplicitV10_NotOverridden` | Fact | ENR-008: request.Version explicitly set to 1.0 → unchanged regardless of defaultVersion |
| 9 | `ENR_009_ExplicitV20_NotOverridden` | Fact | ENR-009: request.Version explicitly set to 2.0 → unchanged regardless of defaultVersion |
| 10 | `ENR_010_DefaultHeader_Merged` | Fact | ENR-010: DefaultRequestHeaders has X-Foo:bar → merged into request |
| 11 | `ENR_011_RequestHeaderNotOverridden` | Fact | ENR-011: Request already has X-Foo:existing → not overridden; existing value kept |
| 12 | `ENR_012_TwoDefaultHeaders_BothMerged` | Fact | ENR-012: DefaultRequestHeaders has two headers → both merged |
| 13 | `ENR_013_EmptyDefaults_NoHeadersAdded` | Fact | ENR-013: DefaultRequestHeaders empty → no headers added; request unchanged |
| 14 | `ENR_014_HeaderCaseInsensitive_NotDoubled` | Fact | ENR-014: Same header name, different casing in request vs defaults → treated as same; not doubled |
| 15 | `ENR_015_MultipleValuesForOneName_AllAdded` | Fact | ENR-015: DefaultRequestHeaders has multiple values for one name → all values added as one entry |
| 16 | `ENR_016_ThreeRequests_AllEnriched_OrderPreserved` | Fact | ENR-016: 3 requests in sequence → all 3 enriched independently, order preserved |

#### `HostRoutingStageOptionsTests.cs` - `FakePool`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `HRS_001_HttpUri_CreatesTcpOptions` | Fact | HRS-001: http URI → pool created with TcpOptions (not TlsOptions) |
| 2 | `HRS_002_HttpsUri_CreatesTlsOptions` | Fact | HRS-002: https URI → pool created with TlsOptions |
| 3 | `HRS_003_ClientOptionsConnectTimeoutPropagated` | Fact | HRS-003: clientOptions.ConnectTimeout=20s → resulting TcpOptions.ConnectTimeout == 20s |
| 4 | `HRS_004_SameHostPortScheme_ReusesPool` | Fact | HRS-004: Two requests to same host:port:scheme → same pool reused (no second creation) |
| 5 | `HRS_005_DifferentHosts_CreatesSeparatePools` | Fact | HRS-005: Two requests to different host → two separate pools |
| 6 | `HRS_006_SameHostDifferentScheme_CreatesSeparatePools` | Fact | HRS-006: http://a.test and https://a.test → two separate pools (different scheme) |


---

## TurboHttp.IntegrationTests (Integration Tests) - 379 tests

### Client (10 tests)

#### `TurboHttpClientIntegrationTests.cs` - `TurboHttpClientIntegrationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `ITG_001_Get_Ping_Returns_Pong` | Fact | ITG-001: GET /ping → 200, body == \ |
| 2 | `ITG_002_BaseAddress_RelativeUri_ResolvesCorrectly` | Fact | ITG-002: BaseAddress set → relative URI \ |
| 3 | `ITG_003_DefaultRequestVersion_1_0_ResponseVersion` | Fact | ITG-003: DefaultRequestVersion = 1.0 → response.Version == 1.0 |
| 4 | `ITG_004_DefaultRequestHeaders_EchoedBack` | Fact | ITG-004: DefaultRequestHeaders[\ |
| 5 | `ITG_005_Post_Echo_BodyEchoed` | Fact | ITG-005: POST /echo with body → body echoed, Content-Length correct |
| 6 | `ITG_006_Get_Status_404` | Fact | ITG-006: GET /status/404 → 404 status code, no exception |
| 7 | `ITG_007_Get_Status_500` | Fact | ITG-007: GET /status/500 → 500 status code, no exception |
| 8 | `ITG_008_TenConcurrentGets_AllReturn200` | Fact | ITG-008: 10 concurrent GETs all return 200 |
| 9 | `ITG_009_HttpsUri_TlsHandshake` | Fact |  |
| 10 | `ITG_010_Timeout_SlowEndpoint_ThrowsTimeoutException` | Fact | ITG-010: Timeout = 100ms, GET /slow/500 → TimeoutException within ~200ms |

### Http10 (60 tests)

#### `Http10HeaderTests.cs` - `Http10HeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_HeadersEcho_SingleXHeader_IsEchoedInResponse` | Fact | IT-10-040: GET /headers/echo with X-Test header — echoed in response |
| 2 | `Get_HeadersEcho_MultipleXHeaders_AllEchoedInResponse` | Fact | IT-10-041: GET /headers/echo with multiple X-* headers — all echoed |
| 3 | `Get_HeadersEcho_AsciiHeaderValue_IsPreserved` | Fact | IT-10-042: GET /headers/echo header value with ASCII printable chars is preserved |
| 4 | `Get_Auth_WithoutAuthorization_Returns401` | Fact | IT-10-043: GET /auth without Authorization header returns 401 |
| 5 | `Get_Auth_WithAuthorization_Returns200` | Fact | IT-10-044: GET /auth with valid Authorization header returns 200 |
| 6 | `Get_Hello_HasServerHeader` | Fact | IT-10-045: GET /hello response has Server header present |
| 7 | `Get_Hello_DateHeader_HasValidFormat` | Fact | IT-10-046: GET /hello response has Date header with a parseable value |
| 8 | `Get_Hello_ContentType_IsTextPlain` | Fact | IT-10-047: GET /hello response Content-Type is text/plain |
| 9 | `Get_HeadersSet_SetsCustomResponseHeader` | Fact | IT-10-048: GET /headers/set?Foo=Bar sets Foo: Bar in response |
| 10 | `Get_HeadersSet_SetsMultipleCustomResponseHeaders` | Fact | IT-10-049: GET /headers/set?A=1&B=2 sets both A and B response headers |
| 11 | `Get_MultiHeader_TwoXValueEntries_BothAccessible` | Fact | IT-10-050: GET /multiheader response has two X-Value entries, both accessible |
| 12 | `Get_Hello_ContentLength_CaseInsensitiveAccess` | Fact | IT-10-051: Response header Content-Length accessible regardless of case |
| 13 | `Get_Hello_ContentLength_MatchesActualBodyLength` | Fact | IT-10-052: GET /hello Content-Length matches actual body bytes returned |

#### `Http10StatusCodeTests.cs` - `Http10StatusCodeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Status200_IsDecodedCorrectly` | Fact | IT-10-060: GET /status/200 decoded status is 200 OK |
| 2 | `Status201_IsDecodedCorrectly` | Fact | IT-10-061: GET /status/201 decoded status is 201 Created |
| 3 | `Status204_IsDecodedCorrectly_EmptyBody` | Fact | IT-10-062: GET /status/204 decoded status is 204 No Content with empty body |
| 4 | `Status206_IsDecodedCorrectly` | Fact | IT-10-063: GET /status/206 decoded status is 206 Partial Content |
| 5 | `Status301_IsDecodedCorrectly` | Fact | IT-10-064: GET /status/301 decoded status is 301 Moved Permanently |
| 6 | `Status302_IsDecodedCorrectly` | Fact | IT-10-065: GET /status/302 decoded status is 302 Found |
| 7 | `Status400_IsDecodedCorrectly` | Fact | IT-10-066: GET /status/400 decoded status is 400 Bad Request |
| 8 | `Status401_IsDecodedCorrectly` | Fact | IT-10-067: GET /status/401 decoded status is 401 Unauthorized |
| 9 | `Status403_IsDecodedCorrectly` | Fact | IT-10-068: GET /status/403 decoded status is 403 Forbidden |
| 10 | `Status404_IsDecodedCorrectly` | Fact | IT-10-069: GET /status/404 decoded status is 404 Not Found |
| 11 | `Status405_IsDecodedCorrectly` | Fact | IT-10-070: GET /status/405 decoded status is 405 Method Not Allowed |
| 12 | `Status408_IsDecodedCorrectly` | Fact | IT-10-071: GET /status/408 decoded status is 408 Request Timeout |
| 13 | `Status500_IsDecodedCorrectly` | Fact | IT-10-072: GET /status/500 decoded status is 500 Internal Server Error |
| 14 | `Status502_IsDecodedCorrectly` | Fact | IT-10-073: GET /status/502 decoded status is 502 Bad Gateway |
| 15 | `Status503_IsDecodedCorrectly` | Fact | IT-10-074: GET /status/503 decoded status is 503 Service Unavailable |

#### `Http10ConnectionTests.cs` - `Http10ConnectionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Connection_ClosesAfterResponse` | Fact | IT-10-080: Connection closes after HTTP/1.0 response — second read returns 0 |
| 2 | `FiveSequentialRequests_SeparateConnections_AllSucceed` | Fact | IT-10-081: Five sequential GET /ping requests on separate connections all succeed |
| 3 | `TryDecodeEof_SucceedsOnServerClose_HeadResponse` | Fact | IT-10-082: TryDecodeEof succeeds when server closes connection — HEAD response |
| 4 | `PartialResponse_TryDecode_ReturnsFalse_UntilComplete` | Fact | IT-10-083: Partial response bytes cause TryDecode to return false until complete |
| 5 | `Response_DecodedSuccessfully_WhenServerSendsConnectionClose` | Fact | IT-10-084: Response decoded successfully when server sends Connection: close |
| 6 | `TwoConcurrent_GetPing_SeparateConnections_BothSucceed` | Fact | IT-10-085: Two concurrent GET /ping requests on separate connections both succeed |
| 7 | `Decoder_Reset_ClearsRemainder` | Fact | IT-10-086: Http10Decoder.Reset clears remainder so next TryDecode starts fresh |

#### `Http10BasicTests.cs` - `Http10BasicTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_Hello_Returns200_WithBodyHelloWorld` | Fact | IT-10-001: GET /hello returns 200 with body 'Hello World' |
| 2 | `Get_Hello_HasDateHeader_AndCorrectContentLength` | Fact | IT-10-002: GET /hello response has Date header and correct Content-Length |
| 3 | `Get_Large_1KB_Returns200_With1KbBody` | Fact | IT-10-003: GET /large/1 returns 200 with 1 KB body |
| 4 | `Get_Large_64KB_Returns200_With64KbBody` | Fact | IT-10-004: GET /large/64 returns 200 with 64 KB body |
| 5 | `Get_Status_ReturnsExpectedStatusCode` | Theory | IT-10-005: GET /status/{code} returns the expected status code |
| 6 | `Get_Status204_ReturnsNoContent` | Fact | IT-10-006: GET /status/204 returns 204 with empty body |
| 7 | `Get_Status301_ReturnsMovedPermanently` | Fact | IT-10-007: GET /status/301 returns 301 redirect status |
| 8 | `Get_Ping_Returns200_WithBodyPong` | Fact | IT-10-008: GET /ping returns 200 with body 'pong' |
| 9 | `Get_Content_TextHtml_HasCorrectContentType` | Fact | IT-10-009: GET /content/text/html response has Content-Type text/html |
| 10 | `Get_Content_ApplicationJson_HasCorrectContentType` | Fact | IT-10-010: GET /content/application/json response has Content-Type application/json |
| 11 | `Head_Hello_Returns200_NoBody_WithContentLength` | Fact | IT-10-011: HEAD /hello returns 200 with no body and Content-Length present |
| 12 | `Get_Methods_Returns_GET` | Fact | IT-10-012: GET /methods returns body equal to 'GET' |
| 13 | `TwoSequential_Get_Ping_BothSucceed` | Fact | IT-10-013: Two sequential GET /ping requests each succeed independently |
| 14 | `Get_Large_ContentLength_MatchesActualBodyLength` | Theory | IT-10-014: GET /large/{kb} Content-Length header matches actual body byte count |

#### `Http10BodyTests.cs` - `Http10BodyTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Post_Echo_SmallBody_IsEchoedCorrectly` | Fact | IT-10-020: POST /echo small body is echoed correctly |
| 2 | `Post_Echo_1KbBody_IsEchoedCorrectly` | Fact | IT-10-021: POST /echo 1 KB body is echoed correctly |
| 3 | `Post_Echo_64KbBody_IsEchoedCorrectly` | Fact | IT-10-022: POST /echo 64 KB body is echoed correctly |
| 4 | `Post_Echo_EmptyBody_Returns200_EmptyBody` | Fact | IT-10-023: POST /echo empty body returns 200 with empty body |
| 5 | `Post_Echo_BinaryBody_0x00To0xFF_ByteAccurateRoundTrip` | Fact | IT-10-024: POST /echo binary body 0x00-0xFF is byte-accurate round-trip |
| 6 | `Post_Echo_BodyWithCrlf_IsPreserved` | Fact | IT-10-025: POST /echo body containing CRLF is preserved |
| 7 | `Post_Echo_BodyWithNullBytes_IsNotTruncated` | Fact | IT-10-026: POST /echo body with null bytes is not truncated |
| 8 | `Post_Echo_ContentLength_MatchesActualBodyLength` | Theory | IT-10-027: POST /echo Content-Length header matches actual body byte count |
| 9 | `Post_Echo_ContentType_TextPlain_IsMirrored` | Fact | IT-10-028: POST /echo Content-Type text/plain is mirrored in response |
| 10 | `Post_Echo_ContentType_Json_IsMirrored` | Fact | IT-10-029: POST /echo Content-Type application/json is mirrored in response |
| 11 | `Post_Echo_AllZeroesBody_IsPreserved` | Fact | IT-10-030: POST /echo all-zeroes body is preserved verbatim |

### Http11 (176 tests)

#### `Http11KeepAliveTests.cs` - `Http11KeepAliveTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `TwoSequentialRequests_SameConnection_Succeed` | Fact | IT-11-030: 2 sequential requests on same connection succeed |
| 2 | `FiveSequentialRequests_SameConnection_AllSucceed` | Fact | IT-11-031: 5 sequential requests on same connection all succeed |
| 3 | `TenSequentialRequests_SameConnection_AllSucceed` | Fact | IT-11-032: 10 sequential requests on same connection all succeed |
| 4 | `ServerConnectionClose_IsFlaggedOnConnection` | Fact | IT-11-033: Server Connection:close response is flagged on connection |
| 5 | `Request_WithConnectionClose_ResponseReceived` | Fact | IT-11-034: Request with Connection:close sends close directive to server |
| 6 | `Mixed_KeepAlive_ThenClose_BothResponsesDecoded` | Fact | IT-11-035: Mixed keep-alive then close — both responses decoded correctly |
| 7 | `Decoder_ResetsCleanly_BetweenRequests` | Fact | IT-11-036: Decoder resets cleanly between requests on same connection |
| 8 | `KeepAlive_VaryingBodySizes_AllDecodedCorrectly` | Fact | IT-11-037: Keep-alive with varying body sizes — decoder handles each correctly |
| 9 | `KeepAlive_Get_Post_Get_SameConnection` | Fact | IT-11-038: Keep-alive GET then POST then GET on same connection |
| 10 | `Pipeline_Depth2_ResponsesInOrder` | Fact | IT-11-039: Pipeline depth 2 — two requests in flight, responses in order |
| 11 | `Pipeline_Depth5_AllResponsesReceived` | Fact | IT-11-040: Pipeline depth 5 — five requests in flight, all responses received |
| 12 | `Pipeline_MixedVerbs_ResponsesInOrder` | Fact | IT-11-041: Pipeline with mixed GET+POST verbs — responses arrive in order |
| 13 | `Pipeline_ResponsesInRequestOrder_VerifiedByBody` | Fact | IT-11-042: Pipelined responses arrive in request order — verified by body content |
| 14 | `TwentySequential_GetPing_SameConnection_AllSucceed` | Fact | IT-11-043: 20 sequential GET /ping on same keep-alive connection all succeed |

#### `Http11HeaderTests.cs` - `Http11HeaderTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `TwentyCustomHeaders_RoundTrip_AllPresent` | Fact | IT-11-070: 20 custom X-* headers round-trip via /headers/echo |
| 2 | `Get_MultiHeader_DuplicateHeaderNames_ListAppendSemantics` | Fact | IT-11-071: GET /multiheader — duplicate X-Value headers decoded with list semantics |
| 3 | `Post_ContentType_WithCharset_RoundTrips` | Fact | IT-11-072: POST /echo — Content-Type with charset parameter round-trips correctly |
| 4 | `Request_MultiValueAccept_Returns200` | Fact | IT-11-073: Request with multi-value Accept header is sent and response is 200 |
| 5 | `AuthorizationHeader_Preserved_Returns200` | Fact | IT-11-074: Authorization header causes /auth to return 200 |
| 6 | `NoAuthorizationHeader_Returns401` | Fact | IT-11-075: Missing Authorization header causes /auth to return 401 |
| 7 | `CookieHeader_Preserved_EchoedBack` | Fact | IT-11-076: Cookie header is sent and echoed back via /headers/echo |
| 8 | `Response_DateHeader_ParseableAsRfc7231Date` | Fact | IT-11-077: Response Date header is present and parseable as RFC 7231 date |
| 9 | `Get_Etag_Returns200_WithEtagHeader` | Fact | IT-11-078: GET /etag returns 200 with ETag header |
| 10 | `Get_Etag_WithMatchingIfNoneMatch_Returns304_NoBody` | Fact | IT-11-079: GET /etag with matching If-None-Match returns 304 with no body |
| 11 | `Get_Etag_WithNonMatchingIfNoneMatch_Returns200_FullBody` | Fact | IT-11-080: GET /etag with non-matching If-None-Match returns 200 full body |
| 12 | `Get_Cache_ReturnsCachingHeaders` | Fact | IT-11-081: GET /cache returns Cache-Control and Last-Modified headers |
| 13 | `XCustomHeaders_EchoedCorrectly` | Fact | IT-11-082: X-* custom headers echoed correctly via /headers/echo |
| 14 | `VeryLongHeaderValue_4KB_RoundTrips` | Fact | IT-11-083: Very long header value (4 KB) round-trips via /headers/echo |
| 15 | `HeaderName_CaseFolding_EchoedCorrectly` | Fact | IT-11-084: Header names are case-insensitive — X-Mixed-Case echoed correctly |
| 16 | `ObsFold_RejectedByDecoder` | Fact | IT-11-085: Folded header value (obs-fold) is rejected by Http11Decoder |
| 17 | `IfModifiedSince_PastDate_Returns200` | Fact | IT-11-086: If-Modified-Since past date → 200 full response |
| 18 | `IfModifiedSince_FutureDate_Returns304` | Fact | IT-11-087: If-Modified-Since future date → 304 not modified |
| 19 | `Get_Cache_ResponseIncludesPragmaNoCache` | Fact | IT-11-088: GET /cache response includes Pragma: no-cache header |
| 20 | `Get_Cache_ResponseIncludesLastModifiedHeader` | Fact | IT-11-089: GET /cache response includes Last-Modified header |

#### `Http11RangeTests.cs` - `Http11RangeTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `NoRangeHeader_Returns200_FullBody` | Fact | IT-11A-016: No Range header — GET /range/1 returns 200 with full 1 KB body |
| 2 | `Range_Bytes0To99_Returns206_ContentRange` | Fact | IT-11A-017: Range: bytes=0-99 — returns 206 with 100 bytes and Content-Range header |
| 3 | `Range_Bytes0To0_Returns206_OneByte` | Fact | IT-11A-018: Range: bytes=0-0 — returns 206 with exactly 1 byte |
| 4 | `Range_SuffixRange100_Returns206_Last100Bytes` | Fact | IT-11A-019: Range: bytes=-100 — returns 206 with last 100 bytes |
| 5 | `Range_OpenEndedFrom100_Returns206_RestOfBody` | Fact | IT-11A-020: Range: bytes=100- — returns 206 from byte 100 to end of 1 KB body |
| 6 | `Range_SecondHalf_1KbBody_Returns206` | Fact | IT-11A-021: Range: bytes=512-1023 on 1 KB body — returns 206 with second half |
| 7 | `Range_First4KB_64KbBody_Returns206` | Fact | IT-11A-022: Range: bytes=0-4095 on 64 KB body — returns 206 with first 4 KB |
| 8 | `Range_SecondHalf_64KbBody_Returns206` | Fact | IT-11A-023: Range: bytes=32768-65535 on 64 KB body — returns 206 with second half |
| 9 | `Range_Unsatisfiable_Returns416` | Fact | IT-11A-024: Range: bytes=99999-99999 on 1 KB body — returns 416 Range Not Satisfiable |
| 10 | `IfRange_MatchingETag_Returns206` | Fact | IT-11A-025: If-Range with matching ETag — returns 206 partial content |
| 11 | `IfRange_NonMatchingETag_Returns200_FullBody` | Fact | IT-11A-026: If-Range with non-matching ETag — returns 200 with full body |
| 12 | `Range_MultiRange_Returns200Or206` | Fact | IT-11A-027: Range: bytes=0-49,50-99 (multi-range) — server returns 200 or 206 |
| 13 | `Range_206Response_ContentRange_IncludesTotalSize` | Fact | IT-11A-028: 206 response includes Content-Range with total resource size |
| 14 | `Range_BodyBytes_MatchSequentialPattern` | Fact | IT-11A-029: Range bytes=256-511 on 1 KB body — body bytes match sequential pattern |

#### `Http11StatusAndErrorTests.cs` - `Http11StatusAndErrorTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_Status200_Returns200OK` | Fact | IT-11-090: GET /status/200 returns 200 OK |
| 2 | `Get_Status201_Returns201Created` | Fact | IT-11-091: GET /status/201 returns 201 Created |
| 3 | `Get_Status202_Returns202Accepted` | Fact | IT-11-092: GET /status/202 returns 202 Accepted |
| 4 | `Get_Status204_Returns204NoContent_EmptyBody` | Fact | IT-11-093: GET /status/204 returns 204 No Content with empty body |
| 5 | `Get_Status206_Returns206PartialContent` | Fact | IT-11-094: GET /status/206 returns 206 Partial Content |
| 6 | `Get_Status301_ReturnsMovedPermanently` | Fact | IT-11-095: GET /status/301 returns 301 Moved Permanently |
| 7 | `Get_Status302_ReturnsFound` | Fact | IT-11-096: GET /status/302 returns 302 Found |
| 8 | `Get_Status303_ReturnsSeeOther` | Fact | IT-11-097: GET /status/303 returns 303 See Other |
| 9 | `Get_Status307_ReturnsTemporaryRedirect` | Fact | IT-11-098: GET /status/307 returns 307 Temporary Redirect |
| 10 | `Get_Status308_ReturnsPermanentRedirect` | Fact | IT-11-099: GET /status/308 returns 308 Permanent Redirect |
| 11 | `Get_Status304_ReturnsNotModified_EmptyBody` | Fact | IT-11-100: GET /status/304 returns 304 Not Modified with empty body |
| 12 | `Get_Status400_ReturnsBadRequest` | Fact | IT-11-101: GET /status/400 returns 400 Bad Request |
| 13 | `Get_Status401_ReturnsUnauthorized` | Fact | IT-11-102: GET /status/401 returns 401 Unauthorized |
| 14 | `Get_Status403_ReturnsForbidden` | Fact | IT-11-103: GET /status/403 returns 403 Forbidden |
| 15 | `Get_Status404_ReturnsNotFound` | Fact | IT-11-104: GET /status/404 returns 404 Not Found |
| 16 | `Get_Status405_ReturnsMethodNotAllowed` | Fact | IT-11-105: GET /status/405 returns 405 Method Not Allowed |
| 17 | `Get_Status408_ReturnsRequestTimeout` | Fact | IT-11-106: GET /status/408 returns 408 Request Timeout |
| 18 | `Get_Status409_ReturnsConflict` | Fact | IT-11-107: GET /status/409 returns 409 Conflict |
| 19 | `Get_Status410_ReturnsGone` | Fact | IT-11-108: GET /status/410 returns 410 Gone |
| 20 | `Get_Status413_ReturnsContentTooLarge` | Fact | IT-11-109: GET /status/413 returns 413 Content Too Large |
| 21 | `Get_Status429_ReturnsTooManyRequests` | Fact | IT-11-110: GET /status/429 returns 429 Too Many Requests |
| 22 | `Get_Status500_ReturnsInternalServerError` | Fact | IT-11-111: GET /status/500 returns 500 Internal Server Error |
| 23 | `Get_Status501_ReturnsNotImplemented` | Fact | IT-11-112: GET /status/501 returns 501 Not Implemented |
| 24 | `Get_Status502_ReturnsBadGateway` | Fact | IT-11-113: GET /status/502 returns 502 Bad Gateway |
| 25 | `Get_Status503_ReturnsServiceUnavailable` | Fact | IT-11-114: GET /status/503 returns 503 Service Unavailable |
| 26 | `Get_Status504_ReturnsGatewayTimeout` | Fact | IT-11-115: GET /status/504 returns 504 Gateway Timeout |
| 27 | `TwoXx_ExceptNoContent_HaveBody` | Theory | IT-11-116: 2xx status codes (except 204) have non-empty body |
| 28 | `ThreeXx_DecodedWithoutError` | Theory | IT-11-117: 3xx status codes are decoded without error |
| 29 | `FiveXx_DecodedWithoutError` | Theory | IT-11-118: 5xx status codes are decoded without error |
| 30 | `Sequential_4xxResponses_KeepAlive_AllDecoded` | Fact | IT-11-119: Sequential 4xx responses on keep-alive connection all decoded |

#### `Http11SecurityTests.cs` - `Http11SecurityTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `LargeBody_10MB_DecoderNoOOM` | Fact | IT-11A-045: GET /large/10240 (10 MB) — decoder accumulates large body without OOM |
| 2 | `FiftyCustomHeaders_AllPreservedInResponse` | Fact | IT-11A-046: 50 custom headers in request — all echoed back in response |
| 3 | `HeaderInjection_CrInValue_Rejected` | Fact | IT-11A-047: Header value with CR rejected by encoder (header injection prevention) |
| 4 | `HeaderInjection_LfInValue_Rejected` | Fact | IT-11A-048: Header value with LF rejected by encoder (header injection prevention) |
| 5 | `HeaderInjection_NulInValue_Rejected` | Fact | IT-11A-049: Header value with NUL byte rejected by encoder |
| 6 | `CrlfInBody_TreatedAsOpaque_EchoedIntact` | Fact | IT-11A-050: POST /echo with CRLF bytes in body — body treated as opaque, echoed intact |
| 7 | `ZeroContentLength_PostEcho_Returns200_EmptyBody` | Fact | IT-11A-051: POST /echo with Content-Length: 0 — server returns 200 with empty body |
| 8 | `NegativeContentLength_Rejected_ByEncoder` | Fact | IT-11A-052: Content-Length < 0 — encoder rejects with exception |
| 9 | `LongUri_Over8KB_EncoderHandlesWithoutException` | Fact | IT-11A-053: GET with query string > 8 KB — encoder encodes without exception |
| 10 | `SlowResponse_DecoderAccumulates_ReturnsCompleteBody` | Fact | IT-11A-054: GET /slow/10 — decoder accumulates 10 bytes arriving 1-per-flush |

#### `Http11CachingTests.cs` - `Http11CachingTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `IfNoneMatch_MatchesETag_Returns304_NoBody` | Fact | IT-11A-030: If-None-Match matches ETag — server returns 304 with no body |
| 2 | `IfNoneMatch_NoMatch_Returns200_FullBody` | Fact | IT-11A-031: If-None-Match no match — server returns 200 with full body |
| 3 | `ETag_InResponse_IsValidQuotedString` | Fact | IT-11A-032: ETag in 200 response is a valid quoted-string |
| 4 | `IfModifiedSince_PastDate_Returns200` | Fact | IT-11A-033: If-Modified-Since with past date — server returns 200 with full body |
| 5 | `IfModifiedSince_FutureDate_Returns304` | Fact | IT-11A-034: If-Modified-Since with future date — server returns 304 Not Modified |
| 6 | `LastModified_InResponse_Present` | Fact | IT-11A-035: Last-Modified header present in response from /if-modified-since |
| 7 | `LastModified_IsParseable_Rfc7231Date` | Fact | IT-11A-036: Last-Modified date is parseable RFC 7231 date |
| 8 | `CacheControl_NoCacheRequest_ServerReturns200` | Fact | IT-11A-037: Cache-Control: no-cache request header sent — server still returns 200 |
| 9 | `CacheControl_MaxAge0_ServerReturns200` | Fact | IT-11A-038: Cache-Control: max-age=0 request header sent — server still returns 200 |
| 10 | `Get_CacheNoStore_ResponseHasNoCacheControl_NoStore` | Fact | IT-11A-039: GET /cache/no-store — response Cache-Control contains no-store |
| 11 | `Get_Cache_ResponseHasCacheControlDirectives` | Fact | IT-11A-040: GET /cache — response Cache-Control max-age and public directives present |
| 12 | `Get_Cache_ResponseHasExpires_InFuture` | Fact | IT-11A-041: GET /cache — response Expires header present and in the future |
| 13 | `Get_Cache_ResponseHasPragmaNoCache` | Fact | IT-11A-042: GET /cache — response Pragma: no-cache header present |
| 14 | `ETag_RoundTrip_200ThenConditional304` | Fact | IT-11A-043: ETag from 200 response used in next If-None-Match — returns 304 |
| 15 | `IfModifiedSince_WithLastModifiedDate_Returns304` | Fact | IT-11A-044: If-Modified-Since with Last-Modified date → 304 (resource not changed) |

#### `Http11BasicTests.cs` - `Http11BasicTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_Any_Returns200_MethodNameInBody` | Fact | IT-11-001: GET /any returns 200 with method 'GET' in body |
| 2 | `Post_Any_Returns200_MethodNameInBody` | Fact | IT-11-002: POST /any returns 200 with method 'POST' in body |
| 3 | `Head_Any_Returns200_NoBody` | Fact | IT-11-003: HEAD /any returns 200 with no body and HTTP/1.1 version |
| 4 | `Put_Echo_Returns200_EchoesBody` | Fact | IT-11-004: PUT /echo returns 200 echoing request body |
| 5 | `Delete_Any_Returns200` | Fact | IT-11-005: DELETE /any returns 200 |
| 6 | `Patch_Echo_Returns200_EchoesBody` | Fact | IT-11-006: PATCH /echo returns 200 echoing request body |
| 7 | `Options_Any_Returns200` | Fact | IT-11-007: OPTIONS /any returns 200 |
| 8 | `Get_Hello_HostHeader_IsRequiredAndPresent` | Fact | IT-11-008: GET /hello Host header required — server sees correct Host |
| 9 | `Get_Hello_ResponseVersion_IsHttp11` | Fact | IT-11-009: HTTP/1.1 response carries HTTP/1.1 version |
| 10 | `Get_Status_ReturnsExpectedStatusCode` | Theory | IT-11-010: GET /status/{code} returns the expected status code |
| 11 | `Get_Large_1KB_Returns200_With1KbBody` | Fact | IT-11-011: GET /large/1 returns 200 with 1 KB body |
| 12 | `Get_Large_64KB_Returns200_With64KbBody` | Fact | IT-11-012: GET /large/64 returns 200 with 64 KB body |
| 13 | `Get_Large_512KB_Returns200_With512KbBody` | Fact | IT-11-013: GET /large/512 returns 200 with 512 KB body |
| 14 | `Get_Large_ContentLength_MatchesActualBodyLength` | Theory | IT-11-014: GET /large/{kb} Content-Length matches actual body byte count |
| 15 | `Get_Content_TextPlain_HasCorrectContentType` | Fact | IT-11-015: GET /content/text/plain response has Content-Type text/plain |
| 16 | `Get_Content_ApplicationJson_HasCorrectContentType` | Fact | IT-11-016: GET /content/application/json response has Content-Type application/json |
| 17 | `Get_Content_OctetStream_HasCorrectContentType` | Fact | IT-11-017: GET /content/application/octet-stream response has correct Content-Type |
| 18 | `Post_Echo_SmallBody_IsEchoedCorrectly` | Fact | IT-11-018: POST /echo small body is echoed correctly |
| 19 | `Post_Echo_64KbBody_IsEchoedCorrectly` | Fact | IT-11-019: POST /echo 64 KB body is echoed correctly |
| 20 | `Post_Echo_EmptyBody_Returns200_EmptyBody` | Fact | IT-11-020: POST /echo empty body returns 200 with empty body |
| 21 | `Get_Hello_ResponseHasDateHeader` | Fact | IT-11-021: GET /hello response includes Date header |
| 22 | `Get_Hello_Returns200_WithBodyHelloWorld` | Fact | IT-11-022: GET /hello returns body 'Hello World' |
| 23 | `TwoIndependent_GetPing_BothSucceed` | Fact | IT-11-023: Two independent GET /ping requests each succeed |
| 24 | `Get_Status204_ReturnsNoContent_EmptyBody` | Fact | IT-11-024: GET /status/204 returns 204 with empty body |
| 25 | `Get_Status304_ReturnsNotModified_EmptyBody` | Fact | IT-11-025: GET /status/304 returns 304 with empty body |

#### `Http11ChunkedTests.cs` - `Http11ChunkedTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Get_Chunked_1KB_ReturnsCorrectBody` | Fact | IT-11-050: GET /chunked/1 returns chunked response with 1 KB of data |
| 2 | `Get_Chunked_64KB_ReturnsCorrectBody` | Fact | IT-11-051: GET /chunked/64 returns chunked response with 64 KB of data |
| 3 | `Get_Chunked_512KB_ReturnsCorrectBody` | Fact | IT-11-052: GET /chunked/512 returns chunked response with 512 KB of data |
| 4 | `Get_ChunkedExact_NChunks_AllDataReceived` | Theory | IT-11-053: Chunked response with N chunks — all data received correctly |
| 5 | `Get_ChunkedExact_VariousChunkSizes_DecodedCorrectly` | Theory | IT-11-054: Chunked response with various chunk sizes decoded correctly |
| 6 | `Post_EchoChunked_RequestBodyEchoedChunked` | Fact | IT-11-055: POST /echo/chunked — request body echoed as chunked response |
| 7 | `Post_EchoChunked_BinaryBody_ByteAccurateRoundTrip` | Fact | IT-11-056: POST /echo/chunked with binary body — byte-accurate round-trip |
| 8 | `Get_ChunkedTrailer_TrailerHeaderPresent` | Fact | IT-11-057: GET /chunked/trailer — chunked response includes trailer header |
| 9 | `ChunkedResponse_ThenKeepAlive_NextRequestSucceeds` | Fact | IT-11-058: Chunked response followed by normal keep-alive request on same connection |
| 10 | `Post_EchoChunked_EmptyBody_Returns200EmptyResponse` | Fact | IT-11-059: POST /echo/chunked with empty body — returns 200 empty chunked response |
| 11 | `Head_Chunked_ResponseHasNoBody` | Fact | IT-11-060: HEAD /chunked/1 — response has no body but headers present |
| 12 | `Get_ChunkedMd5_BodyMatchesContentMd5Header` | Fact | IT-11-061: GET /chunked/md5 — body MD5 matches Content-MD5 header |
| 13 | `LargeChunked_DecodedAcrossMultipleTcpReads` | Fact | IT-11-062: Large chunked response decoded correctly across multiple TCP reads |
| 14 | `Decoder_LastChunk_ImmediatelyAfterData_ParsedCorrectly` | Fact | IT-11-063: Last-chunk 0\\r\\n\\r\\n immediately after data — decoder parses correctly |
| 15 | `Pipeline_TwoChunkedResponses_DecodedInOrder` | Fact | IT-11-064: Two pipelined chunked responses decoded in order |
| 16 | `Get_Chunked_ResponseUsesChunkedTransferEncoding` | Fact | IT-11-065: GET /chunked/1 response uses Transfer-Encoding: chunked |
| 17 | `AlternatingChunkedAndNormal_KeepAliveConnection_AllSucceed` | Fact | IT-11-066: Alternating chunked and normal requests on keep-alive connection |
| 18 | `Get_ChunkedExact_1ByteChunks_BodyAssembledCorrectly` | Fact | IT-11-067: Chunked response with 1-byte chunks — body assembled correctly |
| 19 | `Get_Chunked_WireUsesChunkedTransferEncoding` | Fact | IT-11-068: Chunked response uses Transfer-Encoding: chunked on the wire — RFC 9112 §6.1 |

#### `Http11EdgeCaseTests.cs` - `Http11EdgeCaseTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `EmptyBody_ContentLength0_DecodedAsEmpty` | Fact | IT-11A-055: GET /empty-cl — 200 with Content-Length: 0 decoded as empty body |
| 2 | `Status204_NoBody_NoContentLength` | Fact | IT-11A-056: GET /status/204 — 204 No Content has empty body and no Content-Length |
| 3 | `Status304_NotModified_NoBody` | Fact | IT-11A-057: 304 Not Modified — no body in response |
| 4 | `HeadRequest_HeadersOnly_NoBody` | Fact | IT-11A-058: HEAD /any — response has headers only, no body allowed |
| 5 | `MinimalResponse_OnlyStatusLine_DecodedSuccessfully` | Fact | IT-11A-059: Minimal HTTP/1.1 200 OK\\r\\n\\r\\n — decoder parses correctly |
| 6 | `ExtraBlankLineBeforeBody_DecoderUsesFirstCrlfCrlf` | Fact | IT-11A-060: Extra blank line before body — decoder stops at first CRLFCRLF |
| 7 | `UnknownHeaders_PreservedInResponse` | Fact | IT-11A-061: GET /unknown-headers — response non-standard X-Unknown-* headers preserved |
| 8 | `OptionsAsterisk_EncoderProducesCorrectRequestLine` | Fact | IT-11A-062: OPTIONS * — encoder produces OPTIONS * HTTP/1.1 request line |
| 9 | `Options_Path_Returns200_MethodInBody` | Fact | IT-11A-063: OPTIONS /any — returns 200 with method name 'OPTIONS' in body |
| 10 | `PostEmptyBody_Returns200_EmptyEcho` | Fact | IT-11A-064: POST /echo with empty body — server returns 200 with empty echo |
| 11 | `PutBinaryBody_AllByteValues_EchoedIntact` | Fact | IT-11A-065: PUT /echo with binary body containing all byte values 0x00..0xFF — echoed intact |
| 12 | `PatchJsonBody_EchoedVerbatim` | Fact | IT-11A-066: PATCH /echo with JSON body — server echoes JSON verbatim |
| 13 | `Get_Hello_ResponseVersion_IsHttp11` | Fact | IT-11A-067: GET /hello — decoded response has HTTP/1.1 version |
| 14 | `KeepAlive_TwoSequentialRequests_BothSucceed` | Fact | IT-11A-068: Two sequential GET requests on keep-alive connection — both succeed |

#### `Http11ContentNegotiationTests.cs` - `Http11ContentNegotiationTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Accept_ApplicationJson_ServerReturnsJsonContentType` | Fact | IT-11A-001: Accept: application/json — server returns Content-Type application/json |
| 2 | `Accept_TextHtml_ServerReturnsHtmlContentType` | Fact | IT-11A-002: Accept: text/html — server returns Content-Type text/html |
| 3 | `Accept_Wildcard_ServerReturnsDefaultContentType` | Fact | IT-11A-003: Accept: */* — server returns default Content-Type |
| 4 | `Accept_WithQValues_HighestQMatched` | Fact | IT-11A-004: Accept with q-values (text/html;q=0.9,application/json;q=1.0) — highest q matched |
| 5 | `AcceptCharset_Utf8_SentWithoutError` | Fact | IT-11A-005: Accept-Charset: utf-8 header sent in request without error |
| 6 | `AcceptCharset_MultiValue_SentWithoutError` | Fact | IT-11A-006: Accept-Charset: iso-8859-1,utf-8 multi-value sent without error |
| 7 | `AcceptLanguage_EnUS_SentWithoutError` | Fact | IT-11A-007: Accept-Language: en-US sent in request without error |
| 8 | `AcceptLanguage_MultiValue_SentWithoutError` | Fact | IT-11A-008: Accept-Language: fr,en;q=0.8 multi-value sent without error |
| 9 | `ContentType_MultipartFormData_ServerParsesBody` | Fact | IT-11A-009: Content-Type: multipart/form-data — server parses body successfully |
| 10 | `ContentType_UrlEncoded_ServerParsesBody` | Fact | IT-11A-010: Content-Type: application/x-www-form-urlencoded — server parses body |
| 11 | `DefaultResponse_NoContentEncoding_OrIdentity` | Fact | IT-11A-011: Default response has no Content-Encoding or Content-Encoding: identity |
| 12 | `Request_ContentEncodingIdentity_AcceptedByServer` | Fact | IT-11A-012: Request with Content-Encoding: identity header accepted by server |
| 13 | `Get_GzipMeta_ContentEncodingHeaderPresent` | Fact | IT-11A-013: GET /gzip-meta — response Content-Encoding header present in decoded response |
| 14 | `Get_NegotiateVary_VaryHeaderContainsAccept` | Fact | IT-11A-014: GET /negotiate/vary — response Vary header contains Accept |
| 15 | `AcceptAndAcceptLanguage_BothInRequest_Returns200` | Fact | IT-11A-015: Accept and Accept-Language both in same request — server returns 200 |

### Http2 (133 tests)

#### `Http2LargePayloadTests.cs` - `Http2LargePayloadTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_DecodeFully_When_OneMegabyteResponseBodyReceived` | Fact | IT-2A-050: 1 MB response body decoded correctly — all bytes match |
| 2 | `Should_DecodeFully_When_FourMegabyteResponseBodyReceived` | Fact | IT-2A-051: 4 MB response body decoded correctly — length and content verified |
| 3 | `Should_EchoRequestBody_When_60KbBodySentToEchoEndpoint` | Fact | IT-2A-052: 60 KB request body encoded and echoed correctly |
| 4 | `Should_PreserveAssemblyOrder_When_BodySpansMultipleDataFrames` | Fact | IT-2A-053: Multiple DATA frames reassembly order preserved — 32 KB body |
| 5 | `Should_MatchExpectedHash_When_OneMegabyteBodyReceived` | Fact | IT-2A-054: Body matches SHA-256 of expected content — 1 MB all-'A' bytes |
| 6 | `Should_DecodeBodyAndHeaders_When_LargeBodyWithLargeHeadersReceived` | Fact | IT-2A-055: Large body + large response headers — both decoded correctly |
| 7 | `Should_ReassembleBody_When_ServerStreamsOneByteAtATime` | Fact | IT-2A-056: Streaming decode: slow endpoint delivers body byte-by-byte — reassembled correctly |
| 8 | `Should_DeliverCorrectBodies_When_SequentialLargeResponsesReceived` | Fact | IT-2A-057: Sequential large bodies — no state leakage between responses |
| 9 | `Should_PreserveBodyIntegrity_When_ConcurrentLargeBodiessReceived` | Fact | IT-2A-058: Large body on multiple concurrent streams — body integrity per stream |

#### `Http2HpackTests.cs` - `Http2HpackTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_EncodeAllLiteral_When_FirstRequestSent` | Fact | IT-2-040: First request — all headers encoded as literals (cold HPACK state) |
| 2 | `Should_UseSmallerHeadersFrame_When_SecondIdenticalRequestSent` | Fact | IT-2-041: Second identical request uses indexed headers (smaller HEADERS frame) |
| 3 | `Should_GrowDynamicTable_When_CustomHeadersSentAcrossRequests` | Fact | IT-2-042: HPACK dynamic table grows across requests with custom headers |
| 4 | `Should_NeverIndexAuthorizationHeader_When_Encoded` | Fact | IT-2-043: Sensitive header (Authorization) is never-indexed |
| 5 | `Should_UseStaticTableEntries_When_CommonMethodsAndPathsEncoded` | Fact | IT-2-044: HPACK static table entries used (method, path, status) |
| 6 | `Should_CompressHeaders_When_HuffmanEncodingEnabled` | Fact | IT-2-045: Huffman encoding enabled — request headers are compressed |
| 7 | `Should_EvictTableEntries_When_TableSizeLimitReached` | Fact | IT-2-046: HPACK dynamic table eviction — table does not grow unbounded |
| 8 | `Should_PlacePseudoHeadersFirst_When_RequestEncoded` | Fact | IT-2-047: Pseudo-headers order — :method, :path, :scheme, :authority come first |
| 9 | `Should_SetStatusCode_When_StatusPseudoHeaderDecoded` | Fact | IT-2-048: Response pseudo-header :status decoded — HttpResponseMessage.StatusCode set correctly |
| 10 | `Should_EchoTwentyCustomHeaders_When_SentInRequest` | Fact | IT-2-049: 20 custom headers sent and echoed back in response |
| 11 | `Should_DecodeSetCookieHeader_When_ResponseContainsCookie` | Fact | IT-2-050: Response has Set-Cookie header — decoded without errors |
| 12 | `Should_SucceedRoundTrip_When_AuthorizationHeaderSent` | Fact | IT-2-051: Authorization header sent and accepted by server (never-index does not break round-trip) |
| 13 | `Should_DecodeResponseHeaders_When_HeadersUseIndexedAndLiteralMix` | Fact | IT-2-052: HPACK decoder handles indexed + literal + indexed mix in response headers |
| 14 | `Should_CompressSubsequentRequests_When_SameCustomHeaderRepeated` | Fact | IT-2-053: Multiple requests with same custom header — subsequent encodings are smaller |

#### `Http2MultiplexingTests.cs` - `Http2MultiplexingTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ReturnBothResponses_When_TwoConcurrentStreamsSent` | Fact | IT-2A-001: 2 concurrent streams on same connection — both return 200 |
| 2 | `Should_ReturnAllResponses_When_FourConcurrentStreamsSent` | Fact | IT-2A-002: 4 concurrent streams on same connection — all return 200 |
| 3 | `Should_ReturnAllResponses_When_EightConcurrentStreamsSent` | Fact | IT-2A-003: 8 concurrent streams on same connection — all return 200 |
| 4 | `Should_ReturnAllResponses_When_SixteenConcurrentStreamsSent` | Fact | IT-2A-004: 16 concurrent streams on same connection — all return 200 |
| 5 | `Should_ReassembleBothBodies_When_DataFramesInterleaved` | Fact | IT-2A-005: Streams interleaved: large + small body concurrently — both correct |
| 6 | `Should_CollectAllResponses_When_StreamsCompleteOutOfOrder` | Fact | IT-2A-006: Streams complete out of order — collector handles any arrival order |
| 7 | `Should_CompleteBothStreams_When_DifferentBodySizesUsed` | Fact | IT-2A-007: High-priority (small) + low-priority (large) streams — both complete correctly |
| 8 | `Should_ReturnCorrectResponses_When_ConcurrentGetAndPostSent` | Fact | IT-2A-008: Concurrent GET + POST — both return correct responses |
| 9 | `Should_PreserveBodyIntegrity_When_LargeAndSmallBodiesInterleaved` | Fact | IT-2A-009: Stream 1 large body + stream 3 small body — body integrity preserved |
| 10 | `Should_SucceedSequentially_When_MaxConcurrentStreamsIsOne` | Fact | IT-2A-010: MAX_CONCURRENT_STREAMS = 1: sequential requests succeed after SETTINGS |
| 11 | `Should_SucceedAllRequests_When_MaxConcurrentStreamsFourAndFiveRequestsSent` | Fact | IT-2A-011: MAX_CONCURRENT_STREAMS = 4: five sequential requests all succeed |
| 12 | `Should_ReturnCorrectBodyPerStream_When_FourConcurrentRequestsSent` | Fact | IT-2A-012: All concurrent streams return correct bodies — each verified individually |
| 13 | `Should_DecodeCorrectStatusCodes_When_ConcurrentStreamsReturnDifferentCodes` | Fact | IT-2A-013: Concurrent streams with different response codes — all decoded correctly |
| 14 | `Should_ReturnSameBody_When_TwoStreamsRequestSamePath` | Fact | IT-2A-014: Two streams with same request path — both return identical bodies |
| 15 | `Should_SucceedAllStreams_When_TwentySequentialStreamsUsed` | Fact | IT-2A-015: Stream reuse: 20 sequential streams on one connection — all succeed |

#### `Http2StreamTests.cs` - `Http2StreamTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ReturnHelloWorld_When_GetHelloSentOnStream1` | Fact | IT-2-020: Stream 1: GET /hello → 200 + body 'Hello World' |
| 2 | `Should_EchoRequestBody_When_PostEchoSentOnStream1` | Fact | IT-2-021: Stream 1: POST /echo → server echoes body |
| 3 | `Should_ReturnNoBody_When_HeadRequestSent` | Fact | IT-2-022: HEAD /hello → 200, no body in response |
| 4 | `Should_ReturnNoContent_When_Status204Requested` | Fact | IT-2-023: GET /status/204 → 204 No Content, empty body |
| 5 | `Should_ReturnCorrectResponses_When_TwoSequentialStreamsUsed` | Fact | IT-2-024: Stream 1 then stream 3 (sequential) — both return correct responses |
| 6 | `Should_ReturnOk_When_ThreeSequentialStreamsSent` | Fact | IT-2-025: Three sequential streams (1, 3, 5) — all return 200 |
| 7 | `Should_RemainFunctional_When_ClientSendsRstStreamCancel` | Fact | IT-2-026: Client sends RST_STREAM CANCEL — connection remains functional |
| 8 | `Should_SetEndStreamOnHeaders_When_GetRequestHasNoBody` | Fact | IT-2-027: GET request has END_STREAM on HEADERS frame (no body) |
| 9 | `Should_SetEndStreamOnDataFrame_When_PostRequestHasBody` | Fact | IT-2-028: POST request has END_STREAM on DATA frame |
| 10 | `Should_SendContinuationFrame_When_HeaderBlockExceedsMaxFrameSize` | Fact | IT-2-029: CONTINUATION frame triggered by large HEADERS block |
| 11 | `Should_SendMultipleContinuationFrames_When_HeaderBlockIsVeryLarge` | Fact | IT-2-030: Multiple CONTINUATION frames for very large HEADERS block |
| 12 | `Should_TransitionThroughStreamStates_When_RequestCompletes` | Fact | IT-2-031: Stream state idle → open → half-closed → closed completes cleanly |
| 13 | `Should_DeliverLargeBody_When_60KbResponseRequested` | Fact | IT-2-032: Large response body (60 KB) delivered across multiple DATA frames |
| 14 | `Should_EcholargeRequestBody_When_32KbPostSent` | Fact | IT-2-033: Large request body (32 KB) sent via DATA frames and echoed |
| 15 | `Should_ReassembleFragmentedBody_When_LargeBodyReceivedInMultipleFrames` | Fact | IT-2-034: Response body fragmented across multiple DATA frames is correctly reassembled |
| 16 | `Should_HandleFiveSequentialStreams_When_SentOnSameConnection` | Fact | IT-2-035: Five sequential streams each get correct responses |

#### `Http2PushPromiseTests.cs` - `Http2PushPromiseTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ReturnPromisedStreamId_When_PushPromiseDecoded` | Fact | IT-2A-020: PUSH_PROMISE received and decoded — promised stream ID returned |
| 2 | `Should_AcceptEvenStreamId_When_AnnouncedViaPushPromise` | Fact | IT-2A-021: Push stream ID is even (server-initiated) — decoder accepts it |
| 3 | `Should_ProcessWithoutError_When_PushPromiseContainsHeaderBlock` | Fact | IT-2A-022: PUSH_PROMISE header block present — decoder processes frame without error |
| 4 | `Should_AssemblePushResponse_When_HeadersThenDataOnPushStream` | Fact | IT-2A-023: Push stream DATA frames received — response assembled correctly |
| 5 | `Should_CloseStream_When_RstStreamSentOnPushedStream` | Fact | IT-2A-024: RST_STREAM on pushed stream (refuse push) — stream closed as cancelled |
| 6 | `Should_IncludeEnablePushZero_When_ClientPrefaceBuilt` | Fact | IT-2A-025: PUSH_PROMISE disabled via SETTINGS_ENABLE_PUSH=0 in client preface |
| 7 | `Should_DecodeHeaderBlock_When_PushPromiseContainsRequestPseudoHeaders` | Fact | IT-2A-026: PUSH_PROMISE with :path and :status-equivalent pseudo-headers decoded |
| 8 | `Should_RegisterAllPromisedStreams_When_MultiplePushPromisesReceived` | Fact | IT-2A-027: Multiple push promises in one response — all stream IDs registered |
| 9 | `Should_TrackPushStreamId_When_PushPromiseReceivedOnStream1` | Fact | IT-2A-028: Push promise on stream 1 → push stream 2 — decoder tracks mapping |
| 10 | `Should_ReturnResponse_When_PushStreamHasEndStreamOnHeaders` | Fact | IT-2A-029: Push stream END_STREAM on HEADERS — response returned immediately |

#### `Http2DataFrameTests.cs` - `Http2DataFrameTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_HaveNoDataBytes_When_Status204Received` | Fact | IT-2-060: 204 No Content response has zero DATA bytes |
| 2 | `Should_ReturnEmptyBody_When_ZeroBytePostSent` | Fact | IT-2-061: Zero-byte POST body → echo returns empty body |
| 3 | `Should_ReturnExactBody_When_SmallResponseFitsInOneFrame` | Fact | IT-2-062: Small response body in a single DATA frame — body matches exactly |
| 4 | `Should_AssembleBody_When_BodySpansTwoDataFrames` | Fact | IT-2-063: 17 KB body delivered in two DATA frames (> 16384 bytes) |
| 5 | `Should_AssembleCompleteBody_When_MultipleDataFramesPlusFinalEndStream` | Fact | IT-2-064: Multiple DATA frames + END_STREAM assembles complete body |
| 6 | `Should_HaveInitialReceiveWindow_When_ConnectionOpened` | Fact | IT-2-065: Flow control — connection receive window starts at 65535 |
| 7 | `Should_DecrementReceiveWindow_When_DataFramesReceived` | Fact | IT-2-066: Flow control — receive window decrements as DATA frames arrive |
| 8 | `Should_ReceiveLargeBody_When_WindowUpdateSentToReplenish` | Fact | IT-2-067: Flow control — large body (60 KB) received after WINDOW_UPDATE |
| 9 | `Should_AcceptMoreData_When_ManualWindowUpdateSent` | Fact | IT-2-068: Manual WINDOW_UPDATE on connection (stream 0) — server can send more data |
| 10 | `Should_DeliverResponse_When_PostBodyDataFrameHasEndStream` | Fact | IT-2-069: POST body DATA frame carries END_STREAM — response delivered correctly |
| 11 | `Should_DecrementStreamWindow_When_DataFramesReceivedOnStream` | Fact | IT-2-070: Stream-level receive window decrements correctly for 4 KB response |
| 12 | `Should_ReassembleBody_When_DeliveredInManySmallReads` | Fact | IT-2-071: Body delivered in many small TCP reads reassembles correctly |
| 13 | `Should_DecodeContentTypeHeader_When_ResponseContainsIt` | Fact | IT-2-072: Response content-type header decoded and accessible |

#### `Http2ConnectionTests.cs` - `Http2ConnectionTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ReceiveServerSettings_When_SendingConnectionPreface` | Fact | IT-2-001: Connection preface sent and server SETTINGS received |
| 2 | `Should_SendSettingsAck_When_ServerSettingsReceived` | Fact | IT-2-002: SETTINGS ACK sent in response to server SETTINGS |
| 3 | `Should_ReceivePingAck_When_PingIsSent` | Fact | IT-2-003: PING → server returns PING ACK with same data |
| 4 | `Should_ReceiveMatchingPingAcks_When_MultiplePingsSent` | Fact | IT-2-004: Multiple PING frames — each ACK matches its request data |
| 5 | `Should_ReceiveInitialWindowSize_When_HandshakeCompletes` | Fact | IT-2-005: Server SETTINGS contains INITIAL_WINDOW_SIZE |
| 6 | `Should_HonorMaxFrameSize_When_ServerAnnouncesIt` | Fact | IT-2-006: SETTINGS: server announces MAX_FRAME_SIZE — connection remains functional |
| 7 | `Should_SucceedWithSequentialStreams_When_MaxConcurrentStreamsIsRespected` | Fact | IT-2-007: SETTINGS: MAX_CONCURRENT_STREAMS respected — sequential streams succeed |
| 8 | `Should_AcceptEmptySettings_When_ZeroParametersPresent` | Fact | IT-2-008: SETTINGS frame with zero parameters is valid — no error |
| 9 | `Should_SendGoAway_When_ClientDisconnects` | Fact | IT-2-009: Client sends GOAWAY before disconnect — no server error |
| 10 | `Should_HaveInitialConnectionWindow_When_Connected` | Fact | IT-2-010: Connection-level flow control — initial window is 65535 |
| 11 | `Should_IncreaseConnectionSendWindow_When_WindowUpdateReceived` | Fact | IT-2-011: WINDOW_UPDATE on connection level — encoder send window increases |
| 12 | `Should_RemainFunctional_When_NoRequestsSentBetweenRequests` | Fact | IT-2-012: Idle connection — multiple requests succeed without error |
| 13 | `Should_UseNewWindowSize_When_SettingsUpdatedMidConnection` | Fact | IT-2-013: SETTINGS update INITIAL_WINDOW_SIZE mid-connection — next stream uses new window |
| 14 | `Should_SupportMultipleIndependentConnections_When_ServerIsRunning` | Fact | IT-2-014: Two separate connections to the same server both succeed |
| 15 | `Should_Have24ByteConnectionPreface_When_PrefaceMagicInspected` | Fact | IT-2-015: Connection preface magic is exactly 24 bytes (RFC 7540 §3.5) |
| 16 | `Should_Return200_When_SimpleGetSentOverHttp2` | Fact | IT-2-016: Server responds with 200 status to a basic GET over HTTP/2 |

#### `Http2EdgeCaseTests.cs` - `Http2EdgeCaseTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ReturnEmptyBodyResponse_When_StreamImmediatelyClosed` | Fact | IT-2A-060: Immediately closed stream (HEADERS + END_STREAM, no DATA) — decoded correctly |
| 2 | `Should_ReturnResponse_When_HeadersFrameHasEndStream` | Fact | IT-2A-061: Immediately closed stream via decoder — HEADERS with END_STREAM returns response |
| 3 | `Should_ApplyAllSettings_When_SettingsFrameHasMultipleParameters` | Fact | IT-2A-062: SETTINGS with multiple parameters in one frame — all applied correctly |
| 4 | `Should_ParseAllParameters_When_SettingsFrameContainsMultipleEntries` | Fact | IT-2A-063: SETTINGS decoder parses multiple parameters in one frame |
| 5 | `Should_EchoOpaqueData_When_PingAckReceived` | Fact | IT-2A-064: PING with 8-byte opaque data round-trip — ACK data matches |
| 6 | `Should_IgnoreUnknownFrameType_When_Frame0xFeReceived` | Fact | IT-2A-065: Unknown frame type 0xFE — decoder ignores silently (RFC 7540 §4.1) |
| 7 | `Should_ProcessNormally_When_HeadersFrameHasUnknownFlags` | Fact | IT-2A-066: Unknown flags on HEADERS frame — decoder processes frame normally |
| 8 | `Should_ReturnResponseAndGoAway_When_BothDecodedInSameBatch` | Fact | IT-2A-067: GOAWAY received mid-connection — decoder returns both response and GOAWAY |
| 9 | `Should_RemainFunctional_When_MaxConcurrentStreamsIncreased` | Fact | IT-2A-068: Connection reuse after SETTINGS_MAX_CONCURRENT_STREAMS update |
| 10 | `Should_IgnorePriorityFrame_When_Received` | Fact | IT-2A-069: PRIORITY frame decoded without error — ignored per RFC 9113 |
| 11 | `Should_Return200_When_PathIs4KbLong` | Fact | IT-2A-070: Very long :path value (4 KB URI) — server responds with 200 |
| 12 | `Should_Return200_When_AuthorityIncludesExplicitPort` | Fact | IT-2A-071: :authority with explicit port number — request succeeds |

#### `Http2FlowControlTests.cs` - `Http2FlowControlTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_OmitDataFrame_When_ConnectionSendWindowIsZero` | Fact | IT-2A-030: Stream window exhaustion — encoder omits DATA when window is zero |
| 2 | `Should_EncodeDataFrame_When_ConnectionWindowReplenished` | Fact | IT-2A-031: WINDOW_UPDATE received — encoder can send DATA after window replenishment |
| 3 | `Should_TrackConnectionAndStreamWindowsSeparately_When_EncoderUsed` | Fact | IT-2A-032: Connection window exhaustion — same as stream window for first stream |
| 4 | `Should_ReceiveLargeBody_When_ConnectionWindowUpdatedByServer` | Fact | IT-2A-033: Connection WINDOW_UPDATE resumes large body transfer over real connection |
| 5 | `Should_HandleBothFlowControlWindows_When_LargeBodiesUsed` | Fact | IT-2A-034: Mixed stream + connection flow control — both windows respected |
| 6 | `Should_HaveDefaultStreamWindow_When_NewStreamCreated` | Fact | IT-2A-035: Default stream window is 65535 bytes |
| 7 | `Should_ThrowFlowControlError_When_WindowUpdateCausesOverflow` | Fact | IT-2A-036: Window overflow detection — WINDOW_UPDATE > 2^31-1 throws FLOW_CONTROL_ERROR |
| 8 | `Should_ThrowProtocolError_When_StreamWindowUpdateIncrementIsZero` | Fact | IT-2A-037: Zero WINDOW_UPDATE increment on stream → PROTOCOL_ERROR |
| 9 | `Should_ThrowProtocolError_When_ConnectionWindowUpdateIncrementIsZero` | Fact | IT-2A-038: Zero WINDOW_UPDATE increment on connection → PROTOCOL_ERROR |
| 10 | `Should_DeliverBody_When_64KbBodyFitsInInitialWindow` | Fact | IT-2A-039: 64 KB body fits in initial connection window — delivered without WINDOW_UPDATE |
| 11 | `Should_ReceiveFullBody_When_128KbBodyRequiresMultipleWindowUpdates` | Fact | IT-2A-040: 128 KB body requires WINDOW_UPDATE mid-transfer — fully received |
| 12 | `Should_AccumulateWindow_When_MultipleWindowUpdateFramesReceived` | Fact | IT-2A-041: Multiple WINDOW_UPDATE frames cumulative — encoder tracks total window |
| 13 | `Should_TrackRemainingWindow_When_EncoderEncodesBodies` | Fact | IT-2A-042: Encoder correctly tracks remaining send window after encoding |

#### `Http2ErrorTests.cs` - `Http2ErrorTests`

| # | Method | Type | DisplayName / RFC Reference |
|---|--------|------|-----------------------------|
| 1 | `Should_ParseGoAway_When_DecoderReceivesGoAwayWithProtocolError` | Fact | IT-2-080: Decoder parses GOAWAY with PROTOCOL_ERROR from server |
| 2 | `Should_ParseGoAway_When_DecoderReceivesGoAwayWithEnhanceYourCalm` | Fact | IT-2-081: Decoder parses GOAWAY with ENHANCE_YOUR_CALM from server |
| 3 | `Should_CloseCleanly_When_ClientSendsGoAwayNoError` | Fact | IT-2-082: Client sends GOAWAY NO_ERROR — connection then closed cleanly |
| 4 | `Should_ParseRstStream_When_DecoderReceivesRstStreamCancel` | Fact | IT-2-083: Decoder parses RST_STREAM with CANCEL error code |
| 5 | `Should_ParseRstStream_When_DecoderReceivesRstStreamStreamClosed` | Fact | IT-2-084: Decoder parses RST_STREAM with STREAM_CLOSED error code |
| 6 | `Should_ThrowProtocolError_When_DataFrameOnStream0` | Fact | IT-2-085: DATA frame on stream ID 0 → decoder throws Http2Exception PROTOCOL_ERROR |
| 7 | `Should_ThrowProtocolError_When_HeadersFrameHasEvenStreamId` | Fact | IT-2-086: HEADERS frame with even stream ID (server not promised) → PROTOCOL_ERROR |
| 8 | `Should_ThrowStreamClosed_When_HeadersSentOnClosedStream` | Fact | IT-2-087: HEADERS on previously closed stream → decoder throws STREAM_CLOSED (RFC 7540 §6.2) |
| 9 | `Should_ThrowProtocolError_When_EnablePushSetToInvalidValue` | Fact | IT-2-088: SETTINGS with ENABLE_PUSH=2 → decoder throws PROTOCOL_ERROR |
| 10 | `Should_ThrowFrameSizeError_When_SettingsAckHasNonEmptyPayload` | Fact | IT-2-089: SETTINGS ACK with non-empty payload → decoder throws FRAME_SIZE_ERROR |
| 11 | `Should_ThrowProtocolError_When_WindowUpdateIncrementIsZero` | Fact | IT-2-090: WINDOW_UPDATE increment of 0 → decoder throws PROTOCOL_ERROR |
| 12 | `Should_IgnoreUnknownFrameType_When_Received` | Fact | IT-2-091: Unknown frame type 0xFF — decoder ignores it (RFC 7540 §4.1) |
| 13 | `Should_DecodeRstStreamCleanly_When_ServerSendsIt` | Fact | IT-2-092: Server-initiated RST_STREAM decoded cleanly — no exception from decoder |
| 14 | `Should_ThrowProtocolError_When_StatusPseudoHeaderMissing` | Fact | IT-2-093: Response HEADERS without :status → decoder throws PROTOCOL_ERROR (RFC 9113 §8.3.2) |
| 15 | `Should_ThrowFrameSizeError_When_PayloadExceedsMaxFrameSize` | Fact | IT-2-094: Frame payload exceeds MAX_FRAME_SIZE → decoder throws FRAME_SIZE_ERROR |


---

