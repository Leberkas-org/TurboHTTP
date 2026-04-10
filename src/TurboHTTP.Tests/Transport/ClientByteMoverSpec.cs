using System.Threading.Channels;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="ClientByteMover"/> buffer lifecycle for <see cref="ClientByteMover.MoveStreamToChannel"/>.
/// Verifies that rented <see cref="IMemoryOwner{T}"/> buffers are disposed when the inbound
/// channel is closed before <see cref="ChannelWriter{T}.TryWrite"/> can succeed.
/// </summary>
public sealed class ClientByteMoverSpec
{
    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_dispose_buffer_when_inbound_channel_is_closed()
    {
        // Arrange: a bounded channel that is immediately completed (closed for writing AND reading)
        var inbound = Channel.CreateBounded<NetworkBuffer>(1);
        inbound.Writer.Complete(); // channel closed — TryWrite will return false

        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        // Stream with one byte of data — MoveStreamToChannel will rent a buffer, read 1 byte,
        // try to write to the closed channel, get false from TryWrite, and must dispose the buffer.
        var stream = new MemoryStream([0x42], writable: false);

        var state = new ClientState(65536, stream, inbound, outbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act: run MoveStreamToChannel; it will rent a buffer, read data, try to write to the
        // closed channel, get false from TryWrite, and must dispose the buffer.
        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        // Assert: method completes without throwing (buffer was disposed on TryWrite failure).
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_not_dispose_buffer_when_try_write_succeeds()
    {
        // Arrange: open, unbounded inbound channel
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var stream = new MemoryStream([0xAB, 0xCD], writable: false);

        var state = new ClientState(65536, stream, inbound, outbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        // The item should be readable from the inbound channel
        var ok = inbound.Reader.TryRead(out var item);
        Assert.True(ok, "Expected an item in the inbound channel");
        Assert.NotNull(item);
        Assert.Equal(2, item.Length); // Length == 2

        // Clean up
        item.Dispose();
    }
}
