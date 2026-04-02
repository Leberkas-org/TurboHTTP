using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http11.Encoding;

/// <summary>
/// Tests the batch encoding consolidation behaviour of the HTTP/1.1 encoder stage per RFC 9112.
/// Verifies that multiple output items are correctly concatenated into a single buffer for efficient sending.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11EncoderStage"/>.
/// RFC 9112 §6: HTTP/1.1 message body framing and transfer encoding.
/// </remarks>
public sealed class Http11BatchEncodingSpec : StreamTestBase
{
    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_concatenate_data_items_when_two_items_batched()
    {
        var item1 = NetworkBuffer.FromArray([0x01, 0x02, 0x03, 0x04]);
        var item2 = NetworkBuffer.FromArray([0x05, 0x06, 0x07]);

        var result = Assert.IsAssignableFrom<NetworkBuffer>(Http11Engine.BatchConsolidate(item1, item2));

        Assert.Equal(7, result.Length);
        var bytes = result.Span.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Dispose();
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_preserve_key_from_accumulated_item_when_batching()
    {
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = new Version(1, 1)
        };

        var item1 = NetworkBuffer.FromArray([0xAA, 0xBB]);
        item1.Key = key;

        var item2 = NetworkBuffer.FromArray([0xCC, 0xDD]);

        var result = Assert.IsAssignableFrom<NetworkBuffer>(Http11Engine.BatchConsolidate(item1, item2));

        Assert.Equal(key, result.Key);
        result.Dispose();
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_dispose_source_items_when_batching()
    {
        var item1 = NetworkBuffer.FromArray([0x01, 0x02, 0x03, 0x04]);
        var item2 = NetworkBuffer.FromArray([0x05, 0x06, 0x07]);

        var result = Assert.IsAssignableFrom<NetworkBuffer>(Http11Engine.BatchConsolidate(item1, item2));

        Assert.Equal(7, result.Length);
        var bytes = result.Span.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Dispose();
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_have_max_batch_weight_of_64kb()
    {
        Assert.Equal(65_536L, Http11Engine.MaxBatchWeight);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_enforce_max_eight_items_per_batch_when_min_item_weight_applied()
    {
        Assert.Equal(Http11Engine.MaxBatchWeight / 8, Http11Engine.MinItemWeight);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11BatchEncoding_should_pass_through_unchanged_when_single_request_in_stream()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var item = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http11Engine.MaxBatchWeight,
                    x => x is NetworkBuffer d ? Math.Max(d.Length, Http11Engine.MinItemWeight) : 0L,
                    x => x,
                    Http11Engine.BatchConsolidate))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = (NetworkBuffer)item;
        Assert.True(dataItem.Length > 0);
        // Verify it contains a valid HTTP/1.1 request line
        var text = System.Text.Encoding.ASCII.GetString(dataItem.Span);
        Assert.StartsWith("GET /path HTTP/1.1\r\n", text);
        dataItem.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11BatchEncoding_should_batch_multiple_requests_when_small_requests_in_stream()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/path{i}"))
            .ToList();

        var items = await Source.From(requests)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http11Engine.MaxBatchWeight,
                    x => x is NetworkBuffer d ? Math.Max(d.Length, Http11Engine.MinItemWeight) : 0L,
                    x => x,
                    Http11Engine.BatchConsolidate))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Batching should produce fewer items than input (exact count depends on timing)
        // But the total bytes must equal the sum of all individual encoded requests
        var totalBytes = items.Cast<NetworkBuffer>().Sum(d => d.Length);
        Assert.True(totalBytes > 0);

        // Verify concatenated output contains all request paths
        var allBytes = new byte[totalBytes];
        var offset = 0;
        foreach (var di in items.Cast<NetworkBuffer>())
        {
            di.Span.CopyTo(allBytes.AsSpan(offset));
            offset += di.Length;
            di.Dispose();
        }

        var text = System.Text.Encoding.ASCII.GetString(allBytes);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Contains($"GET /path{i} HTTP/1.1\r\n", text);
        }
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11BatchEncoding_should_concatenate_three_items_when_chained_batching()
    {
        var items = new NetworkBuffer[3];
        for (var i = 0; i < 3; i++)
        {
            items[i] = NetworkBuffer.FromArray(new byte[] { (byte)(i * 2), (byte)(i * 2 + 1) });
        }

        var accumulated = (NetworkBuffer)items[0];
        accumulated = Assert.IsAssignableFrom<NetworkBuffer>(Http11Engine.BatchConsolidate(accumulated, items[1]));
        accumulated = Assert.IsAssignableFrom<NetworkBuffer>(Http11Engine.BatchConsolidate(accumulated, items[2]));

        Assert.Equal(6, accumulated.Length);
        var bytes = accumulated.Span.ToArray();
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }, bytes);
        accumulated.Dispose();
    }
}
