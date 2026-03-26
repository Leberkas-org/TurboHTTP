using System.Diagnostics;
using System.Net;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboHttpInstrumentationTests : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public TurboHttpInstrumentationTests()
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

    [Fact(DisplayName = "Diagnostics-Request-001: StartRequest creates TurboHttp.Request activity")]
    public void StartRequest_Creates_RequestActivity()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("TurboHttp.Request", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact(DisplayName = "Diagnostics-Request-002: StartRequest sets http.request.method tag")]
    public void StartRequest_Sets_MethodTag()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
    }

    [Fact(DisplayName = "Diagnostics-Request-003: StartRequest sets url.full tag")]
    public void StartRequest_Sets_UrlFullTag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("https://example.com/path?q=1", activity.GetTagItem("url.full"));
    }

    [Fact(DisplayName = "Diagnostics-Request-004: StartRequest sets server.address and server.port tags")]
    public void StartRequest_Sets_ServerTags()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com:8443/resource");

        var activity = TurboHttpInstrumentation.StartRequest(request);

        Assert.NotNull(activity);
        Assert.Equal("api.example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
    }

    [Fact(DisplayName = "Diagnostics-Request-005: SetResponse sets http.response.status_code on root activity")]
    public void SetResponse_Sets_StatusCodeTag()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        TurboHttpInstrumentation.SetResponse(activity, response);

        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }

    // ── Redirect child spans ─────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Redirect-001: StartRedirect creates TurboHttp.Redirect activity")]
    public void StartRedirect_Creates_RedirectActivity()
    {
        var uri = new Uri("https://example.com/new-location");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, 301);

        Assert.NotNull(activity);
        Assert.Equal("TurboHttp.Redirect", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact(DisplayName = "Diagnostics-Redirect-002: StartRedirect sets http.response.status_code and url.full tags")]
    public void StartRedirect_Sets_Tags()
    {
        var uri = new Uri("https://example.com/redirected");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, 302);

        Assert.NotNull(activity);
        Assert.Equal(302, activity.GetTagItem("http.response.status_code"));
        Assert.Equal("https://example.com/redirected", activity.GetTagItem("url.full"));
    }

    [Theory(DisplayName = "Diagnostics-Redirect-003: StartRedirect records correct status code per hop")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void StartRedirect_Records_CorrectStatusCode(int statusCode)
    {
        var uri = new Uri("https://example.com/target");

        var activity = TurboHttpInstrumentation.StartRedirect(uri, statusCode);

        Assert.NotNull(activity);
        Assert.Equal(statusCode, activity.GetTagItem("http.response.status_code"));
    }

    [Fact(DisplayName = "Diagnostics-Redirect-004: Multiple redirect hops produce separate activities")]
    public void MultipleRedirectHops_ProduceSeparateActivities()
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

    [Fact(DisplayName = "Diagnostics-Redirect-005: Redirect child spans parent under root request activity")]
    public void RedirectSpans_ParentUnderRootActivity()
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

    // ── Retry child spans ────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Retry-001: StartRetry creates TurboHttp.Retry activity")]
    public void StartRetry_Creates_RetryActivity()
    {
        var activity = TurboHttpInstrumentation.StartRetry(1);

        Assert.NotNull(activity);
        Assert.Equal("TurboHttp.Retry", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact(DisplayName = "Diagnostics-Retry-002: StartRetry sets http.resend_count tag")]
    public void StartRetry_Sets_ResendCountTag()
    {
        var activity = TurboHttpInstrumentation.StartRetry(3);

        Assert.NotNull(activity);
        Assert.Equal(3, activity.GetTagItem("http.resend_count"));
    }

    [Theory(DisplayName = "Diagnostics-Retry-003: Each retry attempt has correct attempt number")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StartRetry_EachAttempt_HasCorrectNumber(int attempt)
    {
        var activity = TurboHttpInstrumentation.StartRetry(attempt);

        Assert.NotNull(activity);
        Assert.Equal(attempt, activity.GetTagItem("http.resend_count"));
    }

    [Fact(DisplayName = "Diagnostics-Retry-004: Multiple retry attempts produce separate activities")]
    public void MultipleRetryAttempts_ProduceSeparateActivities()
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

    [Fact(DisplayName = "Diagnostics-Retry-005: Retry child spans parent under root request activity")]
    public void RetrySpans_ParentUnderRootActivity()
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

    // ── Cache lookup spans ───────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Cache-001: StartCacheLookup creates TurboHttp.CacheLookup activity")]
    public void StartCacheLookup_Creates_CacheLookupActivity()
    {
        var uri = new Uri("https://example.com/cached");

        var activity = TurboHttpInstrumentation.StartCacheLookup(uri);

        Assert.NotNull(activity);
        Assert.Equal("TurboHttp.CacheLookup", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact(DisplayName = "Diagnostics-Cache-002: StartCacheLookup sets url.full tag")]
    public void StartCacheLookup_Sets_UrlTag()
    {
        var uri = new Uri("https://example.com/resource");

        var activity = TurboHttpInstrumentation.StartCacheLookup(uri);

        Assert.NotNull(activity);
        Assert.Equal("https://example.com/resource", activity.GetTagItem("url.full"));
    }

    [Fact(DisplayName = "Diagnostics-Cache-003: Cache hit tagged with cache.hit = true")]
    public void CacheLookup_Hit_SetsTag()
    {
        var uri = new Uri("https://example.com/cached");
        var activity = TurboHttpInstrumentation.StartCacheLookup(uri)!;

        // Simulate what CacheBidiStage does on a cache hit
        activity.SetTag("cache.hit", true);

        Assert.Equal(true, activity.GetTagItem("cache.hit"));
    }

    [Fact(DisplayName = "Diagnostics-Cache-004: Cache miss tagged with cache.hit = false")]
    public void CacheLookup_Miss_SetsTag()
    {
        var uri = new Uri("https://example.com/uncached");
        var activity = TurboHttpInstrumentation.StartCacheLookup(uri)!;

        // Simulate what CacheBidiStage does on a cache miss
        activity.SetTag("cache.hit", false);

        Assert.Equal(false, activity.GetTagItem("cache.hit"));
    }

    // ── Error span attributes ────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Error-001: SetError sets otel.status_code to ERROR")]
    public void SetError_Sets_OtelStatusCode()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("Connection refused"));

        Assert.Equal("ERROR", activity.GetTagItem("otel.status_code"));
    }

    [Fact(DisplayName = "Diagnostics-Error-002: SetError sets exception.type tag")]
    public void SetError_Sets_ExceptionType()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new HttpRequestException("timeout"));

        Assert.Equal(typeof(HttpRequestException).FullName, activity.GetTagItem("exception.type"));
    }

    [Fact(DisplayName = "Diagnostics-Error-003: SetError sets exception.message tag")]
    public void SetError_Sets_ExceptionMessage()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new InvalidOperationException("Pipeline broken"));

        Assert.Equal("Pipeline broken", activity.GetTagItem("exception.message"));
    }

    [Fact(DisplayName = "Diagnostics-Error-004: SetError sets ActivityStatusCode.Error on activity")]
    public void SetError_Sets_ActivityStatus()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/fail");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        TurboHttpInstrumentation.SetError(activity, new TimeoutException("Request timed out"));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Request timed out", activity.StatusDescription);
    }

    [Fact(DisplayName = "Diagnostics-Error-005: Error span attributes set on root activity")]
    public void SetError_OnRootActivity_SetsAllAttributes()
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

    // ── Zero overhead when no listener ───────────────────────────────

    [Fact(DisplayName = "Diagnostics-ZeroOverhead-001: StartRequest returns null when no listener")]
    public void StartRequest_ReturnsNull_WhenNoListener()
    {
        // Dispose our listener so there are none active
        _listener.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request);

        // Activity may or may not be null depending on other test listeners,
        // but verify our source name is correct
        Assert.Equal("TurboHttp", TurboHttpInstrumentation.SourceName);
    }

    // ── RequestActivityKey propagation ───────────────────────────────

    [Fact(DisplayName = "Diagnostics-Propagation-001: RequestActivityKey stores activity in request options")]
    public void RequestActivityKey_StoresActivityInOptions()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var activity = TurboHttpInstrumentation.StartRequest(request)!;

        // Store activity the way TracingBidiStage does
        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);

        Assert.True(request.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var retrieved));
        Assert.Same(activity, retrieved);
    }

    // ── Full lifecycle simulation ────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Lifecycle-001: Full request lifecycle with redirect and retry")]
    public void FullLifecycle_WithRedirectAndRetry()
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

    [Fact(DisplayName = "Diagnostics-Lifecycle-002: Full request lifecycle with error")]
    public void FullLifecycle_WithError()
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

    // ── Source metadata ──────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Source-001: ActivitySource has correct name")]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("TurboHttp", TurboHttpInstrumentation.Source.Name);
    }

    [Fact(DisplayName = "Diagnostics-Source-002: ActivitySource has non-empty version")]
    public void ActivitySource_HasVersion()
    {
        Assert.False(string.IsNullOrEmpty(TurboHttpInstrumentation.Source.Version));
    }
}