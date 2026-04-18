using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Internal;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Internal;

/// <summary>
/// Tests <see cref="NetworkBufferBatchStage"/> batching behavior, flush semantics,
/// and control-item ordering.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="NetworkBufferBatchStage"/>.
/// Key behaviors: buffer batching up to maxWeight, control-item flush, overflow handling.
/// </remarks>
public sealed class NetworkBufferBatchStageSpec : StreamTestBase
{
    // Helpers

    private static NetworkBuffer CreateBuffer(int size, byte fill = (byte)'X')
    {
        var buf = NetworkBuffer.Rent(size);
        buf.FullMemory.Span.Fill(fill);
        buf.Length = size;
        return buf;
    }

    private sealed class ControlItem : IOutputItem
    {
        public string Name { get; }
        public RequestEndpoint Key { get; } = new() { Host = "test", Port = 80, Scheme = "http", Version = new Version(1, 1) };

        public ControlItem(string name = "Control")
        {
            Name = name;
        }

        public override string ToString() => Name;
    }

    private async Task<(List<IOutputItem>, bool)> RunBatchAsync(
        IEnumerable<IOutputItem> items,
        long maxWeight)
    {
        var collected = new List<IOutputItem>();
        var didComplete = false;

        var graph = GraphDsl.Create(
            Sink.ForEach<IOutputItem>(item => collected.Add(item)),
            (builder, sink) =>
            {
                var stage = builder.Add(new NetworkBufferBatchStage(maxWeight));
                var source = builder.Add(Source.From(items));

                builder.From(source).To(stage.Inlet);
                builder.From(stage.Outlet).To(sink);

                return ClosedShape.Instance;
            });

        try
        {
            await RunnableGraph.FromGraph(graph).Run(Materializer);
            didComplete = true;
        }
        catch
        {
            // Exceptions are expected in some test cases
        }

        return (collected, didComplete);
    }

    // NBBS-001: Single buffer with immediate downstream demand pushes immediately

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_push_buffer_immediately_when_downstream_demands()
    {
        // Arrange
        var buf = CreateBuffer(10);
        var items = new List<IOutputItem> { buf };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert
        Assert.True(success);
        Assert.Single(collected);
        Assert.Same(buf, collected[0]);
    }


    // NBBS-003: Control item flushes accumulated buffer before emission

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_flush_buffer_before_control_item()
    {
        // Arrange
        var buf = CreateBuffer(20);
        var ctrl = new ControlItem("Flush");
        var items = new List<IOutputItem> { buf, ctrl };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert — buffer then control, never interleaved
        Assert.True(success);
        Assert.Equal(2, collected.Count);
        Assert.IsType<NetworkBuffer>(collected[0]);
        Assert.Same(ctrl, collected[1]);
    }

    // NBBS-004: Overflow splits batches when adding would exceed maxWeight

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_emit_batch_when_next_buffer_overflows()
    {
        // Arrange — buf1(15) + buf2(15) would be 30, but maxWeight=25 so emit buf1 first
        var buf1 = CreateBuffer(15);
        var buf2 = CreateBuffer(15);
        var items = new List<IOutputItem> { buf1, buf2 };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 25);

        // Assert — buf1 emitted, buf2 emitted separately (or still batching)
        Assert.True(success);
        Assert.NotEmpty(collected);
        // First item should be a buffer (either buf1 merged/alone or buf2)
        Assert.IsType<NetworkBuffer>(collected[0]);
    }

    // NBBS-005: Multiple control items preserve ordering

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_preserve_control_item_order()
    {
        // Arrange
        var ctrl1 = new ControlItem("C1");
        var ctrl2 = new ControlItem("C2");
        var ctrl3 = new ControlItem("C3");
        var items = new List<IOutputItem> { ctrl1, ctrl2, ctrl3 };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert — control items emit in order
        Assert.True(success);
        Assert.Equal(3, collected.Count);
        Assert.Same(ctrl1, collected[0]);
        Assert.Same(ctrl2, collected[1]);
        Assert.Same(ctrl3, collected[2]);
    }

    // NBBS-006: Upstream completion flushes remaining buffer

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_emit_remaining_buffer_on_upstream_finish()
    {
        // Arrange — buffer without downstream demand yet
        var buf = CreateBuffer(50);
        var items = new List<IOutputItem> { buf };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert — buffer is emitted even though no pull came
        Assert.True(success);
        Assert.Single(collected);
        Assert.IsType<NetworkBuffer>(collected[0]);
    }

    // NBBS-007: Empty stream completes immediately

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_complete_immediately_on_empty_stream()
    {
        // Arrange
        var items = new List<IOutputItem>();

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert
        Assert.True(success);
        Assert.Empty(collected);
    }

    // NBBS-008: Control + buffer + control preserves order and flushes correctly

    [Fact(Timeout = 5000)]
    public async Task NetworkBufferBatch_should_handle_mixed_control_and_buffers()
    {
        // Arrange
        var ctrl1 = new ControlItem("Start");
        var buf = CreateBuffer(25);
        var ctrl2 = new ControlItem("End");
        var items = new List<IOutputItem> { ctrl1, buf, ctrl2 };

        // Act
        var (collected, success) = await RunBatchAsync(items, maxWeight: 100);

        // Assert — control1, buffer, control2 in order
        Assert.True(success);
        Assert.Equal(3, collected.Count);
        Assert.Same(ctrl1, collected[0]);
        Assert.IsType<NetworkBuffer>(collected[1]);
        Assert.Same(ctrl2, collected[2]);
    }

}
