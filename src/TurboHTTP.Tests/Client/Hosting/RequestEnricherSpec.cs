using System.Net;
using System.Net.Http.Headers;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Client.Hosting;

public sealed class RequestEnricherSpec
{
    private static RequestEnricher CreateEnricher(
        Uri? baseAddress = null,
        HttpRequestHeaders? defaultHeaders = null,
        Version? defaultVersion = null)
    {
        var holder = new HttpRequestMessage();
        var headers = defaultHeaders ?? holder.Headers;

        return new RequestEnricher(() => new TurboRequestOptions(
            baseAddress,
            headers,
            defaultVersion ?? HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionExact,
            TimeSpan.MaxValue));
    }

    private static (HttpRequestMessage Holder, HttpRequestHeaders Headers) CreateDefaultHeaders()
    {
        var holder = new HttpRequestMessage();
        return (holder, holder.Headers);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_set_request_uri_to_base_address_when_request_uri_is_null()
    {
        var baseAddress = new Uri("http://a.test/");
        var enricher = CreateEnricher(baseAddress: baseAddress);

        var request = new HttpRequestMessage { Method = HttpMethod.Get };
        var result = enricher.Enrich(request);

        Assert.Equal(baseAddress, result.RequestUri);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_combine_relative_uri_with_base_address_when_base_address_set()
    {
        var baseAddress = new Uri("http://a.test/");
        var enricher = CreateEnricher(baseAddress: baseAddress);

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));
        var result = enricher.Enrich(request);

        Assert.Equal(new Uri("http://a.test/ping"), result.RequestUri);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_change_absolute_uri_when_base_address_is_set()
    {
        var absoluteUri = new Uri("http://other.host/path");
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        var result = enricher.Enrich(request);

        Assert.Equal(absoluteUri, result.RequestUri);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_throw_when_uri_and_base_address_are_null()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage { Method = HttpMethod.Get };

        Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_throw_when_relative_uri_and_base_address_is_null()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_set_version_to_20_when_request_version_is_11_and_default_is_20()
    {
        var enricher = CreateEnricher(defaultVersion: HttpVersion.Version20);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_change_version_when_request_version_is_11_and_default_is_11()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version11
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version11, result.Version);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_override_version_when_explicit_v10_set()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version10
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version10, result.Version);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_override_version_when_explicit_v20_set()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/")
        {
            Version = HttpVersion.Version20
        };
        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_merge_default_header_when_default_request_headers_contains_x_foo()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "bar");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-Foo"));
        Assert.Contains("bar", result.Headers.GetValues("X-Foo"));
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_preserve_existing_header_when_default_and_request_have_same_header()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Foo", "default");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");
        var result = enricher.Enrich(request);

        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_merge_both_headers_when_two_default_headers_present()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-One", "1");
        defaultHeaders.TryAddWithoutValidation("X-Two", "2");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-One"));
        Assert.True(result.Headers.Contains("X-Two"));
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_add_no_headers_when_default_request_headers_empty()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        // RFC 9110 §6.6.1 — clients SHOULD NOT send Date; no headers auto-added.
        Assert.Empty(result.Headers);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_double_header_when_header_name_differs_only_in_case()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("x-foo", "default");

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.TryAddWithoutValidation("X-Foo", "existing");
        var result = enricher.Enrich(request);

        var values = new List<string>(result.Headers.GetValues("X-Foo"));
        Assert.Single(values);
        Assert.Equal("existing", values[0]);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_add_all_values_when_default_header_has_multiple_values()
    {
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Multi", ["a", "b"]);

        var enricher = CreateEnricher(defaultHeaders: defaultHeaders);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("X-Multi"));
        var values = new List<string>(result.Headers.GetValues("X-Multi"));
        Assert.Contains("a", values);
        Assert.Contains("b", values);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_enrich_all_requests_in_order_when_three_requests_in_sequence()
    {
        var baseAddress = new Uri("http://a.test/");
        var (holder, defaultHeaders) = CreateDefaultHeaders();
        using var _ = holder;
        defaultHeaders.TryAddWithoutValidation("X-Default", "yes");

        var enricher = CreateEnricher(baseAddress: baseAddress, defaultHeaders: defaultHeaders);

        var req1 = new HttpRequestMessage(HttpMethod.Get, new Uri("/one", UriKind.Relative));
        var req2 = new HttpRequestMessage(HttpMethod.Get, new Uri("/two", UriKind.Relative));
        var req3 = new HttpRequestMessage(HttpMethod.Get, new Uri("/three", UriKind.Relative));

        var results = new List<HttpRequestMessage>
        {
            enricher.Enrich(req1),
            enricher.Enrich(req2),
            enricher.Enrich(req3)
        };

        Assert.Equal(3, results.Count);

        Assert.Equal(new Uri("http://a.test/one"), results[0].RequestUri);
        Assert.Equal(new Uri("http://a.test/two"), results[1].RequestUri);
        Assert.Equal(new Uri("http://a.test/three"), results[2].RequestUri);

        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Default"));
            Assert.Contains("yes", result.Headers.GetValues("X-Default"));
        }
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_not_add_date_when_no_date_header()
    {
        var enricher = CreateEnricher();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        var result = enricher.Enrich(request);

        Assert.False(result.Headers.Date.HasValue);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_preserve_date_when_already_present()
    {
        var enricher = CreateEnricher();

        var existingDate = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.Date = existingDate;
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Date.HasValue);
        Assert.Equal(existingDate, result.Headers.Date!.Value);
    }

    [Fact(Timeout = 5_000)]
    public void RequestEnricher_should_preserve_explicit_date_when_caller_sets_it()
    {
        var enricher = CreateEnricher();

        var expectedDate = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://a.test/");
        request.Headers.Date = expectedDate;
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Date.HasValue);
        Assert.Equal(expectedDate, result.Headers.Date!.Value);
    }
}