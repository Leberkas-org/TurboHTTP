using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.IntegrationTests.Client.Shared;

internal static class HttpbinEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/get", HandleGet);
        app.MapPost("/post", HandlePost);
        app.MapPut("/put", HandlePut);
        app.MapPatch("/patch", HandlePatch);
        app.MapDelete("/delete", HandleDelete);
        app.MapGet("/headers", HandleHeaders);
        app.MapGet("/status/{code}", HandleStatus);
        app.MapGet("/bytes/{n}", HandleBytes);
        app.MapGet("/cookies", HandleGetCookies);
        app.MapGet("/cookies/set", HandleSetCookies);
        app.MapGet("/redirect/{n}", HandleRedirect);
        app.MapGet("/redirect-to", HandleRedirectTo);
        app.MapGet("/basic-auth/{user}/{pass}", HandleBasicAuth);
        app.MapGet("/cache", HandleCache);
        app.MapGet("/cache/{seconds}", HandleCacheWithSeconds);
        app.MapGet("/etag/{value}", HandleEtag);
        app.MapGet("/response-headers", HandleResponseHeaders);
        app.MapGet("/stream/{n:int}", HandleStream);
        app.MapGet("/gzip", HandleGzip);
        app.MapGet("/deflate", HandleDeflate);
        app.MapGet("/delay/{n:int}", HandleDelay);
        app.MapGet("/unstable", HandleUnstable);
        app.MapGet("/stream-bytes/{n:int}", HandleStreamBytes);
        app.MapGet("/drip", HandleDrip);
        app.MapGet("/range/{n:int}", HandleRange);
        app.MapGet("/absolute-redirect/{n:int}", HandleAbsoluteRedirect);
        app.MapGet("/relative-redirect/{n:int}", HandleRelativeRedirect);
        app.MapGet("/cookies/delete", HandleDeleteCookies);
        app.MapGet("/bearer", HandleBearer);
        app.MapGet("/sse/simple", HandleSseSimple);
        app.MapGet("/sse/typed", HandleSseTyped);
        app.MapGet("/sse/multi", HandleSseMulti);
        app.MapGet("/sse/multiline", HandleSseMultiline);
        app.MapGet("/sse/with-comments", HandleSseWithComments);
        app.MapGet("/sse/with-id-retry", HandleSseWithIdRetry);
        app.MapGet("/sse/empty", HandleSseEmpty);
    }

    private static async Task HandleGet(HttpContext ctx)
    {
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePost(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "POST");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePut(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "PUT");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePatch(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "PATCH");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleDelete(HttpContext ctx)
    {
        var response = BuildEchoResponse(ctx, "DELETE");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleHeaders(HttpContext ctx)
    {
        var headersDict = BuildHeadersObject(ctx);
        var response = new { headers = headersDict };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleStatus(HttpContext ctx, int code)
    {
        ctx.Response.StatusCode = code;
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleBytes(HttpContext ctx, int n)
    {
        ctx.Response.ContentType = "application/octet-stream";
        var buffer = new byte[n];
        RandomNumberGenerator.Fill(buffer);
        await ctx.Response.Body.WriteAsync(buffer);
    }

    private static async Task HandleGetCookies(HttpContext ctx)
    {
        var cookies = new JsonObject();
        foreach (var cookie in ctx.Request.Cookies)
        {
            cookies[cookie.Key] = cookie.Value;
        }

        var response = new JsonObject { ["cookies"] = cookies };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleSetCookies(HttpContext ctx)
    {
        var query = ctx.Request.Query;
        foreach (var kvp in query)
        {
            var sanitizedKey = SanitizeCookieToken(kvp.Key);
            var sanitizedValue = SanitizeCookieToken(kvp.Value.ToString());
            ctx.Response.Cookies.Append(sanitizedKey, sanitizedValue, new CookieOptions { Path = "/" });
        }
        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect("/cookies", permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleRedirect(HttpContext ctx, int n)
    {
        var redirectUrl = n <= 1 ? "/get" : string.Concat("/redirect/", n - 1);
        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect(redirectUrl, permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleRedirectTo(HttpContext ctx)
    {
        var url = ctx.Request.Query["url"].ToString();
        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var parsed) || parsed.IsAbsoluteUri)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Only relative redirect URLs are allowed" });
            return;
        }

        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect(parsed.ToString(), permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleBasicAuth(HttpContext ctx, string user, string pass)
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        var isValid = ValidateBasicAuth(authHeader, user, pass);

        if (!isValid)
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Fake Realm\"";
            await ctx.Response.CompleteAsync();
            return;
        }

        var response = new { authenticated = true, user };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleCache(HttpContext ctx)
    {
        var etag = "\"cache-etag\"";
        var lastModified = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");

        var ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString();
        var ifModifiedSince = ctx.Request.Headers.IfModifiedSince.ToString();

        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        if (!string.IsNullOrEmpty(ifModifiedSince))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        ctx.Response.Headers.ETag = etag;
        ctx.Response.Headers.LastModified = lastModified;

        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleCacheWithSeconds(HttpContext ctx, int seconds)
    {
        ctx.Response.Headers.CacheControl = string.Concat("public, max-age=", seconds);
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleEtag(HttpContext ctx, string value)
    {
        var etag = string.Concat("\"", value, "\"");
        var ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString();

        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(value))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        ctx.Response.Headers.ETag = etag;
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleStream(HttpContext ctx, int n)
    {
        ctx.Response.ContentType = "application/json";
        for (var i = 0; i < n; i++)
        {
            var line = JsonSerializer.Serialize(new
            {
                id = i,
                origin = GetClientOrigin(ctx),
                url = GetFullUrl(ctx)
            });
            await ctx.Response.WriteAsync(line + "\n");
            await ctx.Response.Body.FlushAsync();
        }
    }

    private static async Task HandleResponseHeaders(HttpContext ctx)
    {
        foreach (var (key, value) in ctx.Request.Query)
        {
            var sanitizedKey = SanitizeHeaderToken(key);
            var sanitizedValue = SanitizeHeaderToken(value.ToString());
            ctx.Response.Headers.Append(sanitizedKey, sanitizedValue);
        }

        await ctx.Response.WriteAsJsonAsync(BuildEchoResponse(ctx, "GET"));
    }

    private static async Task HandleGzip(HttpContext ctx)
    {
        var jsonBytes = BuildCompressionPayload(ctx, gzipped: true);
        var compressed = CompressBytes(jsonBytes, CompressionType.Gzip);

        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.ContentEncoding = "gzip";
        ctx.Response.ContentLength = compressed.Length;
        await ctx.Response.Body.WriteAsync(compressed);
    }

    private static async Task HandleDeflate(HttpContext ctx)
    {
        var jsonBytes = BuildCompressionPayload(ctx, gzipped: false);
        var compressed = CompressBytes(jsonBytes, CompressionType.Deflate);

        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.ContentEncoding = "deflate";
        ctx.Response.ContentLength = compressed.Length;
        await ctx.Response.Body.WriteAsync(compressed);
    }

    private static async Task HandleDelay(HttpContext ctx, int n)
    {
        var clamped = Math.Min(Math.Max(n, 0), 10);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(clamped), ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var response = BuildEchoResponse(ctx, ctx.Request.Method);
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleUnstable(HttpContext ctx)
    {
        var rateStr = ctx.Request.Query["failure_rate"].FirstOrDefault();
        var failureRate = 0.5f;
        if (rateStr is not null && float.TryParse(rateStr, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            failureRate = Math.Clamp(parsed, 0f, 1f);
        }

        if (Random.Shared.NextSingle() < failureRate)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.CompleteAsync();
            return;
        }

        var response = BuildEchoResponse(ctx, ctx.Request.Method);
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleStreamBytes(HttpContext ctx, int n)
    {
        var seed = 0;
        var seedStr = ctx.Request.Query["seed"].FirstOrDefault();
        if (seedStr is not null && int.TryParse(seedStr, out var parsedSeed))
        {
            seed = parsedSeed;
        }

        var chunkSize = 1024;
        var chunkStr = ctx.Request.Query["chunk_size"].FirstOrDefault();
        if (chunkStr is not null && int.TryParse(chunkStr, out var parsedChunk) && parsedChunk > 0)
        {
            chunkSize = parsedChunk;
        }

        ctx.Response.ContentType = "application/octet-stream";
        var rng = new Random(seed);
        var remaining = Math.Max(n, 0);
        var buffer = new byte[chunkSize];

        while (remaining > 0)
        {
            var toWrite = Math.Min(remaining, chunkSize);
            rng.NextBytes(buffer.AsSpan(0, toWrite));
            await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, toWrite), ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            remaining -= toWrite;
        }
    }

    private static async Task HandleDrip(HttpContext ctx)
    {
        var numbytes = 10;
        var duration = 2.0;
        var delay = 0.0;
        var code = 200;

        var q = ctx.Request.Query;
        if (q.TryGetValue("numbytes", out var nb) && int.TryParse(nb, out var parsedNb))
        {
            numbytes = Math.Max(parsedNb, 1);
        }

        if (q.TryGetValue("duration", out var dur) && double.TryParse(dur, System.Globalization.CultureInfo.InvariantCulture, out var parsedDur))
        {
            duration = Math.Max(parsedDur, 0);
        }

        if (q.TryGetValue("delay", out var del) && double.TryParse(del, System.Globalization.CultureInfo.InvariantCulture, out var parsedDel))
        {
            delay = Math.Max(parsedDel, 0);
        }

        if (q.TryGetValue("code", out var c) && int.TryParse(c, out var parsedCode))
        {
            code = parsedCode;
        }

        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/octet-stream";

        if (delay > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var interval = numbytes > 1 ? duration / numbytes : 0;

        for (var i = 0; i < numbytes; i++)
        {
            try
            {
                await ctx.Response.Body.WriteAsync(new byte[] { 0x2A }, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

                if (interval > 0 && i < numbytes - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task HandleRange(HttpContext ctx, int n)
    {
        var total = Math.Max(n, 0);
        var data = new byte[total];
        for (var i = 0; i < total; i++)
        {
            data[i] = (byte)(i % 256);
        }

        var rangeHeader = ctx.Request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(rangeHeader))
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.Headers.AcceptRanges = "bytes";
            await ctx.Response.Body.WriteAsync(data);
            return;
        }

        if (!TryParseRange(rangeHeader, total, out var start, out var end))
        {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers.ContentRange = string.Concat("bytes */", total);
            await ctx.Response.CompleteAsync();
            return;
        }

        var slice = data.AsMemory(start, end - start + 1);
        ctx.Response.StatusCode = 206;
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.Headers.ContentRange = string.Concat("bytes ", start, "-", end, "/", total);
        ctx.Response.Headers.AcceptRanges = "bytes";
        await ctx.Response.Body.WriteAsync(slice);
    }

    private static async Task HandleAbsoluteRedirect(HttpContext ctx, int n)
    {
        var hostString = ctx.Request.Host.ToString();
        var scheme = "http";

        // Check for X-Forwarded-Proto header (set by reverse proxy) if available,
        // otherwise use IsHttps (for direct HTTPS connections to Kestrel)
        if (ctx.Request.Headers.TryGetValue("X-Forwarded-Proto", out var protoValue))
        {
            scheme = protoValue.ToString().ToLowerInvariant();
        }
        else if (ctx.Request.IsHttps)
        {
            scheme = "https";
        }

        // Check for X-Forwarded-Host (set by reverse proxy to include port)
        if (ctx.Request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost))
        {
            var fwdHost = forwardedHost.ToString();
            if (!string.IsNullOrEmpty(fwdHost))
            {
                hostString = fwdHost;
            }
        }

        // If Host header is missing (e.g., HTTP/1.0), construct from request context
        if (string.IsNullOrEmpty(hostString))
        {
            // Try to use X-Forwarded-For if available, otherwise use connection local address
            var hostIp = "127.0.0.1";
            if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            {
                var fwdFor = forwardedFor.ToString()?.Split(',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(fwdFor))
                {
                    hostIp = fwdFor;
                }
            }
            else if (ctx.Connection.LocalIpAddress != null)
            {
                hostIp = ctx.Connection.LocalIpAddress.ToString();
            }

            var port = ctx.Connection.LocalPort;
            hostString = $"{hostIp}:{port}";
        }

        var redirectUrl = n <= 1
            ? string.Concat(scheme, "://", hostString, "/get")
            : string.Concat(scheme, "://", hostString, "/absolute-redirect/", n - 1);

        ctx.Response.Redirect(redirectUrl, permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleRelativeRedirect(HttpContext ctx, int n)
    {
        var redirectUrl = n <= 1 ? "/get" : string.Concat("/relative-redirect/", n - 1);
        ctx.Response.Redirect(redirectUrl, permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleDeleteCookies(HttpContext ctx)
    {
        foreach (var key in ctx.Request.Query.Keys)
        {
            ctx.Response.Cookies.Delete(key);
        }

        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect("/cookies", permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleBearer(HttpContext ctx)
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            await ctx.Response.CompleteAsync();
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var response = new { authenticated = true, token };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static bool TryParseRange(string header, int total, out int start, out int end)
    {
        start = 0;
        end = total - 1;

        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header["bytes=".Length..].Trim();

        if (spec.StartsWith('-'))
        {
            if (!int.TryParse(spec[1..], out var suffix) || suffix <= 0)
            {
                return false;
            }

            start = Math.Max(total - suffix, 0);
            end = total - 1;
            return true;
        }

        var dashIndex = spec.IndexOf('-');
        if (dashIndex < 0)
        {
            return false;
        }

        if (!int.TryParse(spec[..dashIndex], out start))
        {
            return false;
        }

        var endPart = spec[(dashIndex + 1)..];
        if (string.IsNullOrEmpty(endPart))
        {
            end = total - 1;
        }
        else if (!int.TryParse(endPart, out end))
        {
            return false;
        }

        return start >= 0 && start < total && end >= start && end < total;
    }

    private static byte[] BuildCompressionPayload(HttpContext ctx, bool gzipped)
    {
        var headersDict = BuildHeadersObject(ctx);
        object payload = gzipped
            ? new { gzipped = true, headers = headersDict, origin = GetClientOrigin(ctx), method = "GET" }
            : new { deflated = true, headers = headersDict, origin = GetClientOrigin(ctx), method = "GET" };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static byte[] CompressBytes(byte[] data, CompressionType type)
    {
        using var ms = new MemoryStream();
        using (Stream stream = type switch
        {
            CompressionType.Gzip => new GZipStream(ms, CompressionLevel.Fastest),
            // HTTP "deflate" is actually zlib-wrapped, not raw deflate
            CompressionType.Deflate => new ZLibStream(ms, CompressionLevel.Fastest),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        })
        {
            stream.Write(data);
        }

        return ms.ToArray();
    }

    private enum CompressionType { Gzip, Deflate }

    private static JsonObject BuildEchoResponse(HttpContext ctx, string method)
    {
        var response = new JsonObject
        {
            ["args"] = BuildArgsObject(ctx),
            ["headers"] = BuildHeadersNode(ctx),
            ["origin"] = GetClientOrigin(ctx),
            ["url"] = GetFullUrl(ctx),
            ["method"] = method
        };
        return response;
    }

    private static async Task<JsonObject> BuildMethodBodyResponse(HttpContext ctx, string method)
    {
        var body = await ReadBodyAsString(ctx);
        var contentType = ctx.Request.ContentType ?? "";

        var response = new JsonObject
        {
            ["args"] = BuildArgsObject(ctx),
            ["data"] = body,
            ["headers"] = BuildHeadersNode(ctx),
            ["origin"] = GetClientOrigin(ctx),
            ["url"] = GetFullUrl(ctx),
            ["method"] = method
        };

        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);
                response["json"] = JsonNode.Parse(jsonElement.GetRawText());
            }
            catch
            {
                response["json"] = null;
            }
            response["form"] = new JsonObject();
        }
        else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var formDict = ParseFormData(body);
            var formObject = new JsonObject();
            foreach (var kvp in formDict)
            {
                formObject[kvp.Key] = kvp.Value;
            }
            response["form"] = formObject;
            response["json"] = null;
        }
        else
        {
            response["form"] = new JsonObject();
            response["json"] = null;
        }

        return response;
    }

    private static JsonObject BuildArgsObject(HttpContext ctx)
    {
        var args = new JsonObject();
        foreach (var kvp in ctx.Request.Query)
        {
            args[kvp.Key] = kvp.Value.ToString();
        }
        return args;
    }

    private static JsonNode BuildHeadersNode(HttpContext ctx)
    {
        var headersDict = BuildHeadersObject(ctx);
        var node = new JsonObject();
        foreach (var kvp in headersDict)
        {
            node[kvp.Key] = kvp.Value switch
            {
                string str => str,
                string[] arr => arr[0],
                _ => null
            };
        }
        return node;
    }

    private static Dictionary<string, object> BuildHeadersObject(HttpContext ctx)
    {
        var headers = new Dictionary<string, object>();
        foreach (var kvp in ctx.Request.Headers)
        {
            if (kvp.Value.Count == 1)
            {
                headers[kvp.Key] = kvp.Value[0] ?? "";
            }
            else
            {
                headers[kvp.Key] = kvp.Value.ToArray();
            }
        }
        return headers;
    }

    private static string GetFullUrl(HttpContext ctx)
    {
        var scheme = ctx.Request.Scheme;
        var host = ctx.Request.Host.ToString();
        var path = ctx.Request.Path.ToString();
        var query = ctx.Request.QueryString.ToString();
        return string.Concat(scheme, "://", host, path, query);
    }

    private static string GetClientOrigin(HttpContext ctx)
    {
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }

    private static async Task<string> ReadBodyAsString(HttpContext ctx)
    {
        ctx.Request.EnableBuffering();
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            var body = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
            return body;
        }
    }

    private static Dictionary<string, string> ParseFormData(string body)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(body))
        {
            return result;
        }

        var pairs = body.Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2)
            {
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                result[key] = value;
            }
        }
        return result;
    }

    private static string SanitizeCookieToken(string value)
    {
        return new string(value.Where(c => c >= 0x20 && c != ';' && c != ',' && c != 0x7F).ToArray());
    }

    private static string SanitizeHeaderToken(string value)
    {
        return new string(value.Where(c => c >= 0x20 && c != '\r' && c != '\n' && c != 0x7F).ToArray());
    }

    private static bool IsAllowedRedirectUrl(string url, HttpContext ctx)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.StartsWith('/'))
        {
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.Host, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static async Task HandleSseSimple(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync("data: hello\n\n");
        await ctx.Response.WriteAsync("data: world\n\n");
    }

    private static async Task HandleSseTyped(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync("event: greeting\ndata: hello\n\n");
        await ctx.Response.WriteAsync("event: farewell\ndata: goodbye\n\n");
    }

    private static async Task HandleSseMulti(HttpContext ctx)
    {
        var countStr = ctx.Request.Query["n"].FirstOrDefault();
        var count = 5;
        if (countStr is not null && int.TryParse(countStr, out var parsed))
        {
            count = Math.Clamp(parsed, 1, 100);
        }

        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        for (var i = 0; i < count; i++)
        {
            await ctx.Response.WriteAsync(string.Concat("data: event-", i, "\n\n"));
            await ctx.Response.Body.FlushAsync();
        }
    }

    private static async Task HandleSseMultiline(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync("data: line1\ndata: line2\ndata: line3\n\n");
    }

    private static async Task HandleSseWithComments(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync(": keepalive\ndata: visible\n\n");
    }

    private static async Task HandleSseWithIdRetry(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync("id: 42\nretry: 3000\nevent: update\ndata: payload\n\n");
    }

    private static async Task HandleSseEmpty(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.Body.FlushAsync();
    }

    private static bool ValidateBasicAuth(string authHeader, string expectedUser, string expectedPass)
    {
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var base64 = authHeader["Basic ".Length..];
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0)
            {
                return false;
            }

            var user = decoded[..colonIndex];
            var pass = decoded[(colonIndex + 1)..];

            return user == expectedUser && pass == expectedPass;
        }
        catch
        {
            return false;
        }
    }
}
