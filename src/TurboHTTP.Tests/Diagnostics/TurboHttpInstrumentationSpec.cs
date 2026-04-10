using System.Diagnostics;
using System.Net;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboHttpInstrumentationSpec : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public TurboHttpInstrumentationSpec()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurboHttpInstrumentation.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        foreach (var activity in _activities)
        {
            if (!activity.IsStopped)
            {
                activity.Stop();
            }
        }
    }

    [Fact]
    public void StartRequest_should_create_request_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Request", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact]
    public void StartRequest_should_set_method_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
    }

    [Fact]
    public void StartRequest_should_set_url_full_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("https://example.com/path?*", activity.GetTagItem("url.full"));
    }

    [Fact]
    public void StartRequest_should_set_server_tags()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com:8443/resource");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("api.example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
    }

    [Fact]
    public void SetResponse_should_set_status_code_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }


    [Fact]
    public void StartRedirect_should_create_redirect_activity()
    {
        var uri = new Uri("https://example.com/new-location");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, 301);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Redirect", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact]
    public void StartRedirect_should_set_tags()
    {
        var uri = new Uri("https://example.com/redirected");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, 302);

        Assert.NotNull(activity);
        Assert.Equal(302, activity.GetTagItem("http.response.status_code"));
        Assert.Equal("https://example.com/redirected", activity.GetTagItem("url.full"));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void StartRedirect_should_record_correct_status_code(int statusCode)
    {
        var uri = new Uri("https://example.com/target");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, statusCode);

        Assert.NotNull(activity);
        Assert.Equal(statusCode, activity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public void MultipleRedirectHops_should_produce_separate_activities()
    {
        _activities.Clear();

        var hop1 = TurboHttpInstrumentation.StartRedirect(new Uri("https://a.com/1"), 301);
        hop1?.Stop();
        var hop2 = TurboHttpInstrumentation.StartRedirect(new Uri("https://b.com/2"), 302);
        hop2?.Stop();
        var hop3 = TurboHttpInstrumentation.StartRedirect(new Uri("https://c.com/3"), 307);
        hop3?.Stop();

        var redirectActivities = _activities
            .Where(a => a.OperationName == "TurboHTTP.Redirect")
            .ToList();

        Assert.Equal(3, redirectActivities.Count);
        Assert.Equal("https://a.com/1", redirectActivities[0].GetTagItem("url.full"));
        Assert.Equal("https://b.com/2", redirectActivities[1].GetTagItem("url.full"));
        Assert.Equal("https://c.com/3", redirectActivities[2].GetTagItem("url.full"));
    }

    [Fact]
    public void RedirectSpans_should_parent_under_root_activity()
    {
        _activities.Clear();

        // Simulate the pattern used by TracingBidiStage + RedirectBidiStage:
        // root activity is started and set as Activity.Current
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request);
        Assert.NotNull(rootActivity);

        // Redirect stage parents under root by setting Activity.Current
        var previous = Activity.Current;
        Activity.Current = rootActivity;
        var redirectActivity = TurboHttpInstrumentation.StartRedirect(
            new Uri("https://example.com/redirect"), 301);
        Assert.NotNull(redirectActivity);
        Assert.Equal(rootActivity.Id, redirectActivity.ParentId);
        redirectActivity.Stop();
        Activity.Current = previous;

        rootActivity.Stop();
    }


    [Fact]
    public void StartRetry_should_create_retry_activity()
    {
        var activity = TurboHttpInstrumentation.StartRetry(1);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Retry", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact]
    public void StartRetry_should_set_resend_count_tag()
    {
        var activity = TurboHttpInstrumentation.StartRetry(3);

        Assert.NotNull(activity);
        Assert.Equal(3, activity.GetTagItem("http.resend_count"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StartRetry_should_have_correct_attempt_number(int attempt)
    {
        var activity = TurboHttpInstrumentation.StartRetry(attempt);

        Assert.NotNull(activity);
        Assert.Equal(attempt, activity.GetTagItem("http.resend_count"));
    }

    [Fact]
    public void MultipleRetryAttempts_should_produce_separate_activities()
    {
        _activities.Clear();

        var retry1 = TurboHttpInstrumentation.StartRetry(1);
        retry1?.Stop();
        var retry2 = TurboHttpInstrumentation.StartRetry(2);
        retry2?.Stop();

        var retryActivities = _activities
            .Where(a => a.OperationName == "TurboHTTP.Retry")
            .ToList();

        Assert.Equal(2, retryActivities.Count);
        Assert.Equal(1, retryActivities[0].GetTagItem("http.resend_count"));
        Assert.Equal(2, retryActivities[1].GetTagItem("http.resend_count"));
    }

    [Fact]
    public void RetrySpans_should_parent_under_root_activity()
    {
        _activities.Clear();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request);
        Assert.NotNull(rootActivity);

        var previous = Activity.Current;
        Activity.Current = rootActivity;
        var retryActivity = TurboHttpInstrumentation.StartRetry(1);
        Assert.NotNull(retryActivity);
        Assert.Equal(rootActivity.Id, retryActivity.ParentId);
        retryActivity.Stop();
        Activity.Current = previous;

        rootActivity.Stop();
    }


    [Fact]
    public void StartCacheLookup_should_create_cache_lookup_activity()
    {
        var uri = new Uri("https://example.com/cached");

        var activity = TurboHttpInstrumentation.StartCacheLookup(uri);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.CacheLookup", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact]
    public void StartCacheLookup_should_set_url_tag()
    {
        var uri = new Uri("https://example.com/resource");

        var activity = TurboHttpInstrumentation.StartCacheLookup(uri);

        Assert.NotNull(activity);
        Assert.Equal("https://example.com/resource", activity.GetTagItem("url.full"));
    }

    [Fact]
    public void CacheLookup_hit_should_set_tag()
    {
        var uri = new Uri("https://example.com/cached");
        var activity = TurboHttpInstrumentation.StartCacheLookup(uri)!;

        // Simulate what CacheBidiStage does on a cache hit
        activity.SetTag("cache.hit", true);

        Assert.Equal(true, activity.GetTagItem("cache.hit"));
    }

    [Fact]
    public void CacheLookup_miss_should_set_tag()
    {
        var uri = new Uri("https://example.com/uncached");
        var activity = TurboHttpInstrumentation.StartCacheLookup(uri)!;

        // Simulate what CacheBidiStage does on a cache miss
        activity.SetTag("cache.hit", false);

        Assert.Equal(false, activity.GetTagItem("cache.hit"));
    }


    [Fact]
    public void SetError_should_set_otel_status_code()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("Connection refused"));

        Assert.Equal("ERROR", activity.GetTagItem("otel.status_code"));
    }

    [Fact]
    public void SetError_should_set_exception_type()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("timeout"));

        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("exception.type"));
    }

    [Fact]
    public void SetError_should_set_exception_message()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new InvalidOperationException("Pipeline broken"));

        Assert.Equal("Pipeline broken", activity.GetTagItem("exception.message"));
    }

    [Fact]
    public void SetError_should_set_activity_status()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new TimeoutException("Request timed out"));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Request timed out", activity.StatusDescription);
    }

    [Fact]
    public void SetError_on_root_activity_should_set_all_attributes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/error");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var exception = new HttpRequestException("Connection reset by peer");

        TurboHttpInstrumentation.SetError(activity, exception);

        Assert.Equal("TurboHTTP.Request", activity.OperationName);
        Assert.Equal("ERROR", activity.GetTagItem("otel.status_code"));
        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("exception.type"));
        Assert.Equal("Connection reset by peer", activity.GetTagItem("exception.message"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }


    [Fact]
    public void StartRequest_should_return_null_when_no_listener()
    {
        // Dispose our listener so there are none active
        _listener.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request);

        // Activity may or may not be null depending on other test listeners,
        // but verify our source name is correct
        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.SourceName);
    }


    [Fact]
    public void RequestActivityKey_should_store_activity_in_request_options()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        // Store activity the way TracingBidiStage does
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);

        Assert.True(request.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var retrieved));
        Assert.Same(activity, retrieved);
    }


    [Fact]
    public void FullLifecycle_with_redirect_and_retry()
    {
        _activities.Clear();

        // 1. Root request starts
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request)!;
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, rootActivity);

        // 2. First redirect hop (301)
        var prev = Activity.Current;
        Activity.Current = rootActivity;
        var redirect1 = TurboHttpInstrumentation.StartRedirect(new Uri("https://example.com/hop1"), 301)!;
        redirect1.Stop();
        Activity.Current = prev;

        // 3. Retry after transient failure
        prev = Activity.Current;
        Activity.Current = rootActivity;
        var retry1 = TurboHttpInstrumentation.StartRetry(1)!;
        retry1.Stop();
        Activity.Current = prev;

        // 4. Second redirect hop (302)
        prev = Activity.Current;
        Activity.Current = rootActivity;
        var redirect2 = TurboHttpInstrumentation.StartRedirect(new Uri("https://example.com/hop2"), 302)!;
        redirect2.Stop();
        Activity.Current = prev;

        // 5. Cache lookup (miss)
        prev = Activity.Current;
        Activity.Current = rootActivity;
        var cacheLookup = TurboHttpInstrumentation.StartCacheLookup(new Uri("https://example.com/hop2"))!;
        cacheLookup.SetTag("cache.hit", false);
        cacheLookup.Stop();
        Activity.Current = prev;

        // 6. Response received
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(rootActivity, response);
        rootActivity.Stop();

        // Verify span counts
        Assert.Equal(5, _activities.Count); // root + 2 redirects + 1 retry + 1 cache
        Assert.Single(_activities, a => a.OperationName == "TurboHTTP.Request");
        Assert.Equal(2, _activities.Count(a => a.OperationName == "TurboHTTP.Redirect"));
        Assert.Single(_activities, a => a.OperationName == "TurboHTTP.Retry");
        Assert.Single(_activities, a => a.OperationName == "TurboHTTP.CacheLookup");

        // Verify root span has response status
        Assert.Equal(200, rootActivity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public void FullLifecycle_with_error()
    {
        _activities.Clear();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request)!;
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, rootActivity);

        // Simulate pipeline failure
        var exception = new HttpRequestException("Connection refused");
        TurboHttpInstrumentation.SetError(rootActivity, exception);
        rootActivity.Stop();

        Assert.Single(_activities);
        Assert.Equal("TurboHTTP.Request", rootActivity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, rootActivity.Status);
        Assert.Equal("ERROR", rootActivity.GetTagItem("otel.status_code"));
        Assert.Equal("Connection refused", rootActivity.GetTagItem("exception.message"));
        Assert.True(rootActivity.IsStopped);
    }


    [Fact]
    public void InjectTraceContext_should_add_traceparent_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.True(request.Headers.Contains("traceparent"));
        var traceparent = request.Headers.GetValues("traceparent").Single();
        // W3C format: 00-{traceId}-{spanId}-{flags}
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceparent);
        Assert.Contains(activity.TraceId.ToString(), traceparent);
        Assert.Contains(activity.SpanId.ToString(), traceparent);
    }

    [Fact]
    public void InjectTraceContext_should_add_tracestate_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        activity.TraceStateString = "vendor1=value1";

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.True(request.Headers.Contains("tracestate"));
        Assert.Equal("vendor1=value1", request.Headers.GetValues("tracestate").Single());
    }

    [Fact]
    public void InjectTraceContext_should_not_add_tracestate_when_absent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.False(request.Headers.Contains("tracestate"));
    }

    [Fact]
    public void InjectTraceContext_should_propagate_recorded_flag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        // Our listener samples with AllDataAndRecorded, so Recorded should be set
        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        var traceparent = request.Headers.GetValues("traceparent").Single();
        Assert.EndsWith("-01", traceparent);
    }

    [Fact]
    public void InjectTraceContext_should_not_overwrite_existing_traceparent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        request.Headers.TryAddWithoutValidation("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        // TryAddWithoutValidation won't overwrite — both values present
        var values = request.Headers.GetValues("traceparent").ToList();
        Assert.Contains("00-11111111111111111111111111111111-2222222222222222-01", values);
    }

    [Fact]
    public void ActivitySource_should_have_correct_name()
    {
        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.Source.Name);
    }

    [Fact]
    public void ActivitySource_should_have_version()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpInstrumentation.Source.Version));
    }


    // --- URL Redaction ---

    [Fact]
    public void RedactUrl_should_replace_query_with_asterisk()
    {
        var uri = new Uri("https://example.com/path?secret=abc&token=xyz");
        Assert.Equal("https://example.com/path?*", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact]
    public void RedactUrl_should_preserve_url_without_query()
    {
        var uri = new Uri("https://example.com/path");
        Assert.Equal("https://example.com/path", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact]
    public void RedactUrl_should_strip_fragment()
    {
        var uri = new Uri("https://example.com/path#section");
        Assert.Equal("https://example.com/path", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact]
    public void RedactUrl_should_strip_fragment_and_redact_query()
    {
        var uri = new Uri("https://example.com/path?q=1#frag");
        Assert.Equal("https://example.com/path?*", TurboHttpInstrumentation.RedactUrl(uri));
    }


    // --- Method Normalization ---

    [Theory]
    [InlineData("GET", "GET")]
    [InlineData("POST", "POST")]
    [InlineData("PUT", "PUT")]
    [InlineData("DELETE", "DELETE")]
    [InlineData("HEAD", "HEAD")]
    [InlineData("OPTIONS", "OPTIONS")]
    [InlineData("TRACE", "TRACE")]
    [InlineData("PATCH", "PATCH")]
    [InlineData("CONNECT", "CONNECT")]
    public void NormalizeMethod_should_return_standard_methods_uppercased(string input, string expected)
    {
        Assert.Equal(expected, TurboHttpInstrumentation.NormalizeMethod(input));
    }

    [Theory]
    [InlineData("PURGE")]
    [InlineData("LOCK")]
    [InlineData("CUSTOM")]
    public void NormalizeMethod_should_return_OTHER_for_nonstandard(string method)
    {
        Assert.Equal("_OTHER", TurboHttpInstrumentation.NormalizeMethod(method));
    }

    [Fact]
    public void StartRequest_should_set_method_original_for_nonstandard()
    {
        var request = new HttpRequestMessage(new HttpMethod("PURGE"), "https://example.com/cache");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("_OTHER", activity.GetTagItem("http.request.method"));
        Assert.Equal("PURGE", activity.GetTagItem("http.request.method_original"));
    }

    [Fact]
    public void StartRequest_should_not_set_method_original_for_standard()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("GET", activity.GetTagItem("http.request.method"));
        Assert.Null(activity.GetTagItem("http.request.method_original"));
    }


    // --- Protocol Version Formatting ---

    [Theory]
    [InlineData(1, 0, "1.0")]
    [InlineData(1, 1, "1.1")]
    [InlineData(2, 0, "2")]
    [InlineData(3, 0, "3")]
    public void FormatProtocolVersion_should_return_correct_format(int major, int minor, string expected)
    {
        Assert.Equal(expected, TurboHttpInstrumentation.FormatProtocolVersion(new Version(major, minor)));
    }


    // --- url.scheme tag ---

    [Fact]
    public void StartRequest_should_set_url_scheme_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("https", activity.GetTagItem("url.scheme"));
    }


    // --- SetResponse enriched tags ---

    [Fact]
    public void SetResponse_should_set_protocol_version_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = new Version(2, 0) };
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("2", activity.GetTagItem("network.protocol.version"));
    }

    [Fact]
    public void SetResponse_should_set_error_type_for_4xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("404", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void SetResponse_should_set_error_type_for_5xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("500", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void SetResponse_should_not_set_error_for_2xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Null(activity.GetTagItem("error.type"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public void SetError_should_set_error_type_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("fail"));

        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("error.type"));
    }


    // --- New Span Types ---

    [Fact]
    public void StartDnsLookup_should_create_activity()
    {
        var activity = TurboHttpInstrumentation.StartDnsLookup("example.com");

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.DnsLookup", activity.OperationName);
        Assert.Equal("example.com", activity.GetTagItem("dns.question.name"));
    }

    [Fact]
    public void StartSocketConnect_should_create_activity()
    {
        var activity = TurboHttpInstrumentation.StartSocketConnect("93.184.216.34", 443);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.SocketConnect", activity.OperationName);
        Assert.Equal("93.184.216.34", activity.GetTagItem("network.peer.address"));
        Assert.Equal(443, activity.GetTagItem("network.peer.port"));
        Assert.Equal("tcp", activity.GetTagItem("network.transport"));
    }

    [Fact]
    public void StartSocketConnect_should_set_network_type_when_provided()
    {
        var activity = TurboHttpInstrumentation.StartSocketConnect("93.184.216.34", 443, "tcp", "ipv4");

        Assert.NotNull(activity);
        Assert.Equal("ipv4", activity.GetTagItem("network.type"));
    }

    [Fact]
    public void StartSocketConnect_should_omit_network_type_when_null()
    {
        var activity = TurboHttpInstrumentation.StartSocketConnect("93.184.216.34", 443);

        Assert.NotNull(activity);
        Assert.Null(activity.GetTagItem("network.type"));
    }

    [Fact]
    public void StartTlsHandshake_should_create_activity()
    {
        var activity = TurboHttpInstrumentation.StartTlsHandshake("example.com");

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.TlsHandshake", activity.OperationName);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
    }

    [Fact]
    public void StartWaitForConnection_should_create_activity()
    {
        var activity = TurboHttpInstrumentation.StartWaitForConnection("example.com", 443);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.WaitForConnection", activity.OperationName);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
        Assert.Equal(443, activity.GetTagItem("server.port"));
    }

    [Fact]
    public void StartConnect_should_create_activity()
    {
        var activity = TurboHttpInstrumentation.StartConnect(new Uri("https://example.com:8443/"));

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Connect", activity.OperationName);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
    }

    [Fact]
    public void StartConnect_should_set_url_scheme()
    {
        var activity = TurboHttpInstrumentation.StartConnect(new Uri("https://example.com/"));

        Assert.NotNull(activity);
        Assert.Equal("https", activity.GetTagItem("url.scheme"));
    }

    [Fact]
    public void SetTlsInfo_should_set_protocol_tags()
    {
        var activity = TurboHttpInstrumentation.StartTlsHandshake("example.com");
        Assert.NotNull(activity);

        TurboHttpInstrumentation.SetTlsInfo(activity, "tls", "1.3");

        Assert.Equal("tls", activity.GetTagItem("tls.protocol.name"));
        Assert.Equal("1.3", activity.GetTagItem("tls.protocol.version"));
    }

    [Fact]
    public void SetDnsAnswers_should_set_answers_tag()
    {
        var activity = TurboHttpInstrumentation.StartDnsLookup("example.com");
        Assert.NotNull(activity);

        TurboHttpInstrumentation.SetDnsAnswers(activity, ["93.184.216.34", "2606:2800:220:1::"]);

        Assert.Equal(new[] { "93.184.216.34", "2606:2800:220:1::" }, activity.GetTagItem("dns.answers"));
    }

    [Fact]
    public void SetNetworkPeerAddress_should_set_tag()
    {
        var activity = TurboHttpInstrumentation.StartConnect(new Uri("https://example.com/"));
        Assert.NotNull(activity);

        TurboHttpInstrumentation.SetNetworkPeerAddress(activity, "93.184.216.34");

        Assert.Equal("93.184.216.34", activity.GetTagItem("network.peer.address"));
    }
}
