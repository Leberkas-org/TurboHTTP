using System.Collections.Concurrent;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Multiplexed.Body;

namespace TurboHTTP.Tests.Protocol.Multiplexed.Body;

public sealed class StreamingBodyEncoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task StreamingBodyEncoder_should_drain_content_in_chunks()
    {
        var messages = new BlockingCollection<object>();
        var body = new byte[32_768];
        Random.Shared.NextBytes(body);
        var content = new ByteArrayContent(body);

        using var encoder = new StreamingBodyEncoder(chunkSize: 16_384);
        encoder.Start(content, msg => messages.Add(msg));

        var totalReceived = 0;
        while (true)
        {
            var msg = messages.Take(TestContext.Current.CancellationToken);
            if (msg is OutboundBodyChunk chunk)
            {
                Assert.True(chunk.Length > 0);
                Assert.True(chunk.Length <= 16_384);
                totalReceived += chunk.Length;
                chunk.Owner.Dispose();
            }
            else if (msg is OutboundBodyComplete)
            {
                break;
            }
        }

        Assert.Equal(body.Length, totalReceived);
    }

    [Fact(Timeout = 5000)]
    public async Task StreamingBodyEncoder_should_complete_for_small_content()
    {
        var messages = new BlockingCollection<object>();
        var body = new byte[100];
        Random.Shared.NextBytes(body);
        var content = new ByteArrayContent(body);

        using var encoder = new StreamingBodyEncoder();
        encoder.Start(content, msg => messages.Add(msg));

        var chunk = (OutboundBodyChunk)messages.Take(TestContext.Current.CancellationToken);
        Assert.Equal(100, chunk.Length);
        chunk.Owner.Dispose();

        var complete = messages.Take(TestContext.Current.CancellationToken);
        Assert.IsType<OutboundBodyComplete>(complete);
    }
}