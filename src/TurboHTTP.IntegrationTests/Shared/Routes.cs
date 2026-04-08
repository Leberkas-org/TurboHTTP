using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.IntegrationTests.Shared;

internal static class Routes
{
    internal static void RegisterRoutes(WebApplication app)
    {

        app.MapMethods("/hello", ["GET", "HEAD"], (HttpContext ctx) =>
        {
            if (ctx.Request.Method == "HEAD") return Results.NoContent();
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 11;
            return Results.Content("Hello World", "text/plain");
        });

        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));

        app.MapGet("/large/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

        app.MapGet("/status/{code:int}", async (HttpContext ctx, int code) =>
        {
            ctx.Response.StatusCode = code;
            if (code != 204 && code != 304)
            {
                var body = "ok"u8.ToArray();
                ctx.Response.ContentLength = body.Length;
                await ctx.Response.Body.WriteAsync(body);
            }
        });

        app.MapGet("/content/{*ct}", async (HttpContext ctx, string ct) =>
        {
            ctx.Response.ContentType = ct;
            var body = "body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/methods", (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        app.MapMethods("/any",
            ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"],
            (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));


        app.MapPost("/echo", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapPut("/echo", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapMethods("/echo", ["PATCH"], async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapGet("/headers/echo", (HttpContext ctx) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.Headers[header.Key] = header.Value;
                }
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/headers/set", (HttpContext ctx) =>
        {
            foreach (var param in ctx.Request.Query)
            {
                ctx.Response.Headers[param.Key] = param.Value;
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/headers/user-agent", (HttpContext ctx) =>
        {
            var value = ctx.Request.Headers.UserAgent.ToString();
            ctx.Response.ContentType = "text/plain";
            return Results.Text(value);
        });

        app.MapGet("/headers/count", (HttpContext ctx) =>
        {
            var count = ctx.Request.Headers.Count;
            ctx.Response.Headers["X-Header-Count"] = count.ToString();
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/auth", (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization"))
            {
                return Results.StatusCode(401);
            }

            return Results.Ok();
        });

        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });


        app.MapMethods("/chunked/{kb:int}", ["GET", "HEAD"], async (HttpContext ctx, int kb) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            if (ctx.Request.Method == "HEAD")
            {
                return;
            }

            const int chunkSize = 8192;
            var chunk = new byte[chunkSize];
            Array.Fill(chunk, (byte)'A');
            var remaining = kb * 1024;
            while (remaining > 0)
            {
                var toWrite = Math.Min(remaining, chunkSize);
                await ctx.Response.Body.WriteAsync(chunk.AsMemory(0, toWrite));
                await ctx.Response.Body.FlushAsync();
                remaining -= toWrite;
            }
        });

        app.MapGet("/chunked/exact/{count:int}/{chunkBytes:int}", async (HttpContext ctx, int count, int chunkBytes) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            var chunk = new byte[chunkBytes];
            Array.Fill(chunk, (byte)'B');
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(chunk);
                await ctx.Response.Body.FlushAsync();
            }
        });

        app.MapPost("/echo/chunked", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/chunked/trailer", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var body = "chunked-with-trailer"u8.ToArray();
            await ctx.Response.Body.WriteAsync(body);
            await ctx.Response.Body.FlushAsync();
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseTrailersFeature>() is
                { } trailersFeature)
            {
                trailersFeature.Trailers["X-Checksum"] = "abc123";
            }
        });

        app.MapGet("/chunked/md5", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            var body = "checksum-body"u8.ToArray();
            var md5 = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(body));
            ctx.Response.Headers.ContentMD5 = md5;
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapGet("/close", async ctx =>
        {
            ctx.Response.Headers.Connection = "close";
            ctx.Response.ContentType = "text/plain";
            var body = "closing"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapGet("/etag", async ctx =>
        {
            const string etag = "\"v1\"";
            if (ctx.Request.Headers.IfNoneMatch == etag)
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers.ETag = etag;
                return;
            }

            ctx.Response.Headers.ETag = etag;
            ctx.Response.ContentType = "text/plain";
            var body = "etag-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/cache", async ctx =>
        {
            ctx.Response.Headers.CacheControl = "max-age=3600, public";
            ctx.Response.Headers.LastModified = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
            ctx.Response.Headers.Expires = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.ContentType = "text/plain";
            var body = "cached-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        var fixedLastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        app.MapGet("/if-modified-since", async ctx =>
        {
            ctx.Response.Headers.LastModified = fixedLastModified.ToString("R");
            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ims) &&
                DateTimeOffset.TryParse(ims, out var imsDate) &&
                imsDate >= fixedLastModified)
            {
                ctx.Response.StatusCode = 304;
                return;
            }

            ctx.Response.ContentType = "text/plain";
            var body = "fresh-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapGet("/negotiate", (HttpContext ctx) =>
        {
            var accept = ctx.Request.Headers.Accept.ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Content("{\"ok\":true}", "application/json");
            }

            if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Content("<html><body>ok</body></html>", "text/html");
            }

            return Results.Content("default", "text/plain");
        });

        app.MapGet("/negotiate/vary", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Vary = "Accept";
            return Results.Content("data", "text/plain");
        });

        app.MapGet("/gzip-meta", async ctx =>
        {
            ctx.Response.Headers.ContentEncoding = "identity";
            ctx.Response.ContentType = "text/plain";
            var body = "encoded-body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapPost("/form/multipart", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        app.MapPost("/form/urlencoded", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            ctx.Response.ContentType = "text/plain";
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });


        app.MapGet("/range/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }

            return Results.Bytes(body, "application/octet-stream", enableRangeProcessing: true);
        });

        const string rangeEtag = "\"range-v1\"";
        app.MapGet("/range/etag", (HttpContext _) =>
        {
            var body = new byte[512];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }

            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(rangeEtag);
            return Results.Bytes(body, "application/octet-stream",
                entityTag: entityTag,
                enableRangeProcessing: true);
        });


        app.MapGet("/slow/{count:int}", async (HttpContext ctx, int count) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync();
            var single = new[] { (byte)'x' };
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(single);
                await ctx.Response.Body.FlushAsync();
                await Task.Delay(1);
            }
        });

        app.MapGet("/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });


        app.MapGet("/empty-cl", (HttpContext ctx) =>
        {
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        app.MapGet("/unknown-headers", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Unknown-Foo"] = "bar";
            ctx.Response.Headers["X-Unknown-Bar"] = "baz";
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 2;
            return Results.Content("ok", "text/plain");
        });

        app.MapGet("/edge/close-mid-response", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 10000;
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync("partial"u8.ToArray());
            await ctx.Response.Body.FlushAsync();
            ctx.Abort();
        });

        app.MapGet("/edge/large-header/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var headerValue = new string('X', kb * 1024);
            ctx.Response.Headers["X-Large-Header"] = headerValue;
            ctx.Response.ContentType = "text/plain";
            var body = "ok"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/edge/unknown-encoding", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "x-custom";
            var body = "raw-payload"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/edge/empty-body", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 0;
            await ctx.Response.StartAsync();
        });
    }

    internal static void RegisterH2Routes(WebApplication app)
    {
        app.MapGet("/h2/settings", (HttpContext _) => Results.Content("h2-ok", "text/plain"));

        app.MapGet("/h2/many-headers", (HttpContext ctx) =>
        {
            for (var i = 0; i < 20; i++)
            {
                ctx.Response.Headers[$"X-Custom-{i:D3}"] = $"value-{i:D3}";
            }

            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 12;
            return Results.Content("many-headers", "text/plain");
        });

        app.MapPost("/h2/echo-binary", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/h2/cookie", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", "session=abc123; Path=/; HttpOnly");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 10;
            return Results.Content("cookie-set", "text/plain");
        });

        app.MapGet("/h2/large-headers/{kb:int}", (HttpContext ctx, int kb) =>
        {
            for (var i = 0; i < 10; i++)
            {
                ctx.Response.Headers[$"X-Large-{i:D2}"] = new string('v', 90);
            }

            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

        app.MapGet("/h2/priority/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'P');
            return Results.Bytes(body, "application/octet-stream");
        });

        app.MapGet("/h2/echo-path", (HttpContext ctx) =>
        {
            var path = ctx.Request.Path + ctx.Request.QueryString;
            return Results.Content(path, "text/plain");
        });

        app.MapGet("/h2/settings/max-concurrent", (HttpContext ctx) =>
        {
            var streamId = ctx.Request.Headers["X-Stream-Id"].ToString();
            ctx.Response.Headers["X-Stream-Id"] = streamId;
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });
    }

    internal static void RegisterH3Routes(WebApplication app)
    {
        app.MapGet("/h3/settings", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Protocol"] = ctx.Request.Protocol;
            return Results.Content("h3-ok", "text/plain");
        });

        app.MapGet("/h3/protocol", (HttpContext ctx) =>
            Results.Content(ctx.Request.Protocol, "text/plain"));

        app.MapGet("/h3/many-headers", (HttpContext ctx) =>
        {
            for (var i = 0; i < 20; i++)
            {
                ctx.Response.Headers[$"X-Custom-{i:D3}"] = $"value-{i:D3}";
            }

            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 12;
            return Results.Content("many-headers", "text/plain");
        });

        app.MapPost("/h3/echo-binary", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/h3/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        app.MapGet("/h3/stream/{count:int}", async (HttpContext ctx, int count) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.StartAsync();
            var single = new[] { (byte)'x' };
            for (var i = 0; i < count; i++)
            {
                await ctx.Response.Body.WriteAsync(single);
                await ctx.Response.Body.FlushAsync();
            }
        });
    }

    internal static void RegisterCookieRoutes(WebApplication app)
    {
        // GET /cookie/set/{name}/{value} → Set-Cookie: {name}={value}; Path=/
        app.MapGet("/cookie/set/{name}/{value}", (HttpContext ctx, string name, string value) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/");
            return Results.Content("cookie-set", "text/plain");
        });

        // GET /cookie/set-secure/{name}/{value} → Set-Cookie with Secure flag
        app.MapGet("/cookie/set-secure/{name}/{value}", (HttpContext ctx, string name, string value) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/; Secure");
            return Results.Content("cookie-set-secure", "text/plain");
        });

        // GET /cookie/set-httponly/{name}/{value} → Set-Cookie with HttpOnly flag
        app.MapGet("/cookie/set-httponly/{name}/{value}", (HttpContext ctx, string name, string value) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/; HttpOnly");
            return Results.Content("cookie-set-httponly", "text/plain");
        });

        // GET /cookie/set-samesite/{name}/{value}/{policy} → Set-Cookie with SameSite
        app.MapGet("/cookie/set-samesite/{name}/{value}/{policy}",
            (HttpContext ctx, string name, string value, string policy) =>
            {
                ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/; SameSite={policy}");
                return Results.Content("cookie-set-samesite", "text/plain");
            });

        // GET /cookie/set-expires/{name}/{value}/{seconds} → Set-Cookie with Max-Age
        app.MapGet("/cookie/set-expires/{name}/{value}/{seconds:int}",
            (HttpContext ctx, string name, string value, int seconds) =>
            {
                ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/; Max-Age={seconds}");
                return Results.Content("cookie-set-expires", "text/plain");
            });

        // GET /cookie/set-domain/{name}/{value}/{domain} → Set-Cookie with Domain
        app.MapGet("/cookie/set-domain/{name}/{value}/{domain}",
            (HttpContext ctx, string name, string value, string domain) =>
            {
                ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/; Domain={domain}");
                return Results.Content("cookie-set-domain", "text/plain");
            });

        // GET /cookie/set-path/{name}/{value}/{*path} → Set-Cookie with Path
        // Uses catch-all for path to support slashes in path values
        app.MapGet("/cookie/set-path/{name}/{value}/{*path}",
            (HttpContext ctx, string name, string value, string path) =>
            {
                ctx.Response.Headers.Append("Set-Cookie", $"{name}={value}; Path=/{path}");
                return Results.Content("cookie-set-path", "text/plain");
            });

        // GET /cookie/echo → returns all received Cookie headers as JSON body
        app.MapGet("/cookie/echo", (HttpContext ctx) =>
        {
            var cookies = new Dictionary<string, string>();
            foreach (var cookie in ctx.Request.Cookies)
            {
                cookies[cookie.Key] = cookie.Value;
            }

            var json = JsonSerializer.Serialize(cookies);
            return Results.Content(json, "application/json");
        });

        // GET /cookie/set-multiple → multiple Set-Cookie headers
        app.MapGet("/cookie/set-multiple", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", "alpha=one; Path=/");
            ctx.Response.Headers.Append("Set-Cookie", "beta=two; Path=/");
            ctx.Response.Headers.Append("Set-Cookie", "gamma=three; Path=/");
            return Results.Content("cookie-set-multiple", "text/plain");
        });

        // GET /cookie/delete/{name} → Set-Cookie with Max-Age=0
        app.MapGet("/cookie/delete/{name}", (HttpContext ctx, string name) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"{name}=; Path=/; Max-Age=0");
            return Results.Content("cookie-deleted", "text/plain");
        });

        // GET /cookie/set-and-redirect → Set-Cookie + 302 redirect to /cookie/echo
        // Used to verify cookies persist across redirects
        app.MapGet("/cookie/set-and-redirect", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", "redirect_cookie=from-redirect; Path=/");
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/cookie/echo";
            return Results.Empty;
        });
    }

    internal static void RegisterRedirectRoutes(WebApplication app)
    {
        // GET /redirect/{code}/{target} → responds with status {code}, Location: {target}
        app.MapGet("/redirect/{code:int}/{*target}", (HttpContext ctx, int code, string target) =>
        {
            ctx.Response.StatusCode = code;
            ctx.Response.Headers.Location = "/" + target;
            return Results.Empty;
        });

        // GET /redirect/chain/{n} → chain of n redirects ending at /hello
        // /redirect/chain/3 → 302 Location: /redirect/chain/2
        // /redirect/chain/1 → 302 Location: /hello
        app.MapGet("/redirect/chain/{n:int}", (HttpContext ctx, int n) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = n <= 1 ? "/hello" : $"/redirect/chain/{n - 1}";

            return Results.Empty;
        });

        // GET /redirect/loop → infinite redirect loop (redirects back to itself)
        app.MapGet("/redirect/loop", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/redirect/loop";
            return Results.Empty;
        });

        // GET /redirect/relative → redirect with relative Location header
        app.MapGet("/redirect/relative", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "../hello";
            return Results.Empty;
        });

        // GET /redirect/cross-scheme → HTTPS → HTTP downgrade redirect
        app.MapGet("/redirect/cross-scheme", (HttpContext ctx) =>
        {
            var port = ctx.Connection.LocalPort;
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = $"http://127.0.0.1:{port}/hello";
            return Results.Empty;
        });

        // POST /redirect/307 → 307 preserves method & body
        // Redirects to POST /echo so the body is echoed back
        app.MapPost("/redirect/307", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 307;
            ctx.Response.Headers.Location = "/echo";
            return Results.Empty;
        });

        // POST /redirect/303 → 303 changes to GET
        // Redirects to GET /hello (303 forces GET)
        app.MapPost("/redirect/303", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 303;
            ctx.Response.Headers.Location = "/hello";
            return Results.Empty;
        });

        // POST /redirect/302 → 302 rewrites POST to GET
        // Redirects to GET /hello (302 converts POST to GET per RFC 9110)
        app.MapPost("/redirect/302", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = "/hello";
            return Results.Empty;
        });

        // POST /redirect/308 → 308 preserves method & body
        // Redirects to POST /echo so the body is echoed back
        app.MapPost("/redirect/308", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 308;
            ctx.Response.Headers.Location = "/echo";
            return Results.Empty;
        });

        // GET /redirect/cross-scheme/{code} → HTTPS→HTTP downgrade with given status code
        app.MapGet("/redirect/cross-scheme/{code:int}", (HttpContext ctx, int code) =>
        {
            var port = ctx.Connection.LocalPort;
            ctx.Response.StatusCode = code;
            ctx.Response.Headers.Location = $"http://127.0.0.1:{port}/hello";
            return Results.Empty;
        });

        // GET /redirect/cross-origin → 302 to http://127.0.0.1:{port}/headers/echo
        // Used to test cross-origin Authorization header stripping
        // (client connects via localhost, redirect goes to 127.0.0.1 = different origin)
        app.MapGet("/redirect/cross-origin", (HttpContext ctx) =>
        {
            var port = ctx.Connection.LocalPort;
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = $"http://127.0.0.1:{port}/headers/echo";
            return Results.Empty;
        });

        // GET /redirect/cross-origin-auth → 302 to http://127.0.0.1:{port}/auth
        // Used to test cross-origin Authorization header stripping via /auth (returns 401 if no Auth)
        app.MapGet("/redirect/cross-origin-auth", (HttpContext ctx) =>
        {
            var port = ctx.Connection.LocalPort;
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = $"http://127.0.0.1:{port}/auth";
            return Results.Empty;
        });
    }

    /// <summary>Server-side request counter for /retry/succeed-after/{n} routes.</summary>
    private static readonly ConcurrentDictionary<string, int> _retryCounters = new();

    internal static void RegisterRetryRoutes(WebApplication app)
    {
        // GET /retry/408 → 408 Request Timeout
        app.MapGet("/retry/408", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 408;
            return Results.Empty;
        });

        // GET|HEAD /retry/503 → 503 Service Unavailable
        app.MapMethods("/retry/503", ["GET", "HEAD"], (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 503;
            return Results.Empty;
        });

        // GET /retry/503-retry-after/{seconds} → 503 with Retry-After header (seconds)
        app.MapGet("/retry/503-retry-after/{seconds:int}", (HttpContext ctx, int seconds) =>
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Headers.RetryAfter = seconds.ToString();
            return Results.Empty;
        });

        // GET /retry/503-retry-after-date → 503 with Retry-After as HTTP-date (10 seconds from now)
        app.MapGet("/retry/503-retry-after-date", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Headers.RetryAfter = DateTimeOffset.UtcNow.AddSeconds(10).ToString("R");
            return Results.Empty;
        });

        // GET /retry/succeed-after/{n} → fail first N-1 times with 503, then 200
        // Uses a query parameter ?key={unique} or the path itself as the counter key.
        // Each unique key tracks its own counter independently.
        app.MapGet("/retry/succeed-after/{n:int}", async (HttpContext ctx, int n) =>
        {
            var key = ctx.Request.Query.ContainsKey("key")
                ? ctx.Request.Query["key"].ToString()
                : $"{ctx.Connection.RemoteIpAddress}:{ctx.Connection.RemotePort}:{n}";

            var count = _retryCounters.AddOrUpdate(key, 1, (_, prev) => prev + 1);

            if (count >= n)
            {
                // Reset counter for future test runs
                _retryCounters.TryRemove(key, out _);
                ctx.Response.ContentType = "text/plain";
                var body = "success"u8.ToArray();
                ctx.Response.ContentLength = body.Length;
                await ctx.Response.Body.WriteAsync(body);
            }
            else
            {
                ctx.Response.StatusCode = 503;
            }
        });

        // PUT /retry/503 → 503 Service Unavailable (idempotent — should be retried)
        app.MapPut("/retry/503", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 503;
            return Results.Empty;
        });

        // DELETE /retry/503 → 503 Service Unavailable (idempotent — should be retried)
        app.MapDelete("/retry/503", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 503;
            return Results.Empty;
        });

        // POST /retry/non-idempotent-503 → 503 on POST (should NOT be retried)
        app.MapPost("/retry/non-idempotent-503", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 503;
            return Results.Empty;
        });
    }

    internal static void RegisterCacheRoutes(WebApplication app)
    {
        // GET /cache/max-age/{seconds} → Cache-Control: max-age={seconds}, body = timestamp
        app.MapGet("/cache/max-age/{seconds:int}", async (HttpContext ctx, int seconds) =>
        {
            ctx.Response.Headers.CacheControl = $"max-age={seconds}";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/no-cache → Cache-Control: no-cache
        app.MapGet("/cache/no-cache", async ctx =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/no-store → Cache-Control: no-store
        app.MapGet("/cache/no-store", async ctx =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.ContentType = "text/plain";
            var body = "no-store-resource"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/etag/{id} → ETag header, supports If-None-Match → 304
        app.MapGet("/cache/etag/{id}", async (HttpContext ctx, string id) =>
        {
            var etag = $"\"{id}\"";
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag)
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers.ETag = etag;
                return;
            }

            ctx.Response.Headers.ETag = etag;
            ctx.Response.Headers.CacheControl = "max-age=3600";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes($"etag-resource-{id}");
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/last-modified/{id} → Last-Modified, supports If-Modified-Since → 304
        // Uses a fixed date per id (2026-01-01 + id hash hours) for deterministic testing
        app.MapGet("/cache/last-modified/{id}", async (HttpContext ctx, string id) =>
        {
            var lastModified = new DateTimeOffset(2026, 1, 1, Math.Abs(id.GetHashCode()) % 24, 0, 0, TimeSpan.Zero);
            ctx.Response.Headers.LastModified = lastModified.ToString("R");
            ctx.Response.Headers.CacheControl = "max-age=3600";

            if (ctx.Request.Headers.TryGetValue("If-Modified-Since", out var ims) &&
                DateTimeOffset.TryParse(ims, out var imsDate) &&
                imsDate >= lastModified)
            {
                ctx.Response.StatusCode = 304;
                return;
            }

            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes($"last-modified-resource-{id}");
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/vary/{header} → Vary: {header}, body changes based on header value
        app.MapGet("/cache/vary/{header}", async (HttpContext ctx, string header) =>
        {
            ctx.Response.Headers.Vary = header;
            ctx.Response.Headers.CacheControl = "max-age=3600";
            ctx.Response.ContentType = "text/plain";
            var headerValue = ctx.Request.Headers[header].ToString();
            var body = System.Text.Encoding.UTF8.GetBytes($"vary-{header}:{headerValue}");
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/must-revalidate → Cache-Control: max-age=0, must-revalidate
        app.MapGet("/cache/must-revalidate", async ctx =>
        {
            const string etag = "\"must-rev-1\"";
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag)
            {
                ctx.Response.StatusCode = 304;
                ctx.Response.Headers.ETag = etag;
                ctx.Response.Headers.CacheControl = "max-age=0, must-revalidate";
                return;
            }

            ctx.Response.Headers.ETag = etag;
            ctx.Response.Headers.CacheControl = "max-age=0, must-revalidate";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/s-maxage/{seconds} → Cache-Control: s-maxage={seconds}
        app.MapGet("/cache/s-maxage/{seconds:int}", async (HttpContext ctx, int seconds) =>
        {
            ctx.Response.Headers.CacheControl = $"s-maxage={seconds}";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/expires → Expires header (absolute date, 1 hour from now)
        app.MapGet("/cache/expires", async ctx =>
        {
            ctx.Response.Headers.Expires = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /cache/private → Cache-Control: private
        app.MapGet("/cache/private", async ctx =>
        {
            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            ctx.Response.ContentType = "text/plain";
            var body = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });
    }

    internal static void RegisterContentEncodingRoutes(WebApplication app)
    {
        // GET /compress/gzip/{kb} → gzip-compressed response
        app.MapGet("/compress/gzip/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var payload = GeneratePayload(kb);
            var compressed = CompressGzip(payload);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "gzip";
            ctx.Response.ContentLength = compressed.Length;
            await ctx.Response.Body.WriteAsync(compressed);
        });

        // GET /compress/deflate/{kb} → deflate-compressed response
        app.MapGet("/compress/deflate/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var payload = GeneratePayload(kb);
            var compressed = CompressDeflate(payload);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "deflate";
            ctx.Response.ContentLength = compressed.Length;
            await ctx.Response.Body.WriteAsync(compressed);
        });

        // GET /compress/br/{kb} → brotli-compressed response
        app.MapGet("/compress/br/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var payload = GeneratePayload(kb);
            var compressed = CompressBrotli(payload);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "br";
            ctx.Response.ContentLength = compressed.Length;
            await ctx.Response.Body.WriteAsync(compressed);
        });

        // GET /compress/identity/{kb} → no compression (control)
        app.MapGet("/compress/identity/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var payload = GeneratePayload(kb);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = payload.Length;
            await ctx.Response.Body.WriteAsync(payload);
        });

        // GET /compress/negotiate → honors Accept-Encoding, responds with matching encoding
        app.MapGet("/compress/negotiate", async ctx =>
        {
            var payload = GeneratePayload(1); // 1 KB default
            var acceptEncoding = ctx.Request.Headers.AcceptEncoding.ToString();

            byte[] body;
            string encoding;

            if (acceptEncoding.Contains("br"))
            {
                body = CompressBrotli(payload);
                encoding = "br";
            }
            else if (acceptEncoding.Contains("gzip"))
            {
                body = CompressGzip(payload);
                encoding = "gzip";
            }
            else if (acceptEncoding.Contains("deflate"))
            {
                body = CompressDeflate(payload);
                encoding = "deflate";
            }
            else
            {
                body = payload;
                encoding = "identity";
            }

            ctx.Response.ContentType = "text/plain";
            if (encoding != "identity")
            {
                ctx.Response.Headers.ContentEncoding = encoding;
            }

            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });
        return;

        // Helper: generate a deterministic payload of the given size in KB
        static byte[] GeneratePayload(int kb)
        {
            var size = kb * 1024;
            var data = new byte[size];
            // Fill with repeating ASCII pattern for good compressibility
            for (var i = 0; i < size; i++)
            {
                data[i] = (byte)('A' + i % 26);
            }

            return data;
        }

        // Helper: compress with gzip
        static byte[] CompressGzip(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
            {
                gz.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }

        // Helper: compress with deflate
        static byte[] CompressDeflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest))
            {
                ds.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }

        // Helper: compress with brotli
        static byte[] CompressBrotli(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var bs = new BrotliStream(ms, CompressionLevel.Fastest))
            {
                bs.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }
    }

    internal static void RegisterErrorHandlingRoutes(WebApplication app)
    {
        // GET /h2/abort → aborts the request, triggering RST_STREAM on the HTTP/2 stream
        app.MapGet("/h2/abort", (HttpContext ctx) =>
        {
            ctx.Abort();
            return Results.Empty;
        });

        // GET /h2/delay/{ms} → delays response by ms milliseconds, then returns 200
        app.MapGet("/h2/delay/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms);
            ctx.Response.ContentType = "text/plain";
            var body = "delayed"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });
    }

    internal static void RegisterConnectionReuseRoutes(WebApplication app)
    {
        // GET /conn/keep-alive → explicit Connection: Keep-Alive header (HTTP/1.0 style)
        app.MapGet("/conn/keep-alive", async ctx =>
        {
            ctx.Response.Headers.Connection = "Keep-Alive";
            ctx.Response.ContentType = "text/plain";
            var body = "keep-alive"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /conn/close → explicit Connection: close header
        app.MapGet("/conn/close", async ctx =>
        {
            ctx.Response.Headers.Connection = "close";
            ctx.Response.ContentType = "text/plain";
            var body = "closing"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /conn/default → no Connection header (HTTP/1.1 default keep-alive)
        app.MapGet("/conn/default", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            var body = "default"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /conn/upgrade-101 → 101 Switching Protocols (connection not reusable)
        app.MapGet("/conn/upgrade-101", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 101;
            ctx.Response.Headers.Upgrade = "websocket";
            ctx.Response.Headers.Connection = "Upgrade";
            return Results.Empty;
        });
    }

    internal static void RegisterResilienceRoutes(WebApplication app)
    {
        // GET /resilience/content-length-mismatch → Content-Length: 1000 but sends only 500 bytes, then closes
        app.MapGet("/resilience/content-length-mismatch", async ctx =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 1000;
            await ctx.Response.Body.WriteAsync(new byte[500]);
            await ctx.Response.Body.FlushAsync();
            ctx.Abort();
        });

        // GET /resilience/corrupt-gzip → Content-Encoding: gzip but body is random bytes
        app.MapGet("/resilience/corrupt-gzip", async ctx =>
        {
            var body = new byte[256];
            Random.Shared.NextBytes(body);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "gzip";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /resilience/corrupt-br → Content-Encoding: br but body is random bytes
        app.MapGet("/resilience/corrupt-br", async ctx =>
        {
            var body = new byte[256];
            Random.Shared.NextBytes(body);
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "br";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /resilience/truncated-body/{kb} → sends kb KB Content-Length header, stops at 50%
        app.MapGet("/resilience/truncated-body/{kb:int}", async (HttpContext ctx, int kb) =>
        {
            var totalSize = kb * 1024;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = totalSize;
            await ctx.Response.Body.WriteAsync(new byte[totalSize / 2]);
            await ctx.Response.Body.FlushAsync();
            ctx.Abort();
        });

        // GET /resilience/slow-headers/{ms} → delay ms before sending headers
        app.MapGet("/resilience/slow-headers/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            await Task.Delay(ms, ctx.RequestAborted);
            ctx.Response.ContentType = "text/plain";
            var body = "slow-headers"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /resilience/slow-body/{ms} → sends first half of body, delays ms, then sends rest
        app.MapGet("/resilience/slow-body/{ms:int}", async (HttpContext ctx, int ms) =>
        {
            var body = "slow-body-first-half-||-slow-body-second-half"u8.ToArray();
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.StartAsync();
            var half = body.Length / 2;
            await ctx.Response.Body.WriteAsync(body.AsMemory(0, half));
            await ctx.Response.Body.FlushAsync();
            await Task.Delay(ms, ctx.RequestAborted);
            await ctx.Response.Body.WriteAsync(body.AsMemory(half));
            await ctx.Response.Body.FlushAsync();
        });

        // GET /resilience/invalid-header → response with header containing unusual characters
        app.MapGet("/resilience/invalid-header", async ctx =>
        {
            ctx.Response.Headers["X-Invalid"] = "value\twith\ttabs";
            ctx.Response.ContentType = "text/plain";
            var body = "invalid-header"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /resilience/empty-response → closes connection immediately (no status line)
        app.MapGet("/resilience/empty-response", (HttpContext ctx) =>
        {
            ctx.Abort();
            return Results.Empty;
        });
    }

    internal static void RegisterRequestCompressionRoutes(WebApplication app)
    {
        // POST /compress/echo → reads request body, echoes back uncompressed
        // Returns X-Content-Encoding header showing what Content-Encoding the server received
        app.MapPost("/compress/echo", async ctx =>
        {
            var receivedEncoding = ctx.Request.Headers.ContentEncoding.ToString();
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.Headers["X-Content-Encoding"] = string.IsNullOrEmpty(receivedEncoding)
                ? "identity"
                : receivedEncoding;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // POST /compress/verify-gzip → verifies body is valid gzip, decompresses, echoes
        app.MapPost("/compress/verify-gzip", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            ms.Position = 0;
            using var decompressed = new MemoryStream();
            await using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            {
                await gz.CopyToAsync(decompressed);
            }

            var body = decompressed.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // POST /compress/verify-deflate → verifies body is valid zlib-deflate, decompresses, echoes
        // Uses ZLibStream because the TurboHttp client compresses deflate as zlib-wrapped deflate (RFC 1950).
        app.MapPost("/compress/verify-deflate", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            ms.Position = 0;
            using var decompressed = new MemoryStream();
            await using (var ds = new ZLibStream(ms, CompressionMode.Decompress))
            {
                await ds.CopyToAsync(decompressed);
            }

            var body = decompressed.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // POST /compress/verify-br → verifies body is valid brotli, decompresses, echoes
        app.MapPost("/compress/verify-br", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            ms.Position = 0;
            using var decompressed = new MemoryStream();
            await using (var bs = new BrotliStream(ms, CompressionMode.Decompress))
            {
                await bs.CopyToAsync(decompressed);
            }

            var body = decompressed.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });
    }

    internal static void RegisterExpectContinueRoutes(WebApplication app)
    {
        // POST /expect/echo → reads Expect: 100-continue, Kestrel sends 100 Continue
        // automatically when the app reads the request body, then echoes body with 200
        app.MapPost("/expect/echo", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // POST /expect/reject → responds 417 Expectation Failed without reading body
        // Setting status before reading body prevents Kestrel from sending 100 Continue
        app.MapPost("/expect/reject", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 417;
            return Results.Empty;
        });

        // POST /expect/large → accepts large body (>1KB) with 100-continue flow
        // Same as /expect/echo but explicitly designed for large payloads
        app.MapPost("/expect/large", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });
    }

    internal static void RegisterInteractionRoutes(WebApplication app)
    {
        // GET /interaction/cache-gzip → gzip-compressed body + Cache-Control: max-age=3600
        app.MapGet("/interaction/cache-gzip", async ctx =>
        {
            var payload = "interaction-cache-gzip-payload"u8.ToArray();
            using var ms = new MemoryStream();
            await using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
            {
                gz.Write(payload, 0, payload.Length);
            }

            var compressed = ms.ToArray();
            ctx.Response.Headers.CacheControl = "max-age=3600";
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Headers.ContentEncoding = "gzip";
            ctx.Response.ContentLength = compressed.Length;
            await ctx.Response.Body.WriteAsync(compressed);
        });

        // GET /interaction/cookie-hop/{hop:int}
        // Hop 1 → sets hop1=val1 + 302 → /interaction/cookie-hop/2
        // Hop 2 → sets hop2=val2 + 302 → /interaction/cookie-hop/3
        // Hop 3 → sets hop3=val3 + 302 → /cookie/echo
        app.MapGet("/interaction/cookie-hop/{hop:int}", (HttpContext ctx, int hop) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"hop{hop}=val{hop}; Path=/");
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = hop < 3
                ? $"/interaction/cookie-hop/{hop + 1}"
                : "/cookie/echo";
            return Results.Empty;
        });

        // GET /interaction/redirect-succeed-after/{n:int}/{key}
        // → 302 redirect to /retry/succeed-after/{n}?key={key}
        app.MapGet("/interaction/redirect-succeed-after/{n:int}/{key}", (HttpContext ctx, int n, string key) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Location = $"/retry/succeed-after/{n}?key={key}";
            return Results.Empty;
        });

        // GET /interaction/echo-all-headers → echoes X-* request headers as response headers
        // Cookie header echoed as X-Received-Cookie (for handler + cookie pipeline interaction test)
        app.MapGet("/interaction/echo-all-headers", (HttpContext ctx) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.Headers[header.Key] = header.Value;
                }
            }

            if (ctx.Request.Headers.TryGetValue("Cookie", out var cookie))
            {
                ctx.Response.Headers["X-Received-Cookie"] = cookie;
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });
    }
}