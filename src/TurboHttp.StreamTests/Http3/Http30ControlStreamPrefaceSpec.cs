using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http3;

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
public sealed class Http30ControlStreamPrefaceSpec : StreamTestBase
{
    private static NetworkBuffer MakeDataItem(byte[] data)
        => NetworkBuffer.FromArray(data);

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_emit_preface_first_when_first_pull_occurs()
    {
        var upstream = MakeDataItem([0xAA, 0xBB]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.IsType<Http3OutputTaggedItem>(results[0]);
        Assert.IsNotType<Http3OutputTaggedItem>(results[1]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_tag_preface_as_control_when_emitted()
    {
        var upstream = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = Assert.IsType<Http3OutputTaggedItem>(results[0]);
        Assert.Equal(OutputStreamType.Control, tagged.StreamType);
        Assert.IsType<NetworkBuffer>(tagged.Inner, exactMatch: false);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_start_with_stream_type_0x00_when_preface_emitted()
    {
        var upstream = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = Assert.IsType<NetworkBuffer>(tagged.Inner, exactMatch: false);
        var bytes = data.Span.ToArray();

        // First byte is VarInt-encoded stream type 0x00 (control)
        Assert.Equal(0x00, bytes[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_contain_settings_frame_type_when_preface_emitted()
    {
        var upstream = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = Assert.IsType<NetworkBuffer>(tagged.Inner);
        var bytes = data.Span.ToArray();

        // Byte 1 is VarInt-encoded frame type 0x04 (SETTINGS)
        Assert.Equal(0x04, bytes[1]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_match_open_local_stream_output_when_default_settings()
    {
        var upstream = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (NetworkBuffer)tagged.Inner;
        var actual = data.Span.ToArray();

        // Generate expected bytes using the same API
        var controlStream = new Http3ControlStream();
        var expected = controlStream.OpenLocalStream();

        Assert.Equal(expected, actual);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_emit_preface_only_once_when_multiple_upstream_items()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => (IOutputItem)MakeDataItem([(byte)i]))
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_pass_through_items_unchanged_when_after_preface()
    {
        var item1 = MakeDataItem([0xAA]);
        var item2 = MakeDataItem([0xBB]);

        var results = await Source.From(new IOutputItem[] { item1, item2 })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        Assert.Equal(3, results.Count);
        Assert.Same(item1, results[1]);
        Assert.Same(item2, results[2]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_terminate_cleanly_when_upstream_finishes()
    {
        var item = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { item })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Preface + one passthrough item, then stage completes cleanly
        Assert.Equal(2, results.Count);
        Assert.IsType<Http3OutputTaggedItem>(results[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30ControlStreamPreface_should_use_custom_settings_when_provided()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);

        var upstream = MakeDataItem([0x01]);

        var results = await Source.From(new IOutputItem[] { upstream })
            .Via(Flow.FromGraph(new Http30ControlStreamPrefaceStage(settings)))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var tagged = (Http3OutputTaggedItem)results[0];
        var data = (NetworkBuffer)tagged.Inner;
        var actual = data.Span.ToArray();

        // Generate expected bytes with same settings
        var controlStream = new Http3ControlStream();
        var expected = controlStream.OpenLocalStream(settings);

        Assert.Equal(expected, actual);
    }
}
