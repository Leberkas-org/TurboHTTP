using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

[Collection("TransportBuffer")]
public sealed class ClientByteMoverSpec
{
    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_complete_on_stream_read()
    {
        var stream = new MemoryStream([0x42], writable: false);
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_write_data_to_inbound_channel()
    {
        var stream = new MemoryStream([0xAB, 0xCD], writable: false);
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        Assert.True(state.InboundReader.TryRead(out var buf));
        Assert.Equal(2, buf!.Length);
        Assert.Equal(0xAB, buf.Span[0]);
        Assert.Equal(0xCD, buf.Span[1]);
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_drain_outbound_channel_to_stream()
    {
        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);
        var state = new ClientState(stream);

        WriteToChannel(state, 100, 0x11);
        WriteToChannel(state, 100, 0x22);
        WriteToChannel(state, 100, 0x33);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        var totalBytes = capturedWrites.Sum(w => w.Length);
        Assert.Equal(300, totalBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_write_large_buffers_to_stream()
    {
        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);
        var state = new ClientState(stream);

        WriteToChannel(state, 33 * 1024, 0xAA);
        WriteToChannel(state, 100, 0xBB);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        var totalBytes = capturedWrites.Sum(w => w.Length);
        Assert.Equal(33 * 1024 + 100, totalBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_cancellation()
    {
        var stream = new MemoryStream([0x42], writable: false);
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_complete_channel_on_eof()
    {
        var stream = new MemoryStream([], writable: false);
        var state = new ClientState(stream);
        var closeCalled = false;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => closeCalled = true, cts.Token);

        Assert.True(closeCalled);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_complete_channel_with_exception_on_read_error()
    {
        var stream = new FailingStream();
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        await Assert.ThrowsAsync<AbruptCloseException>(async () =>
        {
            await state.InboundReader.WaitToReadAsync(cts.Token);
        });
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_invoke_on_writes_complete_callback()
    {
        var callbackInvoked = false;
        var stream = new MemoryStream();
        var state = new ClientState(stream)
        {
            OnWritesComplete = () => { callbackInvoked = true; }
        };

        WriteToChannel(state, 10, 0x00);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        Assert.True(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_drain_write_exception()
    {
        var stream = new FailingStream();
        var state = new ClientState(stream);

        WriteToChannel(state, 10, 0x00);
        state.OutboundWriter.TryComplete();

        var onCloseCalled = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { onCloseCalled = true; }, cts.Token);

        Assert.True(onCloseCalled);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_alternating_large_small_buffers()
    {
        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);
        var state = new ClientState(stream);

        WriteToChannel(state, 33 * 1024, 0xAA);
        WriteToChannel(state, 100, 0xBB);
        WriteToChannel(state, 33 * 1024, 0xCC);
        WriteToChannel(state, 100, 0xDD);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        var totalBytes = capturedWrites.Sum(w => w.Length);
        Assert.Equal(2 * (33 * 1024) + 200, totalBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_not_invoke_on_writes_complete_on_error()
    {
        var callbackInvoked = false;
        var stream = new FailingStream();
        var state = new ClientState(stream)
        {
            OnWritesComplete = () => { callbackInvoked = true; }
        };

        WriteToChannel(state, 10, 0x00);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        Assert.False(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_not_invoke_on_writes_complete_on_cancellation()
    {
        var callbackInvoked = false;
        var stream = new SlowStream();
        var state = new ClientState(stream)
        {
            OnWritesComplete = () => { callbackInvoked = true; }
        };

        WriteToChannel(state, 10, 0x00);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        Assert.False(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_many_small_buffers()
    {
        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);
        var state = new ClientState(stream);

        for (var i = 0; i < 200; i++)
        {
            WriteToChannel(state, 100, (byte)(i % 256));
        }

        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        var totalBytes = capturedWrites.Sum(w => w.Length);
        Assert.Equal(20_000, totalBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_call_on_close_exactly_once_on_read_error()
    {
        var stream = new FailingStream();
        var state = new ClientState(stream);

        var closeCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => Interlocked.Increment(ref closeCount), cts.Token);

        Assert.Equal(1, closeCount);
    }

    private static void WriteToChannel(ClientState state, int size, byte fill)
    {
        var buf = TransportBuffer.Rent(size);
        buf.FullMemory.Span[..size].Fill(fill);
        buf.Length = size;
        state.OutboundWriter.TryWrite(buf);
    }

    private sealed class CapturingStream(List<byte[]> writes) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            writes.Add(buffer.ToArray());
            await Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class SlowStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            throw new IOException("Test stream failure");
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            throw new IOException("Test stream failure");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
