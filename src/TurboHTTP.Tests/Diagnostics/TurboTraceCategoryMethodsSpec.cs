using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboTraceCategoryMethodsSpec : IDisposable
{
    private sealed class MockTraceListener : ITurboTraceListener
    {
        public List<TraceEvent> Events { get; } = [];
        public bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category) => true;
        public void Write(in TraceEvent evt) => Events.Add(evt);
    }

    private readonly MockTraceListener _mock = new();

    public void Dispose()
    {
        TurboTrace.Disable();
    }

    #region Connection Category Tests

    [Fact(Timeout = 5000)]
    public void Connection_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Trace(this, "connection trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Connection, evt.Category);
        Assert.Equal("connection trace", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Connection_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Trace(this, "id={0}", 42);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("id=42", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Connection_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Debug(this, "debug msg");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Connection, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Connection_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Debug(this, "{0}:{1}", "host", 443);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("host:443", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Connection_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Info(this, "info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Connection, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Connection_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Info(this, "port={0}", 8080);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("port=8080", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Connection_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Warning(this, "warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Connection, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Connection_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Warning(this, "timeout {0}ms", 5000);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("timeout 5000ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Connection_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Error(this, "error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Connection, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Connection_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Connection.Error(this, "failed: {0}", "refused");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: refused", evt.FormatMessage());
    }

    #endregion

    #region Protocol Category Tests

    [Fact(Timeout = 5000)]
    public void Protocol_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Trace(this, "protocol trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Protocol, evt.Category);
        Assert.Equal("protocol trace", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Trace_with_object_arg_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Trace(this, "frame={0}", 12345);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("frame=12345", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Debug(this, "debug msg");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Protocol, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Debug(this, "type={0}", "HEADERS");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("type=HEADERS", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Info(this, "info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Protocol, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Info(this, "code={0}", 200);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("code=200", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Warning(this, "warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Protocol, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Warning_with_object_arg_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Warning(this, "delay {0}ms", 500);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("delay 500ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Error(this, "error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Protocol, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Protocol.Error(this, "failed: {0}", "timeout");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: timeout", evt.FormatMessage());
    }

    #endregion

    #region Request Category Tests

    [Fact(Timeout = 5000)]
    public void Request_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Trace(this, "req trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Request, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Request_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Trace(this, "method={0}", "POST");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("method=POST", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Request_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Debug(this, "req debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Request, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Request_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Debug(this, "url={0}", "https://example.com");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("url=https://example.com", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Request_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Info(this, "req info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Request, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Request_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Info(this, "size={0}", 1024);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("size=1024", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Request_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Warning(this, "req warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Request, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Request_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Warning(this, "retry {0}", 3);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("retry 3", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Request_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Error(this, "req error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Request, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Request_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Request.Error(this, "failed: {0}", "cancelled");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: cancelled", evt.FormatMessage());
    }

    #endregion

    #region Response Category Tests

    [Fact(Timeout = 5000)]
    public void Response_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Trace(this, "resp trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Response, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Response_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Trace(this, "status={0}", 200);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("status=200", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Response_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Debug(this, "resp debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Response, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Response_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Debug(this, "size={0}", 512);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("size=512", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Response_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Info(this, "resp info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Response, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Response_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Info(this, "code={0}", 404);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("code=404", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Response_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Warning(this, "resp warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Response, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Response_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Warning(this, "delay {0}ms", 1000);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("delay 1000ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Response_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Error(this, "resp error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Response, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Response_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Response.Error(this, "error: {0}", "timeout");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("error: timeout", evt.FormatMessage());
    }

    #endregion

    #region Cache Category Tests

    [Fact(Timeout = 5000)]
    public void Cache_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Trace(this, "cache trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Cache_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Trace(this, "key={0}", "url");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("key=url", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Cache_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Debug(this, "cache debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Cache_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Debug(this, "hit={0}", true);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("hit=True", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Cache_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Info(this, "cache info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Cache_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Info(this, "entries={0}", 42);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("entries=42", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Cache_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Warning(this, "cache warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Cache_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Warning(this, "expired {0}", "stale");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("expired stale", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Cache_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Error(this, "cache error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Cache_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Cache.Error(this, "failed: {0}", "corrupted");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: corrupted", evt.FormatMessage());
    }

    #endregion

    #region Redirect Category Tests

    [Fact(Timeout = 5000)]
    public void Redirect_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Trace(this, "redirect trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Redirect, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Trace(this, "code={0}", 301);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("code=301", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Debug(this, "redirect debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Redirect, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Debug(this, "location={0}", "/new");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("location=/new", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Info(this, "redirect info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Redirect, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Info(this, "count={0}", 2);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("count=2", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Warning(this, "redirect warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Redirect, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Warning(this, "limit {0}", 5);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("limit 5", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Error(this, "redirect error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Redirect, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Redirect.Error(this, "failed: {0}", "circular");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: circular", evt.FormatMessage());
    }

    #endregion

    #region Retry Category Tests

    [Fact(Timeout = 5000)]
    public void Retry_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Trace(this, "retry trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Retry, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Retry_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Trace(this, "attempt={0}", 1);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("attempt=1", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Retry_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Debug(this, "retry debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Retry, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Retry_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Debug(this, "delay={0}ms", 100);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("delay=100ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Retry_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Info(this, "retry info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Retry, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Retry_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Info(this, "reason={0}", "timeout");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("reason=timeout", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Retry_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Warning(this, "retry warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Retry, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Retry_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Warning(this, "backoff {0}ms", 500);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("backoff 500ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Retry_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Error(this, "retry error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Retry, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Retry_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Retry.Error(this, "failed: {0}", "exhausted");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("failed: exhausted", evt.FormatMessage());
    }

    #endregion

    #region Pool Category Tests

    [Fact(Timeout = 5000)]
    public void Pool_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Trace(this, "pool trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Pool, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Pool_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Trace(this, "size={0}", 10);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("size=10", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Pool_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Debug(this, "pool debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Pool, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Pool_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Debug(this, "available={0}", 8);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("available=8", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Pool_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Info(this, "pool info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Pool, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Pool_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Info(this, "size={0}", 16);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("size=16", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Pool_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Warning(this, "pool warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Pool, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Pool_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Warning(this, "leak {0}", "suspected");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("leak suspected", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Pool_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Error(this, "pool error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Pool, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Pool_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Pool.Error(this, "error: {0}", "exhausted");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("error: exhausted", evt.FormatMessage());
    }

    #endregion

    #region Transport Category Tests

    [Fact(Timeout = 5000)]
    public void Transport_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Trace(this, "transport trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Transport_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Trace(this, "protocol={0}", "TCP");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("protocol=TCP", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Transport_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Debug(this, "transport debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Transport_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Debug(this, "port={0}", 443);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("port=443", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Transport_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Info(this, "transport info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Transport_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Info(this, "bytes={0}", 4096);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("bytes=4096", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Transport_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Warning(this, "transport warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Transport_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Warning(this, "slow {0}ms", 2000);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("slow 2000ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Transport_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Error(this, "transport error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Transport_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Transport.Error(this, "error: {0}", "reset");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("error: reset", evt.FormatMessage());
    }

    #endregion

    #region Stream Category Tests

    [Fact(Timeout = 5000)]
    public void Stream_Trace_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Trace(this, "stream trace");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Trace, evt.Level);
        Assert.Equal(TurboTraceCategory.Stream, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Stream_Trace_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Trace(this, "id={0}", 123);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("id=123", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Stream_Debug_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Debug(this, "stream debug");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Debug, evt.Level);
        Assert.Equal(TurboTraceCategory.Stream, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Stream_Debug_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Debug(this, "buffer={0}", 2048);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("buffer=2048", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Stream_Info_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Info(this, "stream info");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Info, evt.Level);
        Assert.Equal(TurboTraceCategory.Stream, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Stream_Info_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Info(this, "stage={0}", "encoding");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("stage=encoding", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Stream_Warning_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Warning(this, "stream warn");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Stream, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Stream_Warning_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Warning(this, "backpressure {0}ms", 300);
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("backpressure 300ms", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Stream_Error_should_write_event()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Error(this, "stream error");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Error, evt.Level);
        Assert.Equal(TurboTraceCategory.Stream, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void Stream_Error_with_args_should_format()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Stream.Error(this, "error: {0}", "malformed");
        var evt = Assert.Single(_mock.Events);
        Assert.Equal("error: malformed", evt.FormatMessage());
    }

    #endregion
}
