using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http3.Qpack;

/// <summary>
/// Tests the QPACK encoder instruction stream wiring: <see cref="Http30Request2FrameStage"/>
/// emits encoder instructions on its second outlet, and <see cref="Http30QpackEncoderPrefaceStage"/>
/// prepends the stream type VarInt (0x02) and tags output for QUIC routing.
/// </summary>
/// <remarks>
/// RFC 9204 §4.2: The encoder sends instructions on a unidirectional stream of type 0x02.
/// RFC 9114 §6.2.1: Stream type is VarInt-encoded as the first byte(s) on the stream.
/// </remarks>
public sealed class Http30QpackEncoderStreamSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.2")]
    public async Task Http30QpackEncoderStream_should_emit_encoder_instructions_when_dynamic_table_is_active()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (_, instructionsTask) = graph.Run(Materializer);
        var instructions = await instructionsTask;

        // With dynamic table capacity > 0, the first encode should produce
        // SetDynamicTableCapacity + insert instructions.
        Assert.NotEmpty(instructions);
        Assert.True(instructions.First().Length > 0, "Encoder instructions should not be empty");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.2")]
    public async Task Http30QpackEncoderStream_should_emit_empty_instructions_when_dynamic_table_is_disabled()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (_, instructionsTask) = graph.Run(Materializer);
        var instructions = await instructionsTask;

        // With maxTableCapacity=0, instructions may still be emitted but will be empty.
        Assert.All(instructions, i => Assert.Equal(0, i.Length));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.2")]
    public async Task Http30QpackEncoderStream_should_prepend_stream_type_0x02_when_first_instruction_emitted()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frameSink = Sink.Seq<Http3Frame>();
        var outputSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, outputSink,
                (m1, m2) => (m1, m2),
                (b, fSink, oSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));
                    var preface = b.Add(new Http30QpackEncoderPrefaceStage());

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(preface.Inlet);
                    b.From(preface.Outlet).To(oSink);

                    return ClosedShape.Instance;
                }));

        var (_, outputTask) = graph.Run(Materializer);
        var outputs = await outputTask;

        Assert.NotEmpty(outputs);
        var tagged = Assert.IsType<Http3OutputTaggedItem>(outputs.First());
        Assert.Equal(OutputStreamType.QpackEncoder, tagged.StreamType);

        var data = Assert.IsAssignableFrom<NetworkBuffer>(tagged.Inner);
        var bytes = data.Span.ToArray();

        // First byte must be VarInt(0x02) — QPACK encoder stream type
        Assert.Equal(0x02, bytes[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.2")]
    public async Task Http30QpackEncoderStream_should_tag_output_as_qpack_encoder_when_instructions_emitted()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/first");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/second");

        var frameSink = Sink.Seq<Http3Frame>();
        var outputSink = Sink.Seq<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, outputSink,
                (m1, m2) => (m1, m2),
                (b, fSink, oSink) =>
                {
                    var source = b.Add(Source.From(new[] { request1, request2 }));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));
                    var preface = b.Add(new Http30QpackEncoderPrefaceStage());

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(preface.Inlet);
                    b.From(preface.Outlet).To(oSink);

                    return ClosedShape.Instance;
                }));

        var (_, outputTask) = graph.Run(Materializer);
        var outputs = await outputTask;

        Assert.All(outputs, item =>
        {
            var tagged = Assert.IsType<Http3OutputTaggedItem>(item);
            Assert.Equal(OutputStreamType.QpackEncoder, tagged.StreamType);
        });
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.2")]
    public async Task Http30QpackEncoderStream_should_filter_empty_instruction_buffers()
    {
        // Empty instructions should be filtered by the preface stage (it pulls again).
        var emptyInstructions = new ReadOnlyMemory<byte>[]
        {
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            new byte[] { 0x20, 0x03 }  // Non-empty: some instruction bytes
        };

        var results = await Source.From(emptyInstructions)
            .Via(Flow.FromGraph(new Http30QpackEncoderPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Only the non-empty instruction should produce output.
        Assert.Single(results);
        var tagged = Assert.IsType<Http3OutputTaggedItem>(results.First());
        var data = Assert.IsAssignableFrom<NetworkBuffer>(tagged.Inner);
        var bytes = data.Span.ToArray();

        // First byte is stream type 0x02, then instruction bytes
        Assert.Equal(0x02, bytes[0]);
        Assert.Equal(0x20, bytes[1]);
        Assert.Equal(0x03, bytes[2]);
    }
}
