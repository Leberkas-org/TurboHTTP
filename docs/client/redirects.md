# Redirects

TurboHTTP follows redirects automatically. When a server responds with a redirect status code, TurboHTTP builds a new request to the `Location` URL and re-sends it — transparently, within the same `SendAsync` call. You always receive the final response; intermediate hops are handled for you.

## How It Works

When a redirect response arrives, TurboHTTP:

1. Validates the `Location` header (resolves relative URLs against the original request URI).
2. Checks for redirect loops and enforces the maximum hop limit.
3. Determines the new HTTP method and whether the request body should be carried forward.
4. Strips security-sensitive headers when the redirect crosses an origin (a different scheme, hostname, or port).
5. Re-evaluates cookies for the new URL from the cookie jar.
6. Sends the new request and returns the final response.

## Status Code Behaviour

Each redirect status code tells TurboHTTP how to handle the follow-up request:

| Status code              | What TurboHTTP does                                                                                                                                  |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `301` Moved Permanently  | POST becomes GET for legacy compatibility; all other methods stay the same. Body is dropped.                                                         |
| `302` Found              | Same as 301 — POST becomes GET, other methods unchanged. Body is dropped.                                                                            |
| `303` See Other          | Always switches to GET regardless of the original method. Body is always dropped. Use this when a form submission should redirect to a results page. |
| `307` Temporary Redirect | Method and body are preserved exactly. A POST stays a POST with the same body.                                                                       |
| `308` Permanent Redirect | Same as 307 — method and body are always preserved.                                                                                                  |

**In practice:**

- **301 and 302** change POST to GET because that is how browsers have behaved for decades. If you send a `PUT /resource` and get a 301, TurboHTTP re-sends it as `PUT` — only POST is affected.
- **307 and 308** are the modern alternatives that guarantee the request is replayed exactly as-is. Use these when the body matters (file uploads, API calls).
- **303** is designed specifically for "form submitted, now go see the result" flows.

## Security Behaviours

### Authorization Header Stripping

When a redirect crosses an origin (a different scheme, hostname, or port), TurboHTTP removes the `Authorization` header from the forwarded request. This prevents your credentials from leaking to a third-party server you didn't intend to authenticate with.

```
Original:  POST https://api.example.com/login   Authorization: Bearer token123
Redirect:  302 → https://other-service.com/welcome

Forwarded: GET https://other-service.com/welcome   (no Authorization header)
```

Same-origin redirects preserve the `Authorization` header normally.

### HTTPS → HTTP Downgrade Protection

TurboHTTP blocks redirects that would downgrade from `https://` to `http://`. If a server tries to redirect you from an encrypted connection to a cleartext one, TurboHTTP fails the request rather than following it.

```
Original:  GET https://secure.example.com/data
Redirect:  301 → http://secure.example.com/data   ← blocked by default
```

This protects your traffic from being silently moved off an encrypted channel.

### Cookie Re-evaluation

The `Cookie` header is never blindly forwarded across redirects. For each redirect hop, TurboHTTP re-evaluates applicable cookies from the cookie jar using the new URL's domain, path, and security rules. This prevents cookies scoped to one site from being sent to a different one.

## Loop Detection

TurboHTTP tracks every URL visited during a redirect chain. If the same URL appears twice, it fails the request immediately rather than continuing. This prevents infinite redirect loops caused by misconfigured servers from hanging your application.

```
GET /a → 302 → /b → 302 → /a   ← request fails (loop detected)
```

## Configuration

Redirect behaviour is controlled via `.WithRedirect()` on the builder:

```csharp
// Enable redirects with defaults: max 10 hops, no HTTPS→HTTP downgrade
builder.Services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect();

// Custom limit
builder.Services.AddTurboHttpClient("strict", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect(redirect =>
{
    redirect.MaxRedirects = 5;
    redirect.AllowHttpsToHttpDowngrade = false;  // default
});
```

### `MaxRedirects`

The maximum number of redirect hops to follow before giving up. Default: **10**. Exceeding this limit causes the request to fail.

### `AllowHttpsToHttpDowngrade`

When `true`, allows HTTPS → HTTP redirects. Default: **false** (blocked).

This is rarely needed. Only enable it in fully-trusted internal networks where you control both endpoints and accept the risk of cleartext traffic. In most applications, leave this at the default.

### Disabling Redirects

Omit `.WithRedirect()` to leave redirects disabled entirely. All 3xx responses are returned as-is.

## Handling Redirect Failures

When a redirect cannot be completed — due to too many hops, a detected loop, or a blocked HTTPS→HTTP downgrade — the failure surfaces as an exception thrown from `SendAsync`. You catch and handle it using the standard exception handling:

```csharp
try
{
    var response = await client.SendAsync(request);
}
catch (Exception ex)
{
    Console.WriteLine($"Request failed: {ex.Message}");
}
```

The specific internal exception types are not part of the public API, so you cannot distinguish between different redirect failure modes by exception type. If your application needs to respond differently to different kinds of redirect failures, consider lowering the `MaxRedirects` limit or disabling redirects entirely (omit `.WithRedirect()`) and handling 3xx responses yourself.

::: info How it works
See [Architecture: Request Pipeline](/architecture/pipeline) to understand how this feature fits into the processing pipeline.
:::
