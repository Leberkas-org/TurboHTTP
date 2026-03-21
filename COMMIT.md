TASK-007: BuildExtendedPipeline — Accept PipelineDescriptor

- `BuildExtendedPipeline` now receives a `PipelineDescriptor` parameter instead of
  always instantiating `CookieJar` and `HttpCacheStore` internally.
- `CreateFlow(poolRouter, options, factory)` (backward-compat public method) derives
  a `PipelineDescriptor` with `new CookieJar()` and `new HttpCacheStore(options.CachePolicy)`
  so existing callers are unaffected.
- `BuildPostProcessGraph` refactored to accept `PipelineDescriptor` directly, using
  `descriptor.RetryPolicy`, `descriptor.RedirectPolicy`, `descriptor.CookieJar`, and
  `descriptor.CacheStore` for all stage instantiation.
- `CacheLookupStage` and `CacheStorageStage` updated to accept nullable `HttpCacheStore?`
  (pass-through / always-miss when null), consistent with the existing nullable-CookieJar
  pattern in `CookieInjectionStage` and `CookieStorageStage`.
