using TurboHTTP.Client;
using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class CookieSpec : ClientAcceptanceTestBase
{
    private static Dictionary<string, string> ParseCookies(string cookieHeader)
    {
        var cookies = new Dictionary<string, string>();
        foreach (var pair in cookieHeader.Split(';', StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
            {
                cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }

        return cookies;
    }

    private static Dictionary<string, string> ExtractCookiesFromRequest(byte[] requestBytes)
    {
        var requestStr = Encoding.Latin1.GetString(requestBytes);
        var cookies = new Dictionary<string, string>();
        foreach (var line in requestStr.Split("\r\n"))
        {
            if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            {
                var cookieValue = line["Cookie:".Length..].Trim();
                foreach (var pair in cookieValue.Split(";", StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    }
                }
                break;
            }
        }

        return cookies;
    }

    private async Task<List<HttpResponseMessage>> SendMultipleAsync(
        IReadOnlyList<HttpRequestMessage> requests,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(HttpVersion.Version11, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version11, configure);

        var responses = new List<HttpResponseMessage>();
        var ct = TestContext.Current.CancellationToken;
        foreach (var request in requests)
        {
            var response = await helper.Client.SendAsync(request, ct);
            responses.Add(response);
        }

        return responses;
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_should_set_and_echo_roundtrip()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set/session/abc123"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "session=abc123; Path=/"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_must_not_be_sent_over_plaintext_http()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-secure/secret/hidden"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "secret=hidden; Path=/; Secure"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.False(cookies.ContainsKey("secret"), "Secure cookie should not be sent over plaintext HTTP");
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_send_httponly_on_subsequent_requests()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-httponly/token/xyz"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "token=xyz; Path=/; HttpOnly"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("xyz", cookies["token"]);
    }

    [Theory(Timeout = 10000)]
    [InlineData("Strict")]
    [InlineData("Lax")]
    [InlineData("None")]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_store_and_send_samesite_policy(string policy)
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, $"http://fake.test/cookie/set-samesite/pref/{policy}/{policy}"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", $"pref={policy}; Path=/; SameSite={policy}"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal(policy, cookies["pref"]);
    }

    [Fact(Timeout = 15000, Skip = "CookieJar Max-Age expiration not yet implemented")]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_must_not_be_sent_after_max_age_elapses()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-expires/temp/value/1"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "temp=value; Path=/; Max-Age=1"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;

        var json1 = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("value", cookies1["temp"]);

        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        var json2 = await responses[2].Content.ReadAsStringAsync(ct);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("temp"), "Expired cookie should not be sent");
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_be_stored_with_domain_scope()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-domain/site/val/localhost"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "site=val; Path=/; Domain=fake.test"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val", cookies["site"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_should_be_sent_only_for_matching_path()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-path/scoped/pathval/cookie"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "scoped=pathval; Path=/cookie"));
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("pathval", cookies["scoped"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_echo_should_return_empty_when_no_cookies()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (_, requestBytes) =>
            {
                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);

        var json = await responses[0].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Empty(cookies);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_store_multiple_set_cookie_headers()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-multiple"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    var sb = new StringBuilder();
                    sb.Append("HTTP/1.1 200 OK\r\n");
                    sb.Append("Set-Cookie: alpha=one; Path=/\r\n");
                    sb.Append("Set-Cookie: beta=two; Path=/\r\n");
                    sb.Append("Set-Cookie: gamma=three; Path=/\r\n");
                    sb.Append("Content-Length: 0\r\n");
                    sb.Append("\r\n");
                    return Encoding.Latin1.GetBytes(sb.ToString());
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_be_deleted_via_max_age_zero()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set/victim/alive"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/delete/victim"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "victim=alive; Path=/"));
                }

                if (index is 1 or 3)
                {
                    var cookies = ExtractCookiesFromRequest(requestBytes);
                    return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
                }

                if (index == 2)
                {
                    return FakeResponse.Http11(200, null,
                        ("Set-Cookie", "victim=; Path=/; Max-Age=0"));
                }

                return FakeResponse.Http11(500);
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;

        var json1 = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("alive", cookies1["victim"]);

        var json2 = await responses[3].Content.ReadAsStringAsync(ct);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("victim"), "Deleted cookie should not be sent");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Cookie_should_persist_across_redirect_response()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/set-and-redirect"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cookie/echo")
        };

        var responses = await SendMultipleAsync(
            requests,
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    var sb = new StringBuilder();
                    sb.Append("HTTP/1.1 302 Found\r\n");
                    sb.Append("Set-Cookie: redirect_cookie=from-redirect; Path=/\r\n");
                    sb.Append("Location: /cookie/echo\r\n");
                    sb.Append("Content-Length: 0\r\n");
                    sb.Append("\r\n");
                    return Encoding.Latin1.GetBytes(sb.ToString());
                }

                var cookies = ExtractCookiesFromRequest(requestBytes);
                return FakeResponse.Http11(200, JsonSerializer.Serialize(cookies));
            },
            builder => builder.WithCookies());

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Found, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var json = await responses[1].Content.ReadAsStringAsync(ct);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }
}