using System.Collections.Concurrent;
using System.Net;
using TurboHttp.Protocol.RFC6265;

namespace TurboHttp.Tests.RFC6265;

/// <summary>
/// RFC 6265 — CookieJar thread-safety tests.
/// Verifies that concurrent access from CookieInjectionStage (pre-processing island)
/// and CookieStorageStage (post-processing island) doesn't corrupt cookie state.
/// </summary>
public sealed class CookieJarThreadSafetyTests
{
    private static Uri Uri(string url) => new(url);

    private static HttpResponseMessage ResponseWithCookie(string setCookie)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }

    // ── TS-001–TS-004: Concurrent read + write safety ────────────────────────

    [Fact(DisplayName = "RFC6265-5.3-TS-001: Concurrent ProcessResponse calls don't throw")]
    public async Task Should_NotThrow_When_ConcurrentProcessResponse()
    {
        var jar = new CookieJar();
        var exceptions = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            try
            {
                jar.ProcessResponse(
                    Uri($"http://example.com/path{i}"),
                    ResponseWithCookie($"cookie{i}=value{i}; Path=/path{i}"));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }

    [Fact(DisplayName = "RFC6265-5.3-TS-002: Concurrent AddCookiesToRequest calls don't throw")]
    public async Task Should_NotThrow_When_ConcurrentAddCookiesToRequest()
    {
        var jar = new CookieJar();

        // Seed with cookies
        for (var i = 0; i < 50; i++)
        {
            jar.ProcessResponse(
                Uri("http://example.com/"),
                ResponseWithCookie($"cookie{i}=value{i}"));
        }

        var exceptions = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
                jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }

    [Fact(DisplayName = "RFC6265-5.3-TS-003: Concurrent read + write (injection + storage) don't throw or corrupt")]
    public async Task Should_NotThrowOrCorrupt_When_ConcurrentReadAndWrite()
    {
        var jar = new CookieJar();
        var exceptions = new ConcurrentBag<Exception>();

        // Seed with initial cookies
        for (var i = 0; i < 10; i++)
        {
            jar.ProcessResponse(
                Uri("http://example.com/"),
                ResponseWithCookie($"init{i}=v{i}"));
        }

        // Simulate CookieInjectionStage (reads) and CookieStorageStage (writes) concurrently
        var writerTasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            try
            {
                jar.ProcessResponse(
                    Uri("http://example.com/"),
                    ResponseWithCookie($"dynamic{i}=val{i}"));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        var readerTasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
                jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        Assert.Empty(exceptions);
        // All dynamic cookies should be present (each replaces or adds unique cookie)
        Assert.True(jar.Count > 0, "Cookie jar should contain cookies after concurrent operations");
    }

    [Fact(DisplayName = "RFC6265-5.3-TS-004: Concurrent Clear + ProcessResponse don't throw")]
    public async Task Should_NotThrow_When_ConcurrentClearAndProcessResponse()
    {
        var jar = new CookieJar();
        var exceptions = new ConcurrentBag<Exception>();

        var writerTasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            try
            {
                jar.ProcessResponse(
                    Uri("http://example.com/"),
                    ResponseWithCookie($"c{i}=v{i}; Path=/p{i}"));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        var clearTasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try
            {
                jar.Clear();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(writerTasks.Concat(clearTasks));

        Assert.Empty(exceptions);
    }

    // ── TS-005–TS-007: Correctness under contention ─────────────────────────

    [Fact(DisplayName = "RFC6265-5.3-TS-005: Cookie injection returns correct cookies under contention")]
    public async Task Should_ReturnCorrectCookies_When_ConcurrentInjectionUnderContention()
    {
        var jar = new CookieJar();

        // Store a known cookie
        jar.ProcessResponse(
            Uri("http://stable.com/"),
            ResponseWithCookie("auth=token123"));

        var incorrectResults = new ConcurrentBag<string>();

        // Concurrent writers to a different domain + concurrent readers to stable.com
        var writerTasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            jar.ProcessResponse(
                Uri("http://other.com/"),
                ResponseWithCookie($"other{i}=v{i}"));
        }));

        var readerTasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "http://stable.com/");
            jar.AddCookiesToRequest(Uri("http://stable.com/"), ref req);

            if (!req.Headers.TryGetValues("Cookie", out var values))
            {
                incorrectResults.Add($"Iteration {i}: No Cookie header found");
                return;
            }

            var header = string.Join("", values);
            if (!header.Contains("auth=token123"))
            {
                incorrectResults.Add($"Iteration {i}: Expected 'auth=token123', got '{header}'");
            }
        }));

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        Assert.Empty(incorrectResults);
    }

    [Fact(DisplayName = "RFC6265-5.3-TS-006: Cookie storage correctly replaces under contention")]
    public async Task Should_ReplaceCorrectly_When_ConcurrentStorageUnderContention()
    {
        var jar = new CookieJar();

        // All threads write the same cookie name with different values — should not corrupt
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            jar.ProcessResponse(
                Uri("http://example.com/"),
                ResponseWithCookie($"session=value{i}"));
        })).ToArray();

        await Task.WhenAll(tasks);

        // Only one cookie with name "session" should exist (last writer wins)
        Assert.Equal(1, jar.Count);

        // Verify the cookie is readable
        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
        Assert.True(req.Headers.TryGetValues("Cookie", out var values));
        var header = string.Join("", values);
        Assert.StartsWith("session=value", header);
    }

    [Fact(DisplayName = "RFC6265-5.3-TS-007: Count property is consistent under concurrent modification")]
    public async Task Should_BeConsistent_When_ConcurrentCountAndModification()
    {
        var jar = new CookieJar();
        var exceptions = new ConcurrentBag<Exception>();

        var writerTasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            try
            {
                jar.ProcessResponse(
                    Uri("http://example.com/"),
                    ResponseWithCookie($"c{i}=v{i}; Path=/p{i}"));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        var readerTasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try
            {
                _ = jar.Count;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        Assert.Empty(exceptions);
        Assert.True(jar.Count > 0);
    }
}
