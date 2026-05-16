using System.Text;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ContentLengthStreamedBodyEncoderSpec : TestKit
{
    [Fact(Timeout = 5000)]
    public void Start_should_deliver_chunks_then_complete_for_small_body()
    {
        var probe = CreateTestProbe();
        var body = "small body"u8.ToArray();
        var content = new ByteArrayContent(body);
        using var encoder = new ContentLengthStreamedBodyEncoder(chunkSize: 16_384);

        encoder.Start(content, probe.Ref);

        var received = new List<byte>();
        while (true)
        {
            var msg = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            if (msg is OutboundBodyComplete) break;
            var chunk = Assert.IsType<OutboundBodyChunk>(msg);
            received.AddRange(chunk.Owner.Memory.Span[..chunk.Length].ToArray());
            chunk.Owner.Dispose();
        }

        Assert.Equal("small body", Encoding.UTF8.GetString(received.ToArray()));
    }

    [Fact(Timeout = 5000)]
    public void Start_should_split_body_larger_than_chunk_size()
    {
        var probe = CreateTestProbe();
        var body = new byte[1000];
        Random.Shared.NextBytes(body);
        var content = new ByteArrayContent(body);
        using var encoder = new ContentLengthStreamedBodyEncoder(chunkSize: 400);

        encoder.Start(content, probe.Ref);

        var chunks = new List<OutboundBodyChunk>();
        while (true)
        {
            var msg = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            if (msg is OutboundBodyComplete) break;
            chunks.Add(Assert.IsType<OutboundBodyChunk>(msg));
        }

        Assert.True(chunks.Count >= 2);
        var all = chunks.SelectMany(c =>
        {
            var arr = c.Owner.Memory.Span[..c.Length].ToArray();
            c.Owner.Dispose();
            return arr;
        }).ToArray();
        Assert.Equal(body, all);
    }
}