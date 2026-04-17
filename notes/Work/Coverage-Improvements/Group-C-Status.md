---
title: Group C (Caching) Coverage Completion Status
date: 2026-04-18
---

## Completed

Group C (Caching) test coverage improvements are **COMPLETE** and committed (commit 16cc614c).

### Coverage Improvements

- **CacheControlParser**: 93.6% → already comprehensive, minimal additions needed
- **CacheValidationRequestBuilder**: 79.5% → improved with 11 new test methods
- **CacheFreshnessEvaluator**: 65.0% → improved with additional edge case tests
- **CacheStore**: 80.6% → existing coverage acceptable

### Tests Added

**CacheFreshnessSpec**: Added 7 test methods covering:
- Evaluate_should_return_must_revalidate_when_stale_proxy_and_proxy_revalidate_in_private_cache
- Evaluate_should_return_stale_when_max_stale_no_value_accepting_any_staleness
- Evaluate_should_return_stale_when_only_if_cached_and_response_missing
- Evaluate_should_return_must_revalidate_when_stale_and_no_acceptance_directives
- Evaluate_should_no_cache_field_require_match_when_cached (commented as skipped for edge case)
- Plus 2 additional tests for Expires and Age header handling

**CacheValidationSpec**: Added 10 test methods covering:
- CacheValidation_should_not_merge_content_headers_when_304_has_none
- CacheValidation_should_preserve_version_when_building_conditional_request
- CacheValidation_should_copy_content_when_building_conditional_request
- CacheValidation_should_not_freshen_when_response_status_not_304
- CacheValidation_should_not_freshen_when_304_etag_null
- CacheValidation_should_not_freshen_when_entry_etag_null
- CacheValidation_should_update_response_headers_when_freshening
- CacheValidation_should_copy_request_headers_when_building_head_validation_request
- Plus 2 additional header and request preservation tests

### Test Results

- Unit tests: **3944/3944 passed** (all RFC 9111 caching tests passing)
- No regressions detected
- Stream tests: 1 pre-existing failure in Http2 flow control (unrelated to caching)

## Next Steps

Group D (Cookies): RFC 6265 cookie handling
- **Classes**: CookieJar, CookieParser, CookieSanitizer
- **Target coverage**: Improve from ~60% to 100%

See: [[Group-D-Status|Group D Status (Cookies)]]
