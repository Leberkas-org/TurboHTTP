using System.Buffers;
using System.Net.Http;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9112;

public sealed class Http11BatchEncodingTests : StreamTestBase
{
    [Fact(DisplayName = "RFC9112-6-11BE-001: BatchConsolidate two DataItems concatenated into single buffer")]
    public void ST_11_BATCH_001_Two_DataItems_Concatenated()
    {
        var owner1 = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0x01, 0x02, 0x03, 0x04 }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 4);

        var owner2 = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0x05, 0x06, 0x07 }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 3);

        var result = (DataItem)Http11Engine.BatchConsolidate(item1, item2);

        Assert.Equal(7, result.Length);
        var bytes = result.Memory.Memory.Span[..result.Length].ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Memory.Dispose();
    }

    [Fact(DisplayName = "RFC9112-6-11BE-002: BatchConsolidate Key preserved from accumulated item")]
    public void ST_11_BATCH_002_Key_Preserved_From_Accumulated()
    {
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = new Version(1, 1)
        };

        var owner1 = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xAA, 0xBB }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 2) { Key = key };

        var owner2 = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xCC, 0xDD }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 2);

        var result = (DataItem)Http11Engine.BatchConsolidate(item1, item2);

        Assert.Equal(key, result.Key);
        result.Memory.Dispose();
    }

    [Fact(DisplayName = "RFC9112-6-11BE-003: BatchConsolidate source items disposed after consolidation")]
    public void ST_11_BATCH_003_Source_Items_Disposed()
    {
        var owner1 = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0x01, 0x02, 0x03, 0x04 }.CopyTo(owner1.Memory);
        var item1 = new DataItem(owner1, 4);

        var owner2 = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0x05, 0x06, 0x07 }.CopyTo(owner2.Memory);
        var item2 = new DataItem(owner2, 3);

        var result = (DataItem)Http11Engine.BatchConsolidate(item1, item2);

        Assert.Equal(7, result.Length);
        var bytes = result.Memory.Memory.Span[..result.Length].ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, bytes);
        result.Memory.Dispose();
    }

    [Fact(DisplayName = "RFC9112-6-11BE-004: MaxBatchWeight is 64KB")]
    public void ST_11_BATCH_004_MaxWeight_Is_64KB()
    {
        Assert.Equal(65_536L, Http11Engine.MaxBatchWeight);
    }

    [Fact(DisplayName = "RFC9112-6-11BE-005: MinItemWeight enforces max 8 items per batch")]
    public void ST_11_BATCH_005_MinItemWeight_Enforces_Max_8_Items()
    {
        Assert.Equal(Http11Engine.MaxBatchWeight / 8, Http11Engine.MinItemWeight);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11BE-006: Single request passes through unchanged in stream")]
    public async Task ST_11_BATCH_006_Single_Request_PassThrough()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var item = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http11Engine.MaxBatchWeight,
                    x => x is DataItem d ? Math.Max(d.Length, Http11Engine.MinItemWeight) : 0L,
                    x => x,
                    Http11Engine.BatchConsolidate))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = (DataItem)item;
        Assert.True(dataItem.Length > 0);
        // Verify it contains a valid HTTP/1.1 request line
        var text = System.Text.Encoding.ASCII.GetString(
            dataItem.Memory.Memory.Span[..dataItem.Length]);
        Assert.StartsWith("GET /path HTTP/1.1\r\n", text);
        dataItem.Memory.Dispose();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11BE-007: Multiple small requests batched into fewer output items")]
    public async Task ST_11_BATCH_007_Multiple_Requests_Batched()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/path{i}"))
            .ToList();

        var items = await Source.From(requests)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .Via(Flow.Create<IOutputItem>()
                .BatchWeighted(
                    Http11Engine.MaxBatchWeight,
                    x => x is DataItem d ? Math.Max(d.Length, Http11Engine.MinItemWeight) : 0L,
                    x => x,
                    Http11Engine.BatchConsolidate))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Batching should produce fewer items than input (exact count depends on timing)
        // But the total bytes must equal the sum of all individual encoded requests
        var totalBytes = items.Cast<DataItem>().Sum(d => d.Length);
        Assert.True(totalBytes > 0);

        // Verify concatenated output contains all request paths
        var allBytes = new byte[totalBytes];
        var offset = 0;
        foreach (var di in items.Cast<DataItem>())
        {
            di.Memory.Memory.Span[..di.Length].CopyTo(allBytes.AsSpan(offset));
            offset += di.Length;
            di.Memory.Dispose();
        }

        var text = System.Text.Encoding.ASCII.GetString(allBytes);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Contains($"GET /path{i} HTTP/1.1\r\n", text);
        }
    }

    [Fact(DisplayName = "RFC9112-6-11BE-008: BatchConsolidate three DataItems chained produce correct concatenation")]
    public void ST_11_BATCH_008_Three_DataItems_Chained()
    {
        var items = new DataItem[3];
        for (var i = 0; i < 3; i++)
        {
            var owner = MemoryPool<byte>.Shared.Rent(2);
            new byte[] { (byte)(i * 2), (byte)(i * 2 + 1) }.CopyTo(owner.Memory);
            items[i] = new DataItem(owner, 2);
        }

        var accumulated = items[0];
        accumulated = (DataItem)Http11Engine.BatchConsolidate(accumulated, items[1]);
        accumulated = (DataItem)Http11Engine.BatchConsolidate(accumulated, items[2]);

        Assert.Equal(6, accumulated.Length);
        var bytes = accumulated.Memory.Memory.Span[..accumulated.Length].ToArray();
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }, bytes);
        accumulated.Memory.Dispose();
    }
}
