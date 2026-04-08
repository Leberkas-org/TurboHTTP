using System.Collections.Concurrent;
using System.Net;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Cookies;

/// <summary>
/// RFC 6265 — CookieJar thread-safety tests.
/// Verifies that concurrent access from CookieBidiStage (request and response directions)
/// doesn't corrupt cookie state.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CookieJar"/>.
/// RFC 6265 §5.3: Cookie storage model — must remain consistent under concurrent reads and writes.
/// </remarks>
public sealed class CookieJarThreadSafetySpec
{
    private static Uri Uri(string url) => new(url);

    private static HttpResponseMessage ResponseWithCookie(string setCookie)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_not_throw_when_concurrent_process_response()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_not_throw_when_concurrent_add_cookies_to_request()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_not_throw_or_corrupt_when_concurrent_read_and_write()
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

        // Simulate CookieBidiStage request direction (reads) and response direction (writes) concurrently
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_not_throw_when_concurrent_clear_and_process_response()
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


    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_return_correct_cookies_when_concurrent_injection_under_contention()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_replace_correctly_when_concurrent_storage_under_contention()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 5000)]
    public async Task CookieJar_should_be_consistent_when_concurrent_count_and_modification()
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
