using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboTraceSpec : IDisposable
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

    [Fact(Timeout = 5000)]
    public void FormatMessage_should_return_template_when_no_args()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "Hello world");

        Assert.Equal("Hello world", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void FormatMessage_should_format_single_arg_correctly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "Value: {0}", 42, null, null);

        Assert.Equal("Value: 42", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void FormatMessage_should_format_two_args_correctly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "{0} = {1}", "key", "value", null);

        Assert.Equal("key = value", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void FormatMessage_should_format_three_args_correctly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "{0}/{1}/{2}", "a", "b", "c");

        Assert.Equal("a/b/c", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void TraceEvent_should_capture_timestamp()
    {
        var before = Stopwatch.GetTimestamp();
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "msg");
        var after = Stopwatch.GetTimestamp();

        Assert.InRange(evt.TimestampTicks, before, after);
    }

    [Fact(Timeout = 5000)]
    public void TraceEvent_should_store_level_and_category()
    {
        var evt = new TraceEvent(
            0, TurboTraceLevel.Warning, TurboTraceCategory.Cache,
            "Test", 0, "msg");

        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Cache, evt.Category);
    }

    [Fact(Timeout = 5000)]
    public void TraceEvent_should_store_source_type()
    {
        var evt = new TraceEvent(
            0, TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "TurboTraceTests", 0, "msg");

        Assert.Equal("TurboTraceTests", evt.SourceType);
    }

    [Fact(Timeout = 5000)]
    public void TraceEvent_should_store_source_hash()
    {
        var hash = GetHashCode();
        var evt = new TraceEvent(
            0, TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "TurboTraceTests", hash, "msg");

        Assert.Equal(hash, evt.SourceHash);
    }

    [Fact(Timeout = 5000)]
    public void TraceEvent_should_be_readonly_struct()
    {
        var type = typeof(TraceEvent);
        Assert.True(type.IsValueType);
        Assert.True(type.GetCustomAttributes(typeof(IsReadOnlyAttribute), false).Length > 0);
    }

    [Fact(Timeout = 5000)]
    public void ShouldTrace_should_return_false_when_no_listener()
    {
        TurboTrace.Disable();

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void ShouldTrace_should_return_true_when_enabled()
    {
        TurboTrace.Configure(_mock);

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void ShouldTrace_should_return_false_when_below_minimum()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All, TurboTraceLevel.Warning);

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void Configure_should_enable_tracing()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Single(_mock.Events);
    }

    [Fact(Timeout = 5000)]
    public void Disable_should_stop_tracing()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Disable();

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(Timeout = 5000)]
    public void ProtocolDebug_should_write_correct_category()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceCategory.Protocol, _mock.Events[0].Category);
    }

    [Fact(Timeout = 5000)]
    public void RequestWarning_should_write_correct_level()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Request.Warning(this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, _mock.Events[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void TraceCall_should_produce_no_event_when_no_listener()
    {
        TurboTrace.Disable();

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(Timeout = 5000)]
    public void CategoryFiltering_should_work_with_bitwise_flags()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.Protocol);

        TurboTrace.Protocol.Debug(this, "yes");
        TurboTrace.Request.Debug(this, "no");

        Assert.Single(_mock.Events);
        Assert.All(_mock.Events, e =>
            Assert.True(e.Category == TurboTraceCategory.Protocol));
    }

    [Fact(Timeout = 5000)]
    public void LevelFiltering_should_work_with_minimum_level()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All, TurboTraceLevel.Warning);

        TurboTrace.Protocol.Debug(this, "no");
        TurboTrace.Protocol.Info(this, "no");
        TurboTrace.Protocol.Warning(this, "yes");
        TurboTrace.Protocol.Error(this, "yes");

        Assert.Equal(2, _mock.Events.Count);
        Assert.Equal(TurboTraceLevel.Warning, _mock.Events[0].Level);
        Assert.Equal(TurboTraceLevel.Error, _mock.Events[1].Level);
    }

    [Theory]
    [InlineData(TurboTraceCategory.Protocol)]
    [InlineData(TurboTraceCategory.Request)]
    [InlineData(TurboTraceCategory.Cache)]
    [InlineData(TurboTraceCategory.Redirect)]
    [InlineData(TurboTraceCategory.Retry)]
    public void AllCategories_should_produce_correct_flag(TurboTraceCategory category)
    {
        TurboTrace.Configure(_mock, category);

        // Call the matching category's Debug method via the static nested classes
        CallCategoryDebug(category, this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(category, _mock.Events[0].Category);
    }

    [Fact(Timeout = 5000)]
    public void SourceObject_should_have_type_and_hash_captured()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal(nameof(TurboTraceSpec), evt.SourceType);
        Assert.Equal(GetHashCode(), evt.SourceHash);
    }

    [Fact(Timeout = 5000)]
    public void ShouldTrace_should_have_aggressive_inlining()
    {
        var method = typeof(TurboTrace).GetMethod(
            "ShouldTrace",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var attr = method.GetMethodImplementationFlags();
        Assert.True((attr & MethodImplAttributes.AggressiveInlining) != 0);
    }

    [Fact(Timeout = 5000)]
    public void Debug_should_write_event_with_zero_args()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "no args");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("no args", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Debug_should_write_formatted_event_with_one_arg()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "val={0}", 42);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("val=42", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Debug_should_write_formatted_event_with_two_args()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "{0}+{1}", "a", "b");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("a+b", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Debug_should_write_formatted_event_with_three_args()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "{0}/{1}/{2}", 1, 2, 3);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("1/2/3", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void AllFiveLevels_should_write_events()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Trace(this, "trace");
        TurboTrace.Protocol.Debug(this, "debug");
        TurboTrace.Protocol.Info(this, "info");
        TurboTrace.Protocol.Warning(this, "warning");
        TurboTrace.Protocol.Error(this, "error");

        Assert.Equal(5, _mock.Events.Count);
        Assert.Equal(TurboTraceLevel.Trace, _mock.Events[0].Level);
        Assert.Equal(TurboTraceLevel.Debug, _mock.Events[1].Level);
        Assert.Equal(TurboTraceLevel.Info, _mock.Events[2].Level);
        Assert.Equal(TurboTraceLevel.Warning, _mock.Events[3].Level);
        Assert.Equal(TurboTraceLevel.Error, _mock.Events[4].Level);
    }

    [Fact(Timeout = 5000)]
    public void NullArg_should_not_throw()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "val={0}", (object?)null);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("val=", evt.FormatMessage());
    }

    [Fact(Timeout = 5000)]
    public void Configure_should_enable_all_categories_with_all()
    {
        TurboTrace.Configure(_mock);

        var categories = new[]
        {
            TurboTraceCategory.Protocol,
            TurboTraceCategory.Request,
            TurboTraceCategory.Cache,
            TurboTraceCategory.Redirect,
            TurboTraceCategory.Retry,
        };

        foreach (var cat in categories)
        {
            Assert.True(TurboTrace.ShouldTrace(cat, TurboTraceLevel.Debug),
                $"Category {cat} should be enabled with All");
        }
    }

    [Fact(Timeout = 5000)]
    public void Configure_should_disable_all_with_none()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.None);

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(Timeout = 5000)]
    public void RapidConfigureDisable_should_not_throw()
    {
        var exception = Record.Exception(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                TurboTrace.Configure(_mock);
                TurboTrace.Protocol.Debug(this, "cycle {0}", i);
                TurboTrace.Disable();
            }
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public void MultipleCategories_should_work_with_bitwise_or()
    {
        const TurboTraceCategory combined = TurboTraceCategory.Protocol | TurboTraceCategory.Request;
        TurboTrace.Configure(_mock, combined);

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Debug));
        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Cache, TurboTraceLevel.Debug));
    }

    private static void CallCategoryDebug(TurboTraceCategory category, object source, string message)
    {
        switch (category)
        {
            case TurboTraceCategory.Protocol: TurboTrace.Protocol.Debug(source, message); break;
            case TurboTraceCategory.Request: TurboTrace.Request.Debug(source, message); break;
            case TurboTraceCategory.Cache: TurboTrace.Cache.Debug(source, message); break;
            case TurboTraceCategory.Redirect: TurboTrace.Redirect.Debug(source, message); break;
            case TurboTraceCategory.Retry: TurboTrace.Retry.Debug(source, message); break;
        }
    }
}