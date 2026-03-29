using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 control stream preface stage per RFC 9114 §6.2.1.
/// Verifies that control stream bytes (stream type VarInt + SETTINGS) are emitted
/// exactly once on first pull, tagged with <see cref="OutputStreamType.Control"/>,
/// and that subsequent items pass through unchanged.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30ControlStreamPrefaceStage"/>.
/// RFC 9114 §6.2.1: Each side MUST initiate a single control stream;
/// the first frame on the control stream MUST be SETTINGS.
/// </remarks>
public sealed class Http30ControlStreamPrefaceStageTests : StreamTestBase
{
    private static DataItem MakeDataItem(byte[] data)
    {
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.AsSpan().CopyTo(owner.Memory.Span);
        return new DataItem(owner, data.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-001: Preface is emitted on first pull before any upstream data")]
    public async Task Should_EmitPrefaceFirst_When_FirstPullOccurs()
    {
        var upstream = MakeDataItem(new byte[] { 0xAA, 0xBB });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.IsType<Http3OutputTaggedItem>(results[0]);
        Assert.IsNotType<Http3OutputTaggedItem>(results[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-002: Preface is tagged as OutputStreamType.Control")]
    public async Task Should_TagPrefaceAsControl_When_Emitted()
    {
        var upstream = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = Assert.IsType<Http3OutputTaggedItem>(results[0]);
        Assert.Equal(OutputStreamType.Control, tagged.StreamType);
        Assert.IsType<DataItem>(tagged.Inner);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-003: Preface bytes start with stream type 0x00 (control)")]
    public async Task Should_StartWithStreamType0x00_When_PrefaceEmitted()
    {
        var upstream = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (DataItem)tagged.Inner;
        var bytes = data.Memory.Memory[..data.Length].ToArray();

        // First byte is VarInt-encoded stream type 0x00 (control)
        Assert.Equal(0x00, bytes[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-004: Preface contains SETTINGS frame type (0x04) after stream type")]
    public async Task Should_ContainSettingsFrameType_When_PrefaceEmitted()
    {
        var upstream = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (DataItem)tagged.Inner;
        var bytes = data.Memory.Memory[..data.Length].ToArray();

        // Byte 1 is VarInt-encoded frame type 0x04 (SETTINGS)
        Assert.Equal(0x04, bytes[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-005: Preface matches Http3ControlStream.OpenLocalStream() output")]
    public async Task Should_MatchOpenLocalStreamOutput_When_DefaultSettings()
    {
        var upstream = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (DataItem)tagged.Inner;
        var actual = data.Memory.Memory[..data.Length].ToArray();

        // Generate expected bytes using the same API
        var controlStream = new Http3ControlStream();
        var expected = controlStream.OpenLocalStream();

        Assert.Equal(expected, actual);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-006: Preface is emitted exactly once even with multiple upstream items")]
    public async Task Should_EmitPrefaceOnlyOnce_When_MultipleUpstreamItems()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => (IOutputItem)MakeDataItem(new[] { (byte)i }))
            .ToArray();

        var results = await Source.From(items)
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // First item = preface (tagged), then 5 upstream items pass through
        Assert.Equal(6, results.Count);

        var taggedCount = results.Count(r => r is Http3OutputTaggedItem t && t.StreamType == OutputStreamType.Control);
        Assert.Equal(1, taggedCount);
        Assert.IsType<Http3OutputTaggedItem>(results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-007: Subsequent items pass through unchanged")]
    public async Task Should_PassThroughItemsUnchanged_When_AfterPreface()
    {
        var item1 = MakeDataItem(new byte[] { 0xAA });
        var item2 = MakeDataItem(new byte[] { 0xBB });

        var results = await Source.From(new IOutputItem[] { item1, item2 })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        Assert.Equal(3, results.Count);
        Assert.Same(item1, results[1]);
        Assert.Same(item2, results[2]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-008: Stage terminates cleanly on UpstreamFinish")]
    public async Task Should_TerminateCleanly_When_UpstreamFinishes()
    {
        var item = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { item })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Preface + one passthrough item, then stage completes cleanly
        Assert.Equal(2, results.Count);
        Assert.IsType<Http3OutputTaggedItem>(results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-6.2.1-H3CP-009: Custom settings are reflected in preface bytes")]
    public async Task Should_UseCustomSettings_When_Provided()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);

        var upstream = MakeDataItem(new byte[] { 0x01 });

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage(settings)))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (DataItem)tagged.Inner;
        var actual = data.Memory.Memory[..data.Length].ToArray();

        // Generate expected bytes with same settings
        var controlStream = new Http3ControlStream();
        var expected = controlStream.OpenLocalStream(settings);

        Assert.Equal(expected, actual);
    }
}
