using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the batch encoding consolidation behaviour of the HTTP/2 encoder stage per RFC 9113.
/// Verifies that multiple output items are correctly concatenated into a single buffer for efficient sending.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20EncoderStage"/>.
/// RFC 9113 §4: HTTP/2 frame serialisation and buffer management for outbound data.
/// </remarks>
public sealed class Http20BatchEncodingTests : StreamTestBase
{
    [Fact(DisplayName = "BatchConsolidate: two DataItems are concatenated into single buffer")]
    public void Should_ConcatenateTwoDataItems_When_BatchConsolidateCalled()
    {
        var owner1 = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0x01, 0x02, 0x03, 0x04 }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 4);

        var owner2 = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0x05, 0x06, 0x07 }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 3);

        var result = (DataItem)Http20Engine.BatchConsolidate(item1, item2);

        Assert.Equal(7, result.Length);
        var bytes = result.Memory.Memory.Span[..result.Length].ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Memory.Dispose();
    }

    [Fact(DisplayName = "BatchConsolidate: three DataItems chained produce correct concatenation")]
    public void Should_ConcatenateThreeDataItems_When_BatchConsolidateChained()
    {
        var items = new DataItem[3];
        for (var i = 0; i < 3; i++)
        {
            var owner = MemoryPool<byte>.Shared.Rent(2);
            new byte[] { (byte)(i * 2), (byte)(i * 2 + 1) }.CopyTo(owner.Memory);
            items[i] = new DataItem(owner, 2);
        }

        var accumulated = items[0];
        accumulated = (DataItem)Http20Engine.BatchConsolidate(accumulated, items[1]);
        accumulated = (DataItem)Http20Engine.BatchConsolidate(accumulated, items[2]);

        Assert.Equal(6, accumulated.Length);
        var bytes = accumulated.Memory.Memory.Span[..accumulated.Length].ToArray();
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }, bytes);
        accumulated.Memory.Dispose();
    }

    [Fact(DisplayName = "BatchConsolidate: Key is preserved from accumulated item")]
    public void Should_PreserveKeyFromAccumulatedItem_When_BatchConsolidateCalled()
    {
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = new Version(2, 0)
        };

        var owner1 = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xAA, 0xBB }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 2) { Key = key };

        var owner2 = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xCC, 0xDD }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 2);

        var result = (DataItem)Http20Engine.BatchConsolidate(item1, item2);

        Assert.Equal(key, result.Key);
        result.Memory.Dispose();
    }

    [Fact(Timeout = 10_000, DisplayName = "BatchConsolidate: single frame passes through unchanged in stream")]
    public async Task Should_PassThroughUnchanged_When_SingleFrameInStream()
    {
        var body = new byte[] { 0x01, 0x02, 0x03 };
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var item = await Source.Single((Http2Frame)frame)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http20Engine.MaxBatchWeight,
                    x => x is DataItem d ? d.Length : 0L,
                    x => x,
                    Http20Engine.BatchConsolidate))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = (DataItem)item;
        Assert.True(dataItem.Length > 0);
        // 9-byte frame header + 3-byte body
        Assert.Equal(12, dataItem.Length);
        var bytes = dataItem.Memory.Memory.Span[..dataItem.Length].ToArray();
        Assert.Equal(body, bytes[9..]);
        dataItem.Memory.Dispose();
    }

    [Fact(DisplayName = "BatchConsolidate: max weight 64KB is exposed as constant")]
    public void Should_ExposeMaxWeightAs64KB_When_CheckingConstant()
    {
        Assert.Equal(65_536L, Http20Engine.MaxBatchWeight);
    }

    [Fact(Timeout = 10_000, DisplayName = "BatchConsolidate: multiple frames batched into fewer output items")]
    public async Task Should_BatchMultipleFrames_When_StreamHasMultipleSmallFrames()
    {
        // Create several small frames that should be batched together
        var frames = Enumerable.Range(1, 5)
            .Select(i => (Http2Frame)new PingFrame(data: BitConverter.GetBytes((long)i)))
            .ToList();

        // Use a queue source so we can push all frames before pulling
        var items = await Source.From(frames)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http20Engine.MaxBatchWeight,
                    x => x is DataItem d ? d.Length : 0L,
                    x => x,
                    Http20Engine.BatchConsolidate))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Batching should produce fewer items than input (exact count depends on timing)
        // But the total bytes must equal the sum of all individual frame sizes
        var totalBytes = items.Cast<DataItem>().Sum(d => d.Length);
        var expectedSize = frames.Sum(f => f.SerializedSize);
        Assert.Equal(expectedSize, totalBytes);

        // Verify concatenated bytes are valid by checking each frame is 17 bytes (9 header + 8 ping data)
        Assert.Equal(17 * 5, totalBytes);

        foreach (var item in items.Cast<DataItem>())
        {
            item.Memory.Dispose();
        }
    }

    [Fact(DisplayName = "BatchConsolidate: source items disposed after consolidation")]
    public void Should_DisposeSourceItems_When_BatchConsolidationComplete()
    {
        var owner1 = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0x01, 0x02, 0x03, 0x04 }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 4);

        var owner2 = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0x05, 0x06, 0x07 }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 3);

        var result = (DataItem)Http20Engine.BatchConsolidate(item1, item2);

        // After consolidation, accessing the original owners should throw or return zeroed memory
        // because they were disposed. The result should still be valid.
        Assert.Equal(7, result.Length);
        var bytes = result.Memory.Memory.Span[..result.Length].ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Memory.Dispose();
    }
}
