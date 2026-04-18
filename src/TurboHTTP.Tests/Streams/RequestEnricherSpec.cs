using System.Net;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Streams;

/// <summary>
/// Tests for RequestEnricher, which enriches HTTP requests with defaults and validation.
/// </summary>
/// <remarks>
/// Type under test: <see cref="RequestEnricher"/>.
/// RFC 9110: HTTP Semantics request construction and validation.
/// </remarks>
public sealed class RequestEnricherSpec
{
    private static TurboRequestOptions DefaultOptions
    {
        get
        {
            var msg = new HttpRequestMessage();
            msg.Headers.TryAddWithoutValidation("User-Agent", "TestClient/1.0");
            return new TurboRequestOptions(
                BaseAddress: new Uri("https://example.com"),
                DefaultRequestHeaders: msg.Headers,
                DefaultRequestVersion: HttpVersion.Version11,
                DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
                Timeout: TimeSpan.FromSeconds(30),
                Credentials: null,
                PreAuthenticate: false
            );
        }
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_resolve_relative_uri_using_base_address()
    {
        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com:8443/api/"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "users");
        var result = enricher.Enrich(request);

        Assert.NotNull(result.RequestUri);
        Assert.Equal("https://example.com:8443/api/users", result.RequestUri.ToString());
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_throw_when_uri_relative_and_no_base_address()
    {
        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "/users");

        var ex = Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
        Assert.Contains("BaseAddress", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_throw_when_uri_null_and_no_base_address()
    {
        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.com/"));
        request.RequestUri = null;

        var ex = Assert.Throws<InvalidOperationException>(() => enricher.Enrich(request));
        Assert.Contains("BaseAddress", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_preserve_absolute_uri()
    {
        var options = DefaultOptions;
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://other.com/path");
        var result = enricher.Enrich(request);

        Assert.Equal("https://other.com/path", result.RequestUri!.ToString());
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_override_version_when_still_default()
    {
        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: new Version(2, 0),
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version11
        };
        var result = enricher.Enrich(request);

        Assert.Equal(new Version(2, 0), result.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_not_override_explicitly_set_version()
    {
        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: new Version(2, 0),
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = new Version(1, 0)
        };
        var result = enricher.Enrich(request);

        // Should not override if not at default
        Assert.Equal(new Version(1, 0), result.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_add_missing_default_headers()
    {
        var msg = new HttpRequestMessage();
        msg.Headers.Add("User-Agent", "TurboHTTP/1.0");
        msg.Headers.Add("Accept", "application/json");
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = enricher.Enrich(request);

        Assert.True(result.Headers.Contains("User-Agent"));
        Assert.True(result.Headers.Contains("Accept"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-5")]
    public void RequestEnricher_should_not_override_existing_default_headers()
    {
        var msg = new HttpRequestMessage();
        msg.Headers.Add("User-Agent", "Default");
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("User-Agent", "Custom");

        var result = enricher.Enrich(request);

        var userAgent = Assert.Single(result.Headers.GetValues("User-Agent"));
        Assert.Equal("Custom", userAgent);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.6.1")]
    public void RequestEnricher_should_inject_basic_auth_when_pre_authenticate_enabled()
    {
        var credentials = new NetworkCredential("user", "pass");
        var credentialsProvider = new CredentialsProvider(credentials);

        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: credentialsProvider,
            PreAuthenticate: true
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        var result = enricher.Enrich(request);

        Assert.NotNull(result.Headers.Authorization);
        Assert.Equal("Basic", result.Headers.Authorization.Scheme);
        Assert.True(!string.IsNullOrEmpty(result.Headers.Authorization.Parameter));
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.6.1")]
    public void RequestEnricher_should_not_inject_auth_when_pre_authenticate_disabled()
    {
        var credentials = new NetworkCredential("user", "pass");
        var credentialsProvider = new CredentialsProvider(credentials);

        var msg = new HttpRequestMessage();
        var options = new TurboRequestOptions(
            BaseAddress: new Uri("https://example.com"),
            DefaultRequestHeaders: msg.Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: credentialsProvider,
            PreAuthenticate: false
        );
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        var result = enricher.Enrich(request);

        Assert.Null(result.Headers.Authorization);
    }

    [Fact]
    [Trait("RFC", "RFC9110-10.5")]
    public void RequestEnricher_should_remove_referer_on_https_to_http_downgrade()
    {
        var options = DefaultOptions;
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            RequestUri = new Uri("http://example.com/page")
        };
        request.Headers.Add("Referer", "https://secure.example.com/previous");

        var result = enricher.Enrich(request);

        Assert.False(result.Headers.Contains("Referer"));
    }

    [Fact]
    [Trait("RFC", "RFC9110-10.5")]
    public void RequestEnricher_should_strip_fragment_from_referer()
    {
        var options = DefaultOptions;
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        request.Headers.Add("Referer", "https://example.com/source#section");

        var result = enricher.Enrich(request);

        var refererValue = Assert.Single(result.Headers.GetValues("Referer"));
        Assert.DoesNotContain("#", refererValue);
    }

    [Fact]
    [Trait("RFC", "RFC9110-10.5")]
    public void RequestEnricher_should_strip_userinfo_from_referer()
    {
        var options = DefaultOptions;
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        request.Headers.Add("Referer", "https://user:pass@example.com/source");

        var result = enricher.Enrich(request);

        var refererValue = Assert.Single(result.Headers.GetValues("Referer"));
        Assert.DoesNotContain("user:pass", refererValue);
    }

    [Fact]
    [Trait("RFC", "RFC9110-13.1.5")]
    public void RequestEnricher_should_validate_if_range()
    {
        var options = DefaultOptions;
        var enricher = new RequestEnricher(() => options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/large-file");
        request.Headers.Add("If-Range", "\"abc123\"");
        request.Headers.Add("Range", "bytes=0-99");

        var result = enricher.Enrich(request);

        // Validation should pass (ETag format is correct)
        Assert.True(result.Headers.Contains("If-Range"));
    }

    private sealed class CredentialsProvider(NetworkCredential credential) : ICredentials
    {
        public NetworkCredential GetCredential(Uri uri, string authType) => credential;
    }
}
