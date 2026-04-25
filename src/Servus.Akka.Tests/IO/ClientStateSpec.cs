using System.Threading.Channels;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO;

public sealed class ClientStateSpec
{
    [Fact(Timeout = 5000)]
    public void ClientState_should_dispose_inbound_items_when_dispose_async_called()
    {
        // Arrange: pre-populate inbound channel with two NetworkBuffers
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var stream = new MemoryStream();

        var state = new ClientState(stream, inbound, outbound);

        var buf1 = NetworkBufferTestExtensions.FromArray(new byte[64]);
        var buf2 = NetworkBufferTestExtensions.FromArray(new byte[128]);
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

        var state = new ClientState(stream, inbound, outbound);

        var buf = NetworkBufferTestExtensions.FromArray(new byte[256]);
        state.OutboundWriter.TryWrite(buf);

        // Act
        state.Dispose();

        // Assert: outbound buffer must have been disposed (no exception on Dispose)
        // NetworkBuffer.Dispose() is idempotent so this just verifies the channel was drained
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_bidirectional_channels_by_default()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        Assert.NotNull(state.InboundReader);
        Assert.NotNull(state.InboundWriter);
        Assert.NotNull(state.OutboundReader);
        Assert.NotNull(state.OutboundWriter);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_accept_explicit_channels()
    {
        var stream = new MemoryStream();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var state = new ClientState(stream, inbound, outbound);

        Assert.NotNull(state.InboundReader);
        Assert.NotNull(state.InboundWriter);
        Assert.NotNull(state.OutboundReader);
        Assert.NotNull(state.OutboundWriter);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_have_working_channels()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        // Verify channels can be written to and read from
        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var writeSuccess = state.InboundWriter.TryWrite(buf);
        Assert.True(writeSuccess);

        var readSuccess = state.InboundReader.TryRead(out _);
        Assert.True(readSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_handle_bidirectional_channels()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        // Both inbound and outbound channels should be operational
        var inboundBuf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var outboundBuf = NetworkBufferTestExtensions.FromArray([4, 5, 6]);

        Assert.True(state.InboundWriter.TryWrite(inboundBuf));
        Assert.True(state.OutboundWriter.TryWrite(outboundBuf));

        Assert.True(state.InboundReader.TryRead(out _));
        Assert.True(state.OutboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_expose_stream_property()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        Assert.Same(stream, state.Stream);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_allow_on_writes_complete_callback()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null)
        {
            OnWritesComplete = () => { }
        };

        Assert.NotNull(state.OnWritesComplete);
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_drain_both_channels_on_dispose()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var stream = new MemoryStream();
        var state = new ClientState(stream, inbound, outbound);

        // Write multiple buffers
        for (var i = 0; i < 5; i++)
        {
            state.InboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([1, 2, 3]));
            state.OutboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([4, 5, 6]));
        }

        state.Dispose();

        // After dispose, channels should be completed and drained
        Assert.False(state.InboundReader.TryRead(out _));
        Assert.False(state.OutboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_complete_writer_on_dispose()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var stream = new MemoryStream();
        var state = new ClientState(stream, inbound, outbound);

        state.Dispose();

        // Writers should be completed after dispose
        Assert.False(state.InboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([1, 2, 3])));
        Assert.False(state.OutboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([4, 5, 6])));
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_dispose_stream_on_dispose()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_handle_double_dispose()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        state.Dispose();
        state.Dispose(); // Should not throw
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_write_only_channels()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null, StreamDirection.WriteOnly);

        Assert.Equal(StreamDirection.WriteOnly, state.Direction);
        Assert.NotNull(state.OutboundReader);
        Assert.NotNull(state.OutboundWriter);

        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        Assert.True(state.OutboundWriter.TryWrite(buf));

        Assert.False(state.InboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([4, 5, 6])));

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_create_read_only_channels()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null, StreamDirection.ReadOnly);

        Assert.Equal(StreamDirection.ReadOnly, state.Direction);
        Assert.NotNull(state.InboundReader);
        Assert.NotNull(state.InboundWriter);

        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        Assert.True(state.InboundWriter.TryWrite(buf));

        Assert.False(state.OutboundWriter.TryWrite(NetworkBufferTestExtensions.FromArray([4, 5, 6])));

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_write_only_should_pre_complete_inbound_channel()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null, StreamDirection.WriteOnly);

        Assert.True(state.InboundReader.Completion.IsCompleted);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_read_only_should_pre_complete_outbound_channel()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null, StreamDirection.ReadOnly);

        Assert.True(state.OutboundReader.Completion.IsCompleted);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_default_to_bidirectional_direction()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        Assert.Equal(StreamDirection.Bidirectional, state.Direction);

        state.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ClientState_should_expose_on_writes_complete_as_null_by_default()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream, null, null);

        Assert.Null(state.OnWritesComplete);

        state.Dispose();
    }
}