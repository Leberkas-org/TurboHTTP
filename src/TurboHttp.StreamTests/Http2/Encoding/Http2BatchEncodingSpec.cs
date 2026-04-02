using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http2.Encoding;

/// <summary>
/// Tests the batch encoding consolidation behaviour of the HTTP/2 encoder stage per RFC 9113.
/// </summary>
[Trait("RFC", "RFC9113-4.1")]
public sealed class Http2BatchEncodingSpec : StreamTestBase
{
    [Fact(Timeout = 5_000)]
    public void Http2BatchEncoding_should_concatenate_two_network_buffers()
    {
        var item1 = NetworkBuffer.FromArray(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var item2 = NetworkBuffer.FromArray(new byte[] { 0x05, 0x06, 0x07 });

        var result = Assert.IsAssignableFrom<NetworkBuffer>(Http20Engine.BatchConsolidate(item1, item2));

        Assert.Equal(7, result.Length);
        var bytes = result.Span.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Dispose();
    }

    [Fact(Timeout = 5_000)]
    public void Http2BatchEncoding_should_concatenate_three_network_buffers_when_chained()
    {
        var items = new NetworkBuffer[3];
        for (var i = 0; i < 3; i++)
        {
            items[i] = NetworkBuffer.FromArray(new byte[] { (byte)(i * 2), (byte)(i * 2 + 1) });
        }

        var accumulated = (NetworkBuffer)items[0];
        accumulated = Assert.IsAssignableFrom<NetworkBuffer>(Http20Engine.BatchConsolidate(accumulated, items[1]));
        accumulated = Assert.IsAssignableFrom<NetworkBuffer>(Http20Engine.BatchConsolidate(accumulated, items[2]));

        Assert.Equal(6, accumulated.Length);
        var bytes = accumulated.Span.ToArray();
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }, bytes);
        accumulated.Dispose();
    }

    [Fact(Timeout = 5_000)]
    public void Http2BatchEncoding_should_preserve_key_from_accumulated_item()
    {
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = new Version(2, 0)
        };

        var item1 = NetworkBuffer.FromArray(new byte[] { 0xAA, 0xBB });
        item1.Key = key;
        var item2 = NetworkBuffer.FromArray(new byte[] { 0xCC, 0xDD });

        var result = Assert.IsAssignableFrom<NetworkBuffer>(Http20Engine.BatchConsolidate(item1, item2));

        Assert.Equal(key, result.Key);
        result.Dispose();
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2BatchEncoding_should_pass_through_unchanged_when_single_frame_in_stream()
    {
        var body = new byte[] { 0x01, 0x02, 0x03 };
        var frame = new DataFrame(streamId: 1, data: body, endStream: true);

        var item = await Source.Single(new List<Http2Frame> { frame })
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = Assert.IsAssignableFrom<NetworkBuffer>(item);
        Assert.True(dataItem.Length > 0);
        // 9-byte frame header + 3-byte body
        Assert.Equal(12, dataItem.Length);
        var bytes = dataItem.Span.ToArray();
        Assert.Equal(body, bytes[9..]);
        dataItem.Dispose();
    }

    [Fact(Timeout = 5_000)]
    public void Http2BatchEncoding_should_expose_max_weight_as_64kb_constant()
    {
        Assert.Equal(65_536L, Http20Engine.MaxBatchWeight);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2BatchEncoding_should_encode_multiple_frames_into_single_buffer()
    {
        // 5 PING frames passed as one batch → encoder writes them into a single NetworkBuffer.
        var frames = Enumerable.Range(1, 5)
            .Select(i => (Http2Frame)new PingFrame(data: BitConverter.GetBytes((long)i)))
            .ToList();

        var item = await Source.Single(frames)
            .Via(Flow.FromGraph(new Http20EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var buffer = Assert.IsAssignableFrom<NetworkBuffer>(item);
        // Each PING frame = 9-byte header + 8-byte data = 17 bytes
        Assert.Equal(17 * 5, buffer.Length);
        buffer.Dispose();
    }

    [Fact(Timeout = 5_000)]
    public void Http2BatchEncoding_should_dispose_source_items_after_consolidation()
    {
        var item1 = NetworkBuffer.FromArray(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var item2 = NetworkBuffer.FromArray(new byte[] { 0x05, 0x06, 0x07 });

        var result = (NetworkBuffer)Http20Engine.BatchConsolidate(item1, item2);

        Assert.Equal(7, result.Length);
        var bytes = result.Span.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Dispose();
    }
}
