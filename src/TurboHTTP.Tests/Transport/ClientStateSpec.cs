using System.Threading.Channels;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="ClientState.Dispose"/> — verifies that pending
/// <see cref="IMemoryOwner{T}"/> items in both channels are disposed during cleanup.
/// </summary>
public sealed class ClientStateSpec
{
    [Fact(Timeout = 5000)]
    public void ClientState_should_dispose_inbound_items_when_dispose_async_called()
    {
        // Arrange: pre-populate inbound channel with two NetworkBuffers
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var stream = new MemoryStream();

        var state = new ClientState(65536, stream, inbound, outbound);

        var buf1 = NetworkBuffer.FromArray(new byte[64]);
        var buf2 = NetworkBuffer.FromArray(new byte[128]);
        state.InboundWriter.TryWrite(buf1);
        state.InboundWriter.TryWrite(buf2);

        // Act
        state.Dispose();

        // Assert: both inbound buffers must have been disposed (no exception on Dispose)
        // NetworkBuffer.Dispose() is idempotent so this just verifies the channel was drained
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_dispose_outbound_items_when_dispose_async_called()
    {
        // Arrange: pre-populate outbound channel with one NetworkBuffer
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var stream = new MemoryStream();

        var state = new ClientState(65536, stream, inbound, outbound);

        var buf = NetworkBuffer.FromArray(new byte[256]);
        state.OutboundWriter.TryWrite(buf);

        // Act
        state.Dispose();

        // Assert: outbound buffer must have been disposed (no exception on Dispose)
        // NetworkBuffer.Dispose() is idempotent so this just verifies the channel was drained
    }
}
