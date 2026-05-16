using System.Text;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ChunkedBodyEncoderSpec : TestKit
{
    [Fact(Timeout = 5000)]
    public void Start_should_wrap_body_in_chunk_framing()
    {
        var probe = CreateTestProbe();
        var content = new ByteArrayContent("hello"u8.ToArray());
        using var encoder = new ChunkedBodyEncoder(chunkSize: 16_384);

        encoder.Start(content, probe.Ref);

        var chunks = new List<OutboundBodyChunk>();
        while (true)
        {
            var msg = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            if (msg is OutboundBodyComplete) break;
            chunks.Add(Assert.IsType<OutboundBodyChunk>(msg));
        }

        var all = string.Concat(chunks.Select(c =>
        {
            var s = Encoding.ASCII.GetString(c.Owner.Memory.Span[..c.Length]);
            c.Owner.Dispose();
            return s;
        }));

        Assert.Contains("5\r\nhello\r\n", all);
        Assert.Contains("0\r\n\r\n", all);
    }

    [Fact(Timeout = 5000)]
    public void Start_should_emit_terminator_only_for_empty_body()
    {
        var probe = CreateTestProbe();
        var content = new ByteArrayContent([]);
        using var encoder = new ChunkedBodyEncoder(chunkSize: 16_384);

        encoder.Start(content, probe.Ref);

        var msg = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var chunk = Assert.IsType<OutboundBodyChunk>(msg);
        var wire = Encoding.ASCII.GetString(chunk.Owner.Memory.Span[..chunk.Length]);
        Assert.Equal("0\r\n\r\n", wire);
        chunk.Owner.Dispose();

        var msg2 = probe.ReceiveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<OutboundBodyComplete>(msg2);
    }
}