using Servus.Akka.Tests.Utils;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

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
        Assert.Equal(2, buf.Length);
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

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_drain_pipe_to_channel_with_abrupt_close()
    {
        var stream = new MemoryStream([0xAA, 0xBB], writable: false);
        var state = new ClientState(stream);
        var closeCount = 0;

        var task = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            try
            {
                await state.InboundPipe.Writer.CompleteAsync(new AbruptCloseException());
            }
            catch
            {
                // noop - writer might already be completed
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => Interlocked.Increment(ref closeCount), cts.Token);
        await task;

        Assert.Equal(1, closeCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_drain_pipe_to_channel_generic_exception()
    {
        var stream = new MemoryStream([0xAA, 0xBB], writable: false);
        var state = new ClientState(stream);
        var closeCount = 0;

        var task = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            try
            {
                await state.InboundPipe.Writer.CompleteAsync(new InvalidOperationException("Test error"));
            }
            catch
            {
                // noop - writer might already be completed
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => Interlocked.Increment(ref closeCount), cts.Token);
        await task;

        Assert.Equal(1, closeCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_read_final_data_after_pipe_completion()
    {
        var stream = new MemoryStream([0xAA, 0xBB, 0xCC], writable: false);
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        Assert.True(state.InboundReader.TryRead(out var buf));
        Assert.Equal(3, buf.Length);
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_drain_pipe_to_stream_with_multi_segment_buffer()
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
    public async Task ClientByteMover_should_handle_drain_pipe_to_stream_write_cancellation()
    {
        var stream = new SlowStream();
        var state = new ClientState(stream);
        var closeCount = 0;

        WriteToChannel(state, 100, 0x44);
        state.OutboundWriter.TryComplete();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await ClientByteMover.MoveChannelToStream(state, () => Interlocked.Increment(ref closeCount), cts.Token);

        Assert.Equal(1, closeCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_fill_pipe_from_channel_generic_exception()
    {
        var stream = new MemoryStream();
        var state = new ClientState(stream);

        WriteToChannel(state, 10, 0x00);
        state.OutboundWriter.TryComplete(new InvalidOperationException("Channel error"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        Assert.True(stream.Length > 0);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_complete_channel_with_abrupt_exception_on_drain_error()
    {
        var stream = new FailingStream();
        var state = new ClientState(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        // Verify channel is completed with AbruptCloseException
        var exceptionThrown = false;
        try
        {
            await state.InboundReader.WaitToReadAsync(TestContext.Current.CancellationToken);
        }
        catch (AbruptCloseException)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    private static void WriteToChannel(ClientState state, int size, byte fill)
    {
        var buf = TransportBuffer.Rent(size);
        buf.FullMemory.Span[..size].Fill(fill);
        buf.Length = size;
        state.OutboundWriter.TryWrite(buf);
    }
}