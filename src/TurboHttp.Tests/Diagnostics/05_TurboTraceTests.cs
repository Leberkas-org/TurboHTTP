using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[Collection("TurboTrace")]
public sealed class TurboTraceTests : IDisposable
{
    private sealed class MockTraceListener : ITurboTraceListener
    {
        public List<TraceEvent> Events { get; } = new();
        public bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category) => true;
        public void Write(in TraceEvent evt) => Events.Add(evt);
    }

    private readonly MockTraceListener _mock = new();

    public void Dispose()
    {
        TurboTrace.Disable();
    }

    // ── TraceEvent struct tests ──────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Trace-001: FormatMessage returns template when no args")]
    public void FormatMessage_NoArgs_ReturnsTemplate()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "Hello world");

        Assert.Equal("Hello world", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-002: FormatMessage formats single arg correctly")]
    public void FormatMessage_OneArg_FormatsCorrectly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "Value: {0}", 42, null, null);

        Assert.Equal("Value: 42", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-003: FormatMessage formats two args correctly")]
    public void FormatMessage_TwoArgs_FormatsCorrectly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "{0} = {1}", "key", "value", null);

        Assert.Equal("key = value", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-004: FormatMessage formats three args correctly")]
    public void FormatMessage_ThreeArgs_FormatsCorrectly()
    {
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "{0}/{1}/{2}", "a", "b", "c");

        Assert.Equal("a/b/c", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-005: TraceEvent captures TimestampTicks from Stopwatch")]
    public void TraceEvent_CapturesTimestamp()
    {
        var before = Stopwatch.GetTimestamp();
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "msg");
        var after = Stopwatch.GetTimestamp();

        Assert.InRange(evt.TimestampTicks, before, after);
    }

    [Fact(DisplayName = "Diagnostics-Trace-006: TraceEvent stores Level and Category correctly")]
    public void TraceEvent_StoresLevelAndCategory()
    {
        var evt = new TraceEvent(
            0, TurboTraceLevel.Warning, TurboTraceCategory.Transport,
            "Test", 0, "msg");

        Assert.Equal(TurboTraceLevel.Warning, evt.Level);
        Assert.Equal(TurboTraceCategory.Transport, evt.Category);
    }

    [Fact(DisplayName = "Diagnostics-Trace-007: TraceEvent stores SourceType from object.GetType().Name")]
    public void TraceEvent_StoresSourceType()
    {
        var evt = new TraceEvent(
            0, TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "TurboTraceTests", 0, "msg");

        Assert.Equal("TurboTraceTests", evt.SourceType);
    }

    [Fact(DisplayName = "Diagnostics-Trace-008: TraceEvent stores SourceHash from object.GetHashCode()")]
    public void TraceEvent_StoresSourceHash()
    {
        var hash = GetHashCode();
        var evt = new TraceEvent(
            0, TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "TurboTraceTests", hash, "msg");

        Assert.Equal(hash, evt.SourceHash);
    }

    [Fact(DisplayName = "Diagnostics-Trace-009: TraceEvent is a readonly struct")]
    public void TraceEvent_IsReadonlyStruct()
    {
        var type = typeof(TraceEvent);
        Assert.True(type.IsValueType);
        Assert.True(type.GetCustomAttributes(typeof(IsReadOnlyAttribute), false).Length > 0);
    }

    // ── TurboTrace static API tests ──────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Trace-010: ShouldTrace returns false when listener is null")]
    public void ShouldTrace_FalseWhenNoListener()
    {
        TurboTrace.Disable();

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(DisplayName = "Diagnostics-Trace-011: ShouldTrace returns true when listener enabled for category and level")]
    public void ShouldTrace_TrueWhenEnabled()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All, TurboTraceLevel.Trace);

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(DisplayName = "Diagnostics-Trace-012: ShouldTrace returns false when category not enabled")]
    public void ShouldTrace_FalseWhenCategoryDisabled()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.Connection, TurboTraceLevel.Trace);

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(DisplayName = "Diagnostics-Trace-013: ShouldTrace returns false when level below minimum")]
    public void ShouldTrace_FalseWhenBelowMinimum()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All, TurboTraceLevel.Warning);

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(DisplayName = "Diagnostics-Trace-014: Configure sets listener and enables tracing")]
    public void Configure_EnablesTracing()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Single(_mock.Events);
    }

    [Fact(DisplayName = "Diagnostics-Trace-015: Disable clears listener and stops tracing")]
    public void Disable_StopsTracing()
    {
        TurboTrace.Configure(_mock);
        TurboTrace.Disable();

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(DisplayName = "Diagnostics-Trace-016: Protocol.Debug writes event with correct category")]
    public void ProtocolDebug_WritesCorrectCategory()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceCategory.Protocol, _mock.Events[0].Category);
    }

    [Fact(DisplayName = "Diagnostics-Trace-017: Connection.Info writes event with correct category")]
    public void ConnectionInfo_WritesCorrectCategory()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Connection.Info(this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceCategory.Connection, _mock.Events[0].Category);
    }

    [Fact(DisplayName = "Diagnostics-Trace-018: Request.Warning writes event with correct level")]
    public void RequestWarning_WritesCorrectLevel()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Request.Warning(this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(TurboTraceLevel.Warning, _mock.Events[0].Level);
    }

    [Fact(DisplayName = "Diagnostics-Trace-019: Trace call with no listener produces no event")]
    public void TraceCall_NoListener_NoEvent()
    {
        TurboTrace.Disable();

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(DisplayName = "Diagnostics-Trace-020: Category filtering via bitwise flags works")]
    public void CategoryFiltering_BitwiseFlags()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.Protocol | TurboTraceCategory.Connection);

        TurboTrace.Protocol.Debug(this, "yes");
        TurboTrace.Connection.Debug(this, "yes");
        TurboTrace.Request.Debug(this, "no");

        Assert.Equal(2, _mock.Events.Count);
        Assert.All(_mock.Events, e =>
            Assert.True(e.Category == TurboTraceCategory.Protocol || e.Category == TurboTraceCategory.Connection));
    }

    [Fact(DisplayName = "Diagnostics-Trace-021: Level filtering via minimum level works")]
    public void LevelFiltering_MinimumLevel()
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

    [Theory(DisplayName = "Diagnostics-Trace-022: All 10 categories produce events with correct category flag")]
    [InlineData(TurboTraceCategory.Connection)]
    [InlineData(TurboTraceCategory.Protocol)]
    [InlineData(TurboTraceCategory.Request)]
    [InlineData(TurboTraceCategory.Response)]
    [InlineData(TurboTraceCategory.Cache)]
    [InlineData(TurboTraceCategory.Redirect)]
    [InlineData(TurboTraceCategory.Retry)]
    [InlineData(TurboTraceCategory.Pool)]
    [InlineData(TurboTraceCategory.Transport)]
    [InlineData(TurboTraceCategory.Stream)]
    public void AllCategories_ProduceCorrectFlag(TurboTraceCategory category)
    {
        TurboTrace.Configure(_mock, category);

        // Call the matching category's Debug method via the static nested classes
        CallCategoryDebug(category, this, "test");

        Assert.Single(_mock.Events);
        Assert.Equal(category, _mock.Events[0].Category);
    }

    [Fact(DisplayName = "Diagnostics-Trace-023: Source object type name and hash are captured")]
    public void SourceObject_TypeAndHash_Captured()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "test");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal(nameof(TurboTraceTests), evt.SourceType);
        Assert.Equal(GetHashCode(), evt.SourceHash);
    }

    [Fact(DisplayName = "Diagnostics-Trace-024: ShouldTrace is marked AggressiveInlining")]
    public void ShouldTrace_HasAggressiveInlining()
    {
        var method = typeof(TurboTrace).GetMethod(
            "ShouldTrace",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var attr = method.GetMethodImplementationFlags();
        Assert.True((attr & MethodImplAttributes.AggressiveInlining) != 0);
    }

    // ── Overload tests ───────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Trace-025: Debug with 0 args writes event")]
    public void Debug_ZeroArgs_WritesEvent()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "no args");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("no args", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-026: Debug with 1 arg writes event with formatted message")]
    public void Debug_OneArg_WritesFormattedEvent()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "val={0}", 42);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("val=42", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-027: Debug with 2 args writes event with formatted message")]
    public void Debug_TwoArgs_WritesFormattedEvent()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "{0}+{1}", "a", "b");

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("a+b", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-028: Debug with 3 args writes event with formatted message")]
    public void Debug_ThreeArgs_WritesFormattedEvent()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "{0}/{1}/{2}", 1, 2, 3);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("1/2/3", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-029: All five level methods write events")]
    public void AllFiveLevels_WriteEvents()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All, TurboTraceLevel.Trace);

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

    // ── Edge case tests ──────────────────────────────────────────────

    [Fact(DisplayName = "Diagnostics-Trace-030: Null arg in format does not throw")]
    public void NullArg_DoesNotThrow()
    {
        TurboTrace.Configure(_mock);

        TurboTrace.Protocol.Debug(this, "val={0}", (object?)null);

        var evt = Assert.Single(_mock.Events);
        Assert.Equal("val=", evt.FormatMessage());
    }

    [Fact(DisplayName = "Diagnostics-Trace-031: Configure with All categories enables all 10 categories")]
    public void Configure_AllCategories_EnablesAll()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.All);

        var categories = new[]
        {
            TurboTraceCategory.Connection, TurboTraceCategory.Protocol,
            TurboTraceCategory.Request, TurboTraceCategory.Response,
            TurboTraceCategory.Cache, TurboTraceCategory.Redirect,
            TurboTraceCategory.Retry, TurboTraceCategory.Pool,
            TurboTraceCategory.Transport, TurboTraceCategory.Stream
        };

        foreach (var cat in categories)
        {
            Assert.True(TurboTrace.ShouldTrace(cat, TurboTraceLevel.Debug),
                $"Category {cat} should be enabled with All");
        }
    }

    [Fact(DisplayName = "Diagnostics-Trace-032: Configure with None categories disables all tracing")]
    public void Configure_NoneCategories_DisablesAll()
    {
        TurboTrace.Configure(_mock, TurboTraceCategory.None);

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));

        TurboTrace.Protocol.Debug(this, "test");

        Assert.Empty(_mock.Events);
    }

    [Fact(DisplayName = "Diagnostics-Trace-033: Rapid Configure and Disable cycles do not throw")]
    public void RapidConfigureDisable_DoesNotThrow()
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

    [Fact(DisplayName = "Diagnostics-Trace-034: Multiple categories can be enabled via bitwise OR")]
    public void MultipleCategories_BitwiseOr()
    {
        var combined = TurboTraceCategory.Protocol | TurboTraceCategory.Request | TurboTraceCategory.Stream;
        TurboTrace.Configure(_mock, combined);

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Debug));
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Stream, TurboTraceLevel.Debug));
        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Connection, TurboTraceLevel.Debug));
        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Cache, TurboTraceLevel.Debug));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static void CallCategoryDebug(TurboTraceCategory category, object source, string message)
    {
        switch (category)
        {
            case TurboTraceCategory.Connection: TurboTrace.Connection.Debug(source, message); break;
            case TurboTraceCategory.Protocol: TurboTrace.Protocol.Debug(source, message); break;
            case TurboTraceCategory.Request: TurboTrace.Request.Debug(source, message); break;
            case TurboTraceCategory.Response: TurboTrace.Response.Debug(source, message); break;
            case TurboTraceCategory.Cache: TurboTrace.Cache.Debug(source, message); break;
            case TurboTraceCategory.Redirect: TurboTrace.Redirect.Debug(source, message); break;
            case TurboTraceCategory.Retry: TurboTrace.Retry.Debug(source, message); break;
            case TurboTraceCategory.Pool: TurboTrace.Pool.Debug(source, message); break;
            case TurboTraceCategory.Transport: TurboTrace.Transport.Debug(source, message); break;
            case TurboTraceCategory.Stream: TurboTrace.Stream.Debug(source, message); break;
        }
    }
}