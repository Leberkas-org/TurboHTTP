# Automatic Retries

TurboHTTP automatically retries failed requests when it is safe to do so — specifically, when the HTTP method is **idempotent** (safe to repeat without side effects) and the failure is a transient network or server error.

Retries are disabled by default. Enable them by calling `.WithRetry()` on the builder.

## How It Works

When a request fails, TurboHTTP checks three things before retrying:

1. **Is the method idempotent?** — Sending the same request twice must produce the same result. Only idempotent methods are retried.
2. **Is the failure transient?** — Connection drops, timeouts, and specific server-side error codes are considered transient. A `400 Bad Request` is not.
3. **Has the retry limit been reached?** — Each request is retried at most `MaxRetries` times.

If all three conditions are satisfied, TurboHTTP retries the request automatically. The caller receives either a successful response or the last error — retry attempts are transparent.

## Method Retry Table

| Method | Retried? | Reason |
|--------|----------|--------|
| `GET` | Yes | Idempotent — reading a resource has no side effects |
| `HEAD` | Yes | Idempotent — same as GET, response body omitted |
| `PUT` | Yes | Idempotent — replacing a resource produces the same result each time |
| `DELETE` | Yes | Idempotent — deleting an already-deleted resource is still "deleted" |
| `OPTIONS` | Yes | Idempotent — capability query with no side effects |
| `TRACE` | Yes | Idempotent — diagnostic echo with no side effects |
| `POST` | **No** | Non-idempotent — sending the same POST twice could create duplicate records |
| `PATCH` | **No** | Non-idempotent — partial updates may produce different results each time |
| `CONNECT` | **No** | Non-idempotent — establishes a tunnel, not a repeatable operation |

## Status Code Retry Table

| Status Code | Retry Behavior | Notes |
|-------------|---------------|-------|
| Network failure (no response) | Retried | Connection dropped, refused, or reset before any response arrived |
| `408 Request Timeout` | Retried | Server explicitly signals the request timed out and can be resent |
| `503 Service Unavailable` | Retried | Server temporarily unable to handle requests; respects `Retry-After` |
| Any other 4xx / 5xx | **Not retried** | Non-transient errors — retrying would not change the outcome |

## Retry-After Header

When a `408` or `503` response includes a `Retry-After` header, TurboHTTP parses the delay and waits before issuing the next attempt. Both formats are supported:

- **Delay in seconds:** `Retry-After: 30`
- **HTTP date:** `Retry-After: Fri, 20 Mar 2026 18:00:00 GMT`

If the date is in the past, the delay is treated as zero. Set `RespectRetryAfter = false` in the policy to ignore this header and retry immediately.

## Configuration

Retries are configured via `.WithRetry()` on the builder:

```csharp
// Enable retries with defaults: up to 3 retries, Retry-After respected
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry(RetryPolicy.Default);

// Custom retry policy
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry(new RetryPolicy
{
    MaxRetries = 5,          // retry up to 5 times (default: 3)
    RespectRetryAfter = true // honour Retry-After header (default: true)
});
```

## Non-Retryable Scenarios

The following situations are **never retried**, regardless of the method or status code:

- **Request body is a stream that cannot be rewound** — if the request body has been partially sent (e.g. a streaming upload), TurboHTTP cannot restart it from the beginning, so retrying would send an incomplete body.
- **Non-idempotent methods** — `POST`, `PATCH`, and `CONNECT` are never retried because repeating the request could create duplicate records or have unintended side effects.
- **Retry limit reached** — once `MaxRetries` attempts have been made, the last failure is returned to the caller.
- **`RetryPolicy` is null** — retries are disabled entirely.

## Zero-Configuration Retries

The simplest way to enable retries with sensible defaults:

```csharp
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry(RetryPolicy.Default);
```

`RetryPolicy.Default` retries up to 3 times and honours `Retry-After` delays from the server.
