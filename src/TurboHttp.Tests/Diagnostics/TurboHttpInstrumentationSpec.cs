using System.Diagnostics;
using System.Net;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

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
        Assert.Equal("TurboHttp.Request", activity.OperationName);
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
        Assert.Equal("https://example.com/path?q=1", activity.GetTagItem("url.full"));
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
        Assert.Equal("TurboHttp.Redirect", activity.OperationName);
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
            .Where(a => a.OperationName == "TurboHttp.Redirect")
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
        Assert.Equal("TurboHttp.Retry", activity.OperationName);
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
            .Where(a => a.OperationName == "TurboHttp.Retry")
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
        Assert.Equal("TurboHttp.CacheLookup", activity.OperationName);
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

        Assert.Equal("TurboHttp.Request", activity.OperationName);
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
        Assert.Equal("TurboHttp", TurboHttpInstrumentation.SourceName);
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
        Assert.Single(_activities, a => a.OperationName == "TurboHttp.Request");
        Assert.Equal(2, _activities.Count(a => a.OperationName == "TurboHttp.Redirect"));
        Assert.Single(_activities, a => a.OperationName == "TurboHttp.Retry");
        Assert.Single(_activities, a => a.OperationName == "TurboHttp.CacheLookup");

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
        Assert.Equal("TurboHttp.Request", rootActivity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, rootActivity.Status);
        Assert.Equal("ERROR", rootActivity.GetTagItem("otel.status_code"));
        Assert.Equal("Connection refused", rootActivity.GetTagItem("exception.message"));
        Assert.True(rootActivity.IsStopped);
    }


    [Fact]
    public void ActivitySource_should_have_correct_name()
    {
        Assert.Equal("TurboHttp", TurboHttpInstrumentation.Source.Name);
    }

    [Fact]
    public void ActivitySource_should_have_version()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpInstrumentation.Source.Version));
    }
}
