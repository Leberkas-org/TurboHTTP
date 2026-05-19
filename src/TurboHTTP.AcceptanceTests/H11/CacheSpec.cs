using TurboHTTP.Client;
using System.Net;
using System.Text;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class CacheSpec : ClientAcceptanceTestBase
{
    private async Task<List<HttpResponseMessage>> SendMultipleAsync(
        IReadOnlyList<(HttpRequestMessage Request, string? ExpectedCachePath)> requests,
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
        foreach (var (request, _) in requests)
        {
            var response = await helper.Client.SendAsync(request, ct);
            responses.Add(response);
        }

        return responses;
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.1")]
    public async Task Cache_should_serve_max_age_response_from_cache()
    {
        var callCount = 0;
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/max-age/3600"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/max-age/3600")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"max-age-body-{callCount}",
                    ("Cache-Control", "max-age=3600"));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.False(string.IsNullOrEmpty(body1), "First response body should be non-empty");
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public async Task Cache_should_force_revalidation_with_no_cache()
    {
        var callCount = 0;
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/no-cache"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/no-cache")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"no-cache-body-{callCount}",
                    ("Cache-Control", "no-cache"));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.NotEqual(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.5")]
    public async Task Cache_should_never_cache_no_store_response()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/no-store"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/no-store")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) => FakeResponse.Http11(200, "no-store-resource",
                ("Cache-Control", "no-store")),
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal("no-store-resource", body1);
        Assert.Equal("no-store-resource", body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-4.3.2")]
    public async Task Cache_should_send_if_none_match_on_etag_revalidation()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/etag/test1"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/etag/test1")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) => FakeResponse.Http11(200, "etag-resource-test1",
                ("Cache-Control", "max-age=3600"),
                ("ETag", "\"etag-test1\"")),
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("etag-resource-test1", body1);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-4.3.2")]
    public async Task Cache_should_send_if_modified_since_on_last_modified_revalidation()
    {
        var lastMod = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/last-modified/doc1"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/last-modified/doc1")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) => FakeResponse.Http11(200, "last-modified-resource-doc1",
                ("Cache-Control", "max-age=3600"),
                ("Last-Modified", lastMod)),
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("last-modified-resource-doc1", body1);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-4.1")]
    public async Task Cache_should_produce_different_cache_entries_per_vary_header_value()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/vary/Accept-Language");
        request1.Headers.TryAddWithoutValidation("Accept-Language", "en");

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/vary/Accept-Language");
        request2.Headers.TryAddWithoutValidation("Accept-Language", "de");

        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/vary/Accept-Language");
        request3.Headers.TryAddWithoutValidation("Accept-Language", "en");

        var requestsWithLangs = new[] { (request1, "en"), (request2, "de"), (request3, "en") };

        var responses = await SendMultipleAsync(
            requestsWithLangs.Select(r => (r.Item1, (string?)null)).ToList(),
            (index, requestBytes) =>
            {
                var requestStr = Encoding.Latin1.GetString(requestBytes);
                var lang = "unknown";
                foreach (var line in requestStr.Split("\r\n"))
                {
                    if (line.StartsWith("Accept-Language:", StringComparison.OrdinalIgnoreCase))
                    {
                        lang = line.Split(": ")[1].Trim();
                        break;
                    }
                }

                return FakeResponse.Http11(200, $"vary-Accept-Language:{lang}",
                    ("Cache-Control", "max-age=3600"),
                    ("Vary", "Accept-Language"));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);
        var body3 = await responses[2].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("vary-Accept-Language:en", body1);

        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal("vary-Accept-Language:de", body2);

        Assert.NotEqual(body1, body2);

        Assert.Equal(HttpStatusCode.OK, responses[2].StatusCode);
        Assert.Equal(body1, body3);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.2")]
    public async Task Cache_should_force_revalidation_with_must_revalidate()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/must-revalidate"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/must-revalidate")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (index, requestBytes) =>
            {
                if (index == 0)
                {
                    return FakeResponse.Http11(200, "must-revalidate-body",
                        ("Cache-Control", "must-revalidate, max-age=0"),
                        ("ETag", "\"mr-etag-1\""));
                }

                var requestStr = Encoding.Latin1.GetString(requestBytes);
                var hasIfNoneMatch = requestStr.Contains("If-None-Match");
                if (hasIfNoneMatch)
                {
                    return FakeResponse.Http11(304, null,
                        ("ETag", "\"mr-etag-1\""));
                }

                return FakeResponse.Http11(200, "must-revalidate-body-new",
                    ("Cache-Control", "must-revalidate, max-age=0"),
                    ("ETag", "\"mr-etag-2\""));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.10")]
    public async Task Cache_should_respect_s_maxage_by_shared_cache()
    {
        var callCount = 0;
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/s-maxage/3600"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/s-maxage/3600")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"s-maxage-body-{callCount}",
                    ("Cache-Control", "s-maxage=3600"));
            },
            builder => builder.WithCache(opts => opts.SharedCache = true));

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.3")]
    public async Task Cache_should_enable_caching_with_expires_header()
    {
        var callCount = 0;
        var expiresTime = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
        var dateTime = DateTimeOffset.UtcNow.ToString("R");
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/expires"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/expires")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"expires-body-{callCount}",
                    ("Cache-Control", "public"),
                    ("Expires", expiresTime),
                    ("Date", dateTime));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-5.2.2.7")]
    public async Task Cache_should_cache_private_response_by_private_cache()
    {
        var callCount = 0;
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/private"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/private")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"private-body-{callCount}",
                    ("Cache-Control", "private, max-age=3600"));
            },
            builder => builder.WithCache());

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9111-3.1")]
    public async Task Cache_should_not_cache_private_response_by_shared_cache()
    {
        var callCount = 0;
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/private"),
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/cache/private")
        };

        var responses = await SendMultipleAsync(
            requests.Select(r => (r, (string?)null)).ToList(),
            (_, _) =>
            {
                callCount++;
                return FakeResponse.Http11(200, $"private-body-{callCount}",
                    ("Cache-Control", "private, max-age=3600"));
            },
            builder => builder.WithCache(opts => opts.SharedCache = true));

        var ct = TestContext.Current.CancellationToken;
        var body1 = await responses[0].Content.ReadAsStringAsync(ct);
        var body2 = await responses[1].Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.NotEqual(body1, body2);
    }
}