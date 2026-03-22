# Redirects

TurboHttp follows redirects automatically. When a server responds with a 3xx status code, TurboHttp builds a new request to the `Location` URL and re-sends it — transparently, within the same `SendAsync` call.

## How It Works

When a redirect response arrives, TurboHttp:

1. Validates the `Location` header (resolves relative URLs against the original request URI).
2. Checks for redirect loops and enforces the maximum hop limit.
3. Determines the new HTTP method and whether the request body should be carried forward.
4. Strips security-sensitive headers when the redirect crosses an origin.
5. Re-evaluates cookies for the new URL from the cookie jar.
6. Sends the new request and returns the final response.

The calling code always receives the final non-redirect response. Intermediate redirect hops are never surfaced.

## Status Code Behaviour

Different redirect status codes have different semantics for method rewriting and body handling:

| Status code | Name | Method | Body |
|-------------|------|--------|------|
| `301` | Moved Permanently | POST → GET; other methods unchanged | Dropped |
| `302` | Found | POST → GET; other methods unchanged | Dropped |
| `303` | See Other | Always GET | Always dropped |
| `307` | Temporary Redirect | Unchanged | Preserved |
| `308` | Permanent Redirect | Unchanged | Preserved |

**Key distinction:**

- **301 and 302** rewrite `POST` to `GET` (long-standing browser practice). A `PUT /resource` on 301 stays `PUT`. A `POST /submit` on 301 becomes `GET /submit`.
- **303** unconditionally rewrites to `GET` regardless of the original method. Use 303 when a `POST` submission should redirect to a results page.
- **307 and 308** are the strict variants — the method and body are always preserved. A `POST /upload` on 307 re-sends `POST /upload` to the new location.

## Security Behaviours

### Authorization Header Stripping

When a redirect crosses an origin (different scheme, host, or port), TurboHttp removes the `Authorization` header from the forwarded request. This prevents credentials from leaking to an unintended third-party server.

```
Original:  POST https://api.example.com/login   Authorization: Bearer token123
Redirect:  302 → https://other-service.com/welcome

Forwarded: GET https://other-service.com/welcome   (no Authorization header)
```

Same-origin redirects preserve the `Authorization` header.

### HTTPS → HTTP Downgrade Protection

TurboHttp blocks redirects that would downgrade from `https://` to `http://`. If a server responds with a redirect pointing to a plain HTTP URL, TurboHttp throws `RedirectException` with `RedirectError.ProtocolDowngrade` instead of following it.

```
Original:  GET https://secure.example.com/data
Redirect:  301 → http://secure.example.com/data   ← blocked by default
```

This protects against traffic being silently moved from an encrypted channel to a cleartext one.

### Cookie Re-evaluation

The `Cookie` header is never blindly forwarded across redirects. For each redirect hop, TurboHttp re-evaluates applicable cookies from the cookie jar using the new URL's domain, path, and `Secure` attribute rules. This prevents cookies scoped to one origin from being sent to a different one.

## Loop Detection

TurboHttp tracks every URL visited during a redirect chain. If the same URL appears twice, it throws `RedirectException` with `RedirectError.RedirectLoop` immediately rather than following the chain indefinitely.

```
GET /a → 302 → /b → 302 → /a   ← RedirectException (loop detected)
```

## Configuration

Redirect behaviour is controlled via `RedirectPolicy` on `TurboClientOptions`.

```csharp
var options = new TurboClientOptions
{
    RedirectPolicy = new RedirectPolicy
    {
        MaxRedirects = 5,
        AllowHttpsToHttpDowngrade = false,  // default
    }
};
```

### `MaxRedirects`

The maximum number of redirect hops to follow before giving up. Default: **10**. Exceeding this limit throws `RedirectException` with `RedirectError.MaxRedirectsExceeded`.

### `AllowHttpsToHttpDowngrade`

When `true`, allows HTTPS → HTTP redirects. Default: **false** (blocked). Only set this to `true` for specific internal environments where you control both endpoints.

### Disabling Redirects

Set `RedirectPolicy` to `null` to disable redirect following entirely. All 3xx responses are returned as-is.

```csharp
var options = new TurboClientOptions
{
    RedirectPolicy = null   // no redirect following
};
```

## Handling Redirect Exceptions

When the redirect limit is exceeded or a loop is detected, TurboHttp throws `RedirectException`:

```csharp
try
{
    var response = await client.SendAsync(request);
}
catch (RedirectException ex) when (ex.Error == RedirectError.MaxRedirectsExceeded)
{
    Console.WriteLine($"Too many redirects: {ex.Message}");
}
catch (RedirectException ex) when (ex.Error == RedirectError.RedirectLoop)
{
    Console.WriteLine($"Redirect loop detected: {ex.Message}");
}
catch (RedirectException ex) when (ex.Error == RedirectError.ProtocolDowngrade)
{
    Console.WriteLine($"Blocked HTTPS→HTTP downgrade: {ex.Message}");
}
```

Protocol downgrade errors use `RedirectError.ProtocolDowngrade`, so you can handle them independently with a `when` filter.
