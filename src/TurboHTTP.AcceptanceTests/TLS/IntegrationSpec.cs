using System.Net;
using System.Text;
using System.Text.Json;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class IntegrationSpec : AcceptanceTestBase
{
    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request)
    {
        var fake = ResponseMapFake.Create(map);
        var flow = fake
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithCookiesAsync(ResponseMap map, HttpRequestMessage request,
        CookieJar jar)
    {
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var fake = ResponseMapFake.Create(map);
        var flow = cookie.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRedirectAsync(ResponseMap map, HttpRequestMessage request)
    {
        var redirect = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()));
        var fake = ResponseMapFake.Create(map);
        var flow = redirect.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task Get_hello_should_return_200_over_https()
    {
        var map = new ResponseMap()
            .On("/hello", HttpStatusCode.OK, "Hello World");

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.3")]
    public async Task Post_echo_should_echo_body_over_https()
    {
        var payload = "TLS echo payload";
        var map = new ResponseMap()
            .On("/echo", req =>
            {
                var reqBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reqBody)
                };
            });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/echo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.3")]
    public async Task Headers_should_roundtrip_custom_headers_over_https()
    {
        var map = new ResponseMap()
            .On("/headers/echo", req =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                if (req.Headers.TryGetValues("X-Custom-Tls", out var vals))
                {
                    r.Headers.TryAddWithoutValidation("X-Custom-Tls", vals);
                }

                return r;
            });

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/headers/echo");
        request.Headers.Add("X-Custom-Tls", "secure-value");
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom-Tls", out var values));
        Assert.Equal("secure-value", values.First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_should_set_and_echo_roundtrip_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set/tlssession/encrypted", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "tlssession=encrypted; Path=/");
                return r;
            })
            .On("/cookie/echo", req =>
            {
                var cookies = new Dictionary<string, string>();
                if (req.Headers.TryGetValues("Cookie", out var vals))
                {
                    foreach (var v in vals)
                    {
                        foreach (var pair in v.Split(';', StringSplitOptions.TrimEntries))
                        {
                            var eq = pair.IndexOf('=');
                            if (eq > 0)
                            {
                                cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                            }
                        }
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(cookies))
                };
            });

        var setResponse = await SendWithCookiesAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set/tlssession/encrypted"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoResponse = await SendWithCookiesAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("encrypted", cookies["tlssession"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Secure_cookie_should_be_sent_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-secure/secret/hidden", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "secret=hidden; Path=/; Secure");
                return r;
            })
            .On("/cookie/echo", req =>
            {
                var cookies = new Dictionary<string, string>();
                if (req.Headers.TryGetValues("Cookie", out var vals))
                {
                    foreach (var v in vals)
                    {
                        foreach (var pair in v.Split(';', StringSplitOptions.TrimEntries))
                        {
                            var eq = pair.IndexOf('=');
                            if (eq > 0)
                            {
                                cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                            }
                        }
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(cookies))
                };
            });

        await SendWithCookiesAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-secure/secret/hidden"), jar);

        var echoResponse = await SendWithCookiesAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.True(cookies.ContainsKey("secret"), "Secure cookie MUST be sent over HTTPS");
        Assert.Equal("hidden", cookies["secret"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4.1")]
    public async Task Gzip_should_be_transparently_decompressed_over_https()
    {
        var bodyBytes = new byte[4 * 1024];
        for (var i = 0; i < bodyBytes.Length; i++)
        {
            bodyBytes[i] = (byte)('A' + i % 26);
        }

        var map = new ResponseMap()
            .On("/compress/gzip/4", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bodyBytes)
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/compress/gzip/4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_302_should_be_followed_over_https()
    {
        var map = new ResponseMap()
            .On("/redirect/302/hello", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("https://localhost/hello");
                return r;
            })
            .On("/hello", HttpStatusCode.OK, "Hello World");

        var response = await SendWithRedirectAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/302/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 5000)]
    [InlineData(64)]
    [InlineData(256)]
    [Trait("RFC", "RFC9110-8.6")]
    public async Task Large_body_should_transfer_over_https(int kb)
    {
        var bodyBytes = new byte[kb * 1024];
        for (var i = 0; i < bodyBytes.Length; i++)
        {
            bodyBytes[i] = (byte)(i & 0xFF);
        }

        var map = new ResponseMap()
            .On($"/large/{kb}", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bodyBytes)
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, $"https://localhost/large/{kb}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(kb * 1024, body.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Chunked_transfer_encoding_should_work_over_https()
    {
        var bodyBytes = new byte[4 * 1024];
        for (var i = 0; i < bodyBytes.Length; i++)
        {
            bodyBytes[i] = (byte)(i & 0xFF);
        }

        var map = new ResponseMap()
            .On("/chunked/4", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bodyBytes)
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/chunked/4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, body.Length);
    }
}