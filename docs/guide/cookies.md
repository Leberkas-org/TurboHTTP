# Cookie Management

TurboHttp handles cookies automatically. When a server sends a `Set-Cookie` header, TurboHttp stores it and attaches it to subsequent requests that match the cookie's domain and path — no configuration needed.

## How It Works

The cookie lifecycle has two steps:

1. **Store** — after every response, TurboHttp scans for `Set-Cookie` headers and adds matching cookies to an internal `CookieJar`.
2. **Inject** — before every outgoing request, TurboHttp checks the jar for applicable cookies and adds them to the `Cookie` request header.

Both steps happen transparently inside the request pipeline. Cookies from a login response are automatically sent on the very next request to the same domain.

## Cookie Isolation

Each `TurboHttpClient` instance has its own `CookieJar`. Cookies received by one client are never shared with another. This means:

- A client used for API calls and a client used for authentication do **not** share cookie state.
- Creating multiple clients for different services keeps their session cookies completely separate.

```csharp
// These two clients have independent cookie jars
var apiClient = new TurboHttpClient(options, system);
var authClient = new TurboHttpClient(options, system);
```

## Domain Matching

A cookie is only sent to the domain it was set for. TurboHttp uses proper label-boundary matching — a cookie for `example.com` does not match `notexample.com`.

- **Host-only cookies** (no `Domain` attribute) — sent only to the exact host that set them.
- **Domain cookies** (`Domain=example.com`) — sent to `example.com` and all subdomains (`api.example.com`, `auth.example.com`, etc.).

```
Set-Cookie: token=abc123                   → host-only: api.example.com only
Set-Cookie: session=xyz; Domain=example.com → domain: example.com + all subdomains
```

## Path Matching

A cookie is only sent to URLs whose path starts with the cookie's path. More specific paths take priority — if two cookies match, the one with the longer path is sent first.

```
Set-Cookie: pref=dark; Path=/settings  → sent to /settings and /settings/theme, not to /api
Set-Cookie: session=xyz; Path=/        → sent to every path
```

## Cookie Attributes

### `Secure`

The cookie is only sent over HTTPS connections. Cookies marked `Secure` are silently withheld on plain `http://` requests.

```
Set-Cookie: token=abc; Secure   ← sent on https://, not http://
```

**Practical impact:** Always use `Secure` for session tokens and authentication cookies in production.

### `HttpOnly`

Marks a cookie as server-only — it cannot be read by client-side scripts. TurboHttp stores and sends `HttpOnly` cookies normally; the attribute is informational for browsers.

```
Set-Cookie: session=xyz; HttpOnly
```

**Practical impact:** `HttpOnly` cookies are stored in the jar and injected into requests just like any other cookie.

### `SameSite`

Controls whether a cookie is sent with cross-site requests. TurboHttp stores the `SameSite` attribute but does **not** enforce it — the library always sends cookies that match domain and path rules. SameSite enforcement is a browser-level protection that does not apply to programmatic HTTP clients.

| Value | Meaning |
|-------|---------|
| `Strict` | Cookie sent only for requests originating from the same site |
| `Lax` | Cookie sent for same-site and top-level cross-site navigations |
| `None` | Cookie sent with all requests (requires `Secure`) |
| _(absent)_ | No policy; treated like `Lax` in browsers |

## Expiration

A cookie's lifetime is controlled by two attributes. `Max-Age` takes precedence over `Expires` when both are present.

### `Max-Age`

Lifetime in seconds from the time the response was received.

```
Set-Cookie: promo=sale; Max-Age=3600   ← expires in 1 hour
Set-Cookie: cart=empty; Max-Age=0      ← deleted immediately
```

### `Expires`

Absolute expiry date in HTTP-date format.

```
Set-Cookie: pref=dark; Expires=Fri, 20 Jun 2026 12:00:00 GMT
```

### Session cookies

A cookie with no `Max-Age` and no `Expires` is a **session cookie** — it lives for the duration of the current client instance and is discarded when the `TurboHttpClient` is disposed.

```
Set-Cookie: sid=abc123   ← no expiry: lasts until the client is disposed
```

## Working with `CookieJar` Directly

`CookieJar` is a public class. You can construct one independently to pre-populate cookies, test cookie matching logic, or share a jar across request processing outside the pipeline.

```csharp
using TurboHttp.Protocol.Cookies;

var jar = new CookieJar();

// Simulate a server response that sets a cookie
var response = new HttpResponseMessage();
response.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/; Secure");
jar.ProcessResponse(new Uri("https://api.example.com"), response);

Console.WriteLine($"Cookies stored: {jar.Count}"); // → 1

// Inspect what would be injected into a request
var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
jar.AddCookiesToRequest(new Uri("https://api.example.com/data"), ref request);
// request.Headers["Cookie"] now contains "session=abc123"

// Clear all stored cookies (e.g., on logout)
jar.Clear();
Console.WriteLine($"Cookies after clear: {jar.Count}"); // → 0
```

### Clearing cookies on logout

Call `Clear()` on the jar when a user logs out to remove all stored cookies:

```csharp
// After a successful logout response:
jar.Clear();
```

Since the per-client `CookieJar` is managed internally by the pipeline, clearing it in tests or custom integrations requires direct access to the jar instance used at construction time.
