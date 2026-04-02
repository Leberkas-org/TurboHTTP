using System.Collections.Immutable;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.Cookies;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Cookies;

/// <summary>
/// RFC 6265 — CookieBidiStage request and response direction tests.
/// Verifies that the request direction injects cookies from the jar and the response direction
/// stores Set-Cookie headers into the jar.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="CookieBidiStage"/>.
/// RFC 6265 §5.4: Cookie header construction (request path).
/// RFC 6265 §5.2–§5.3: Set-Cookie storage (response path).
/// </remarks>
public sealed class CookieBidiStageSpec : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        CookieBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredRequestSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        CookieBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredResponseSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private static CookieJar JarWithCookie(string name, string value, string domain, string path = "/")
    {
        var jar = new CookieJar();
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"{name}={value}; Domain={domain}; Path={path}");
        jar.ProcessResponse(new Uri($"http://{domain}/"), response);
        return jar;
    }

    private static HttpResponseMessage MakeResponse(string? requestUri, string? setCookie = null)
    {
        var response = new HttpResponseMessage();
        if (requestUri is not null)
        {
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        }
        if (setCookie is not null)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }
        return response;
    }


    // Request direction tests

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_pass_through_request_when_cookie_jar_is_null()
    {
        var stage = new CookieBidiStage(null);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_inject_cookie_when_matching_cookie_in_jar()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_not_add_cookie_header_when_jar_is_empty()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_not_add_cookie_header_when_domain_does_not_match()
    {
        var jar = JarWithCookie("session", "abc123", "other.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_pass_through_request_when_request_uri_is_null()
    {
        var jar = JarWithCookie("session", "abc123", "example.com");
        var stage = new CookieBidiStage(jar);
        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_inject_cookies_independently_for_multiple_requests()
    {
        var jar = JarWithCookie("token", "xyz", "example.com");
        var stage = new CookieBidiStage(jar);
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = new List<HttpRequestMessage>(await RunRequestAsync(stage, req1, req2));

        Assert.Equal(2, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("Cookie"));
            var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
            Assert.Contains("token=xyz", cookieValue);
        }
    }


    // Response direction tests

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_pass_through_response_when_cookie_jar_is_null()
    {
        var stage = new CookieBidiStage(null);
        var response = MakeResponse("http://example.com/", "session=abc; Domain=example.com");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
        Assert.Same(response, results[0]);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_store_cookie_when_set_cookie_header_present()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/", "session=abc123; Domain=example.com; Path=/");

        await RunResponseAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_not_modify_response()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/", "token=xyz; Domain=example.com; Path=/");
        var originalStatusCode = response.StatusCode;

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(originalStatusCode, result.StatusCode);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_keep_jar_empty_when_no_set_cookie_header()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/");

        await RunResponseAsync(stage, response);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_pass_through_response_when_request_message_is_null()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var response = MakeResponse(null, "session=abc; Domain=example.com");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.False(nextRequest.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_accumulate_cookies_for_multiple_responses()
    {
        var jar = new CookieJar();
        var stage = new CookieBidiStage(jar);
        var resp1 = MakeResponse("http://example.com/", "a=1; Domain=example.com; Path=/");
        var resp2 = MakeResponse("http://example.com/", "b=2; Domain=example.com; Path=/");

        await RunResponseAsync(stage, resp1, resp2);

        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(new Uri("http://example.com/"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("a=1", cookieValue);
        Assert.Contains("b=2", cookieValue);
    }


    // Bidirectional integration test

    [Trait("RFC", "RFC6265-5.4")]
    [Fact(Timeout = 10_000)]
    public async Task CookieBidiStage_should_inject_cookie_stored_from_previous_response()
    {
        var jar = new CookieJar();

        // First: process a response with Set-Cookie to populate the jar
        var stage1 = new CookieBidiStage(jar);
        var response = MakeResponse("http://example.com/login", "auth=token123; Domain=example.com; Path=/");
        await RunResponseAsync(stage1, response);

        // Second: process a request — the cookie should be injected from the jar
        var stage2 = new CookieBidiStage(jar);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dashboard");
        var results = await RunRequestAsync(stage2, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", result.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token123", cookieValue);
    }
}
