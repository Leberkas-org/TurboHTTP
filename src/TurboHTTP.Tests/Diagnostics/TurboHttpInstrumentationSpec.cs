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

    [Fact(Timeout = 5000)]
    public void StartRequest_should_create_request_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Request", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_method_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_url_full_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("https://example.com/path?*", activity.GetTagItem("url.full"));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_server_tags()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com:8443/resource");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("api.example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_should_set_status_code_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }

    [Fact(Timeout = 5000)]
    public void AddRedirectEvent_should_add_event_to_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var uri = new Uri("https://example.com/new-location");

        TurboHttpInstrumentation.AddRedirectEvent(activity, uri, 301);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.redirect");
        Assert.Equal(301, evt.Tags.First(t => t.Key == "http.response.status_code").Value);
        Assert.Equal("https://example.com/new-location", evt.Tags.First(t => t.Key == "url.full").Value);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void AddRedirectEvent_should_record_correct_status_code(int statusCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var uri = new Uri("https://example.com/target");

        TurboHttpInstrumentation.AddRedirectEvent(activity, uri, statusCode);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.redirect");
        Assert.Equal(statusCode, evt.Tags.First(t => t.Key == "http.response.status_code").Value);
    }

    [Fact(Timeout = 5000)]
    public void MultipleRedirectEvents_should_be_recorded_on_same_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.AddRedirectEvent(activity, new Uri("https://a.com/1"), 301);
        TurboHttpInstrumentation.AddRedirectEvent(activity, new Uri("https://b.com/2"), 302);
        TurboHttpInstrumentation.AddRedirectEvent(activity, new Uri("https://c.com/3"), 307);

        var redirectEvents = activity.Events.Where(e => e.Name == "http.redirect").ToList();
        Assert.Equal(3, redirectEvents.Count);
    }

    [Fact(Timeout = 5000)]
    public void AddRetryEvent_should_add_event_to_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.AddRetryEvent(activity, 1);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.retry");
        Assert.Equal(1, evt.Tags.First(t => t.Key == "http.resend_count").Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AddRetryEvent_should_have_correct_attempt_number(int attempt)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.AddRetryEvent(activity, attempt);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.retry");
        Assert.Equal(attempt, evt.Tags.First(t => t.Key == "http.resend_count").Value);
    }

    [Fact(Timeout = 5000)]
    public void MultipleRetryEvents_should_be_recorded_on_same_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.AddRetryEvent(activity, 1);
        TurboHttpInstrumentation.AddRetryEvent(activity, 2);

        var retryEvents = activity.Events.Where(e => e.Name == "http.retry").ToList();
        Assert.Equal(2, retryEvents.Count);
        Assert.Equal(1, retryEvents[0].Tags.First(t => t.Key == "http.resend_count").Value);
        Assert.Equal(2, retryEvents[1].Tags.First(t => t.Key == "http.resend_count").Value);
    }

    [Fact(Timeout = 5000)]
    public void AddCacheLookupEvent_should_add_event_to_activity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/cached");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var uri = new Uri("https://example.com/cached");

        TurboHttpInstrumentation.AddCacheLookupEvent(activity, uri, true);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.cache_lookup");
        Assert.Equal("https://example.com/cached", evt.Tags.First(t => t.Key == "url.full").Value);
        Assert.Equal(true, evt.Tags.First(t => t.Key == "cache.hit").Value);
    }

    [Fact(Timeout = 5000)]
    public void AddCacheLookupEvent_miss_should_set_hit_false()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/uncached");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var uri = new Uri("https://example.com/uncached");

        TurboHttpInstrumentation.AddCacheLookupEvent(activity, uri, false);

        var evt = Assert.Single(activity.Events, e => e.Name == "http.cache_lookup");
        Assert.Equal(false, evt.Tags.First(t => t.Key == "cache.hit").Value);
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_set_exception_type()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("timeout"));

        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("exception.type"));
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_set_exception_message()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new InvalidOperationException("Pipeline broken"));

        Assert.Equal("Pipeline broken", activity.GetTagItem("exception.message"));
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_set_activity_status()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new TimeoutException("Request timed out"));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Request timed out", activity.StatusDescription);
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_not_set_redundant_otel_status_code_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("fail"));

        Assert.Null(activity.GetTagItem("otel.status_code"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void SetError_on_root_activity_should_set_all_attributes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/error");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var exception = new HttpRequestException("Connection reset by peer");

        TurboHttpInstrumentation.SetError(activity, exception);

        Assert.Equal("TurboHTTP.Request", activity.OperationName);
        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("exception.type"));
        Assert.Equal("Connection reset by peer", activity.GetTagItem("exception.message"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_return_null_when_no_listener()
    {
        _listener.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        TurboHttpInstrumentation.StartRequest(request);

        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.SourceName);
    }

    [Fact(Timeout = 5000)]
    public void RequestActivityKey_should_store_activity_in_request_options()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);

        Assert.True(request.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var retrieved));
        Assert.Same(activity, retrieved);
    }

    [Fact(Timeout = 5000)]
    public void FullLifecycle_with_redirect_and_retry_events()
    {
        _activities.Clear();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/start");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request)!;
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, rootActivity);

        TurboHttpInstrumentation.AddRedirectEvent(rootActivity, new Uri("https://example.com/hop1"), 301);
        TurboHttpInstrumentation.AddRetryEvent(rootActivity, 1);
        TurboHttpInstrumentation.AddRedirectEvent(rootActivity, new Uri("https://example.com/hop2"), 302);
        TurboHttpInstrumentation.AddCacheLookupEvent(rootActivity, new Uri("https://example.com/hop2"), false);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(rootActivity, response);
        rootActivity.Stop();

        Assert.Single(_activities);
        var events = rootActivity.Events.ToList();
        Assert.Equal(4, events.Count);
        Assert.Equal(2, events.Count(e => e.Name == "http.redirect"));
        Assert.Single(events, e => e.Name == "http.retry");
        Assert.Single(events, e => e.Name == "http.cache_lookup");
        Assert.Equal(200, rootActivity.GetTagItem("http.response.status_code"));
    }

    [Fact(Timeout = 5000)]
    public void FullLifecycle_with_error()
    {
        _activities.Clear();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var rootActivity = TurboHttpInstrumentation.StartRequest(request)!;
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, rootActivity);

        var exception = new HttpRequestException("Connection refused");
        TurboHttpInstrumentation.SetError(rootActivity, exception);
        rootActivity.Stop();

        Assert.Single(_activities);
        Assert.Equal("TurboHTTP.Request", rootActivity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, rootActivity.Status);
        Assert.Equal("Connection refused", rootActivity.GetTagItem("exception.message"));
        Assert.True(rootActivity.IsStopped);
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_add_traceparent_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.True(request.Headers.Contains("traceparent"));
        var traceparent = request.Headers.GetValues("traceparent").Single();
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceparent);
        Assert.Contains(activity.TraceId.ToString(), traceparent);
        Assert.Contains(activity.SpanId.ToString(), traceparent);
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_add_tracestate_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        activity.TraceStateString = "vendor1=value1";

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.True(request.Headers.Contains("tracestate"));
        Assert.Equal("vendor1=value1", request.Headers.GetValues("tracestate").Single());
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_not_add_tracestate_when_absent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        Assert.False(request.Headers.Contains("tracestate"));
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_propagate_recorded_flag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        var traceparent = request.Headers.GetValues("traceparent").Single();
        Assert.EndsWith("-01", traceparent);
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_not_overwrite_existing_traceparent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/traced");
        request.Headers.TryAddWithoutValidation("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.InjectTraceContext(activity, request);

        var values = request.Headers.GetValues("traceparent").ToList();
        Assert.Contains("00-11111111111111111111111111111111-2222222222222222-01", values);
    }

    [Fact(Timeout = 5000)]
    public void ActivitySource_should_have_correct_name()
    {
        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.Source.Name);
    }

    [Fact(Timeout = 5000)]
    public void ActivitySource_should_have_version()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpInstrumentation.Source.Version));
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_should_replace_query_with_asterisk()
    {
        var uri = new Uri("https://example.com/path?secret=abc&token=xyz");
        Assert.Equal("https://example.com/path?*", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_should_preserve_url_without_query()
    {
        var uri = new Uri("https://example.com/path");
        Assert.Equal("https://example.com/path", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_should_strip_fragment()
    {
        var uri = new Uri("https://example.com/path#section");
        Assert.Equal("https://example.com/path", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_should_strip_fragment_and_redact_query()
    {
        var uri = new Uri("https://example.com/path?q=1#frag");
        Assert.Equal("https://example.com/path?*", TurboHttpInstrumentation.RedactUrl(uri));
    }

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

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_method_original_for_nonstandard()
    {
        var request = new HttpRequestMessage(new HttpMethod("PURGE"), "https://example.com/cache");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("_OTHER", activity.GetTagItem("http.request.method"));
        Assert.Equal("PURGE", activity.GetTagItem("http.request.method_original"));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_not_set_method_original_for_standard()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("GET", activity.GetTagItem("http.request.method"));
        Assert.Null(activity.GetTagItem("http.request.method_original"));
    }

    [Theory]
    [InlineData(1, 0, "1.0")]
    [InlineData(1, 1, "1.1")]
    [InlineData(2, 0, "2")]
    [InlineData(3, 0, "3")]
    public void FormatProtocolVersion_should_return_correct_format(int major, int minor, string expected)
    {
        Assert.Equal(expected, TurboHttpInstrumentation.FormatProtocolVersion(new Version(major, minor)));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_url_scheme_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("https", activity.GetTagItem("url.scheme"));
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_should_set_protocol_version_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = new Version(2, 0) };
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("2", activity.GetTagItem("network.protocol.version"));
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_should_set_error_type_for_4xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("404", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_should_set_error_type_for_5xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("500", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_should_not_set_error_for_2xx()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Null(activity.GetTagItem("error.type"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_set_error_type_tag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("fail"));

        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact(Timeout = 5000)]
    public void IsTracingActive_should_return_true_when_listener_present()
    {
        Assert.True(TurboHttpInstrumentation.IsTracingActive);
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_should_handle_empty_query()
    {
        var uri = new Uri("https://example.com/path?");
        Assert.Equal("https://example.com/path?*", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact(Timeout = 5000)]
    public void RedactUrl_with_complex_path_should_preserve_structure()
    {
        var uri = new Uri("https://api.example.com:8080/v1/users/123/profile?token=secret#top");
        Assert.Equal("https://api.example.com:8080/v1/users/123/profile?*", TurboHttpInstrumentation.RedactUrl(uri));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_with_get_no_uri_should_work()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        var activity = TurboHttpInstrumentation.StartRequest(request);
        Assert.NotNull(activity);
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_with_3xx_status_should_not_set_error()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Null(activity.GetTagItem("error.type"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact(Timeout = 5000)]
    public void NormalizeMethod_should_handle_lowercase_standard_methods()
    {
        Assert.Equal("GET", TurboHttpInstrumentation.NormalizeMethod("get"));
        Assert.Equal("POST", TurboHttpInstrumentation.NormalizeMethod("post"));
        Assert.Equal("PUT", TurboHttpInstrumentation.NormalizeMethod("put"));
    }

    [Fact(Timeout = 5000)]
    public void NormalizeMethod_should_handle_mixed_case()
    {
        Assert.Equal("GET", TurboHttpInstrumentation.NormalizeMethod("Get"));
        Assert.Equal("POST", TurboHttpInstrumentation.NormalizeMethod("PoSt"));
    }

    [Fact(Timeout = 5000)]
    public void StartRequest_should_set_url_scheme_for_http()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("http", activity.GetTagItem("url.scheme"));
    }

    [Fact(Timeout = 5000)]
    public void FormatProtocolVersion_should_handle_version_3_with_minor()
    {
        Assert.Equal("3", TurboHttpInstrumentation.FormatProtocolVersion(new Version(3, 1)));
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_handle_aggregate_exception()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;
        var ex = new AggregateException("Multiple failures");

        TurboHttpInstrumentation.SetError(activity, ex);

        Assert.Equal(typeof(AggregateException).FullName, activity.GetTagItem("error.type"));
        Assert.Equal("Multiple failures", activity.GetTagItem("exception.message"));
    }

    [Fact(Timeout = 5000)]
    public void InjectTraceContext_should_handle_activity_with_no_current_context()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var prev = Activity.Current;
        Activity.Current = null;
        try
        {
            TurboHttpInstrumentation.InjectTraceContext(activity, request);
            Assert.True(request.Headers.Contains("traceparent"));
        }
        finally
        {
            Activity.Current = prev;
        }
    }

    [Fact(Timeout = 5000)]
    public void SourceName_should_be_constant()
    {
        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.SourceName);
    }

    [Fact(Timeout = 5000)]
    public void Source_version_should_not_be_empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(TurboHttpInstrumentation.Source.Version));
    }

    [Fact(Timeout = 5000)]
    public void Source_should_be_disposable()
    {
        Assert.NotNull(TurboHttpInstrumentation.Source);
        Assert.Equal("TurboHTTP", TurboHttpInstrumentation.Source.Name);
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_with_http10_should_format_version_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = new Version(1, 0) };
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("1.0", activity.GetTagItem("network.protocol.version"));
    }

    [Fact(Timeout = 5000)]
    public void SetResponse_with_http11_should_format_version_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = new Version(1, 1) };
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal("1.1", activity.GetTagItem("network.protocol.version"));
    }
}
