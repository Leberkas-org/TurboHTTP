using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Regression tests for QPACK stage completion propagation fixes (Feature 030).
/// Each test verifies that an upstream failure terminates the downstream outlet
/// within the timeout — a hang indicates the bug has been reintroduced.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="QpackDecoderStreamStage"/>, <see cref="QpackEncoderStreamStage"/>.
/// </remarks>
public sealed class QpackStageCompletionRegressionTests : StreamTestBase
{
    private static readonly InvalidOperationException UpstreamError = new("upstream error");

    // ──────────────────────────────────────────────────────────────
    // SCREG-016: QpackDecoderStreamStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-016: QpackDecoderStreamStage outlet terminates on upstream failure")]
    public async Task QpackDecoderStreamStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var source = Source.From(new[] { (ReadOnlyMemory<byte>)new byte[] { 0x00 } })
            .Concat(Source.Failed<ReadOnlyMemory<byte>>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
                .RunWith(Sink.Seq<DecoderInstruction>(), Materializer));
    }

    // ──────────────────────────────────────────────────────────────
    // SCREG-017: QpackEncoderStreamStage
    // ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000,
        DisplayName = "SCREG-017: QpackEncoderStreamStage outlet terminates on upstream failure")]
    public async Task QpackEncoderStreamStage_Outlet_Terminates_On_Upstream_Failure()
    {
        var instruction = new EncoderInstruction
        {
            Type = EncoderInstructionType.SetDynamicTableCapacity,
            IntValue = 4096
        };
        var source = Source.From(new[] { instruction })
            .Concat(Source.Failed<EncoderInstruction>(UpstreamError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source
                .Via(Flow.FromGraph(new QpackEncoderStreamStage()))
                .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Materializer));
    }
}
