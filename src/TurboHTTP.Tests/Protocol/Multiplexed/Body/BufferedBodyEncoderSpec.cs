using System.Collections.Concurrent;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Multiplexed.Body;

namespace TurboHTTP.Tests.Protocol.Multiplexed.Body;

public sealed class BufferedBodyEncoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task BufferedBodyEncoder_should_drain_content_as_single_chunk()
    {
        var messages = new BlockingCollection<object>();
        var body = new byte[100];
        Random.Shared.NextBytes(body);
        var content = new ByteArrayContent(body);

        using var encoder = new BufferedBodyEncoder();
        encoder.Start(content, msg => messages.Add(msg));

        var chunk = (OutboundBodyChunk)messages.Take(TestContext.Current.CancellationToken);
        Assert.Equal(100, chunk.Length);
        Assert.Equal(body, chunk.Owner.Memory[..chunk.Length].ToArray());
        chunk.Owner.Dispose();

        var complete = messages.Take(TestContext.Current.CancellationToken);
        Assert.IsType<OutboundBodyComplete>(complete);
    }

    [Fact(Timeout = 5000)]
    public async Task BufferedBodyEncoder_should_handle_empty_content()
    {
        var messages = new BlockingCollection<object>();
        var content = new ByteArrayContent([]);

        using var encoder = new BufferedBodyEncoder();
        encoder.Start(content, msg => messages.Add(msg));

        var chunk = (OutboundBodyChunk)messages.Take(TestContext.Current.CancellationToken);
        Assert.Equal(0, chunk.Length);
        chunk.Owner.Dispose();

        var complete = messages.Take(TestContext.Current.CancellationToken);
        Assert.IsType<OutboundBodyComplete>(complete);
    }
}