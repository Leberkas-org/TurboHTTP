TASK-023: Tests — Middleware Registration and DI Resolution

Add unit tests for `.AddMiddleware<T>()` and factory DI resolution in
`TurboHttpClientBuilderMiddlewareTests`. Covers: type registration in
MiddlewareTypes, Transient lifetime in ServiceCollection, UseRequest
adding to MiddlewareFactories only, FIFO ordering across multiple calls,
and factory resolving the correct instance from a real IServiceProvider.
7 tests, all green.
