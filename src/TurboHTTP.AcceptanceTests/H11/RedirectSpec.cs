using System.Net;
using System.Text;
using TurboHTTP.Client;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class RedirectSpec : ClientAcceptanceTestBase
{
    private async Task<HttpResponseMessage> SendWithRedirectAsync(
        HttpRequestMessage request,
        Func<string, byte[]?> pathHandler)
    {
        var requestCount = 0;
        var stage = CreateAccumulatingScriptedConnection((_, requestBytes) =>
        {
            requestCount++;
            var requestStr = Encoding.Latin1.GetString(requestBytes);
            var lines = requestStr.Split("\r\n");
            if (lines.Length == 0)
            {
                return FakeResponse.Http11(400);
            }

            var pathLine = lines[0];
            var parts = pathLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var pathMatch = parts.Length > 1 ? parts[1] : "/";
            return pathHandler(pathMatch);
        });

        var transports = new TransportRegistry()
            .Register(HttpVersion.Version11, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version11,
            builder => builder.WithRedirect());

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_301_to_hello()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/301/hello"),
            path => path switch
            {
                "/redirect/301/hello" => FakeResponse.Http11(301, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_302_to_hello()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/302/hello"),
            path => path switch
            {
                "/redirect/302/hello" => FakeResponse.Http11(302, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_307_to_hello()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/307/hello"),
            path => path switch
            {
                "/redirect/307/hello" => FakeResponse.Http11(307, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_308_to_hello()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/308/hello"),
            path => path switch
            {
                "/redirect/308/hello" => FakeResponse.Http11(308, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 10000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_chain_of_n_hops_to_hello(int hops)
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, $"http://fake.test/redirect/chain/{hops}"),
            path =>
            {
                if (path == "/hello")
                {
                    return FakeResponse.Http11(200, "Hello World");
                }

                if (path.StartsWith("/redirect/chain/"))
                {
                    var parts = path.Split('/');
                    if (int.TryParse(parts.Last(), out var n))
                    {
                        var nextPath = n <= 1
                            ? "/hello"
                            : $"/redirect/chain/{n - 1}";
                        return FakeResponse.Http11(302, null,
                            ("Location", $"http://fake.test{nextPath}"));
                    }
                }

                return FakeResponse.Http11(404);
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_return_final_redirect_response_on_infinite_loop()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/loop"),
            path => path switch
            {
                "/redirect/loop" => FakeResponse.Http11(302, null,
                    ("Location", "http://fake.test/redirect/loop")),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_resolve_relative_location_header_to_hello()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/relative"),
            path => path switch
            {
                "/redirect/relative" => FakeResponse.Http11(302, null,
                    ("Location", "/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_allow_cross_scheme_http_to_http()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/cross-scheme"),
            path => path switch
            {
                "/redirect/cross-scheme" => FakeResponse.Http11(302, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_preserve_post_307_method_and_body()
    {
        var payload = "redirect-307-body";
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://fake.test/redirect/307")
            {
                Content = new StringContent(payload, Encoding.UTF8, "text/plain")
            },
            path => path switch
            {
                "/redirect/307" => FakeResponse.Http11(307, null,
                    ("Location", "http://fake.test/echo")),
                "/echo" => FakeResponse.Http11(200, payload),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_rewrite_post_303_to_get()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://fake.test/redirect/303")
            {
                Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
            },
            path => path switch
            {
                "/redirect/303" => FakeResponse.Http11(303, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_rewrite_post_302_to_get()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://fake.test/redirect/302")
            {
                Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
            },
            path => path switch
            {
                "/redirect/302" => FakeResponse.Http11(302, null,
                    ("Location", "http://fake.test/hello")),
                "/hello" => FakeResponse.Http11(200, "Hello World"),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_preserve_post_308_method_and_body()
    {
        var payload = "redirect-308-body";
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://fake.test/redirect/308")
            {
                Content = new StringContent(payload, Encoding.UTF8, "text/plain")
            },
            path => path switch
            {
                "/redirect/308" => FakeResponse.Http11(308, null,
                    ("Location", "http://fake.test/echo")),
                "/echo" => FakeResponse.Http11(200, payload),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_cross_origin_to_headers_echo()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/cross-origin")
            {
                Headers = { { "X-Test", "preserved" } }
            },
            path => path switch
            {
                "/redirect/cross-origin" => FakeResponse.Http11(302, null,
                    ("Location", "http://other-host/echo")),
                "/echo" => FakeResponse.Http11(200),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_preserve_authorization_header_on_same_origin()
    {
        var response = await SendWithRedirectAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/redirect/cross-origin-auth")
            {
                Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token") }
            },
            path => path switch
            {
                "/redirect/cross-origin-auth" => FakeResponse.Http11(302, null,
                    ("Location", "http://fake.test/auth")),
                "/auth" => FakeResponse.Http11(200),
                _ => FakeResponse.Http11(404)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}