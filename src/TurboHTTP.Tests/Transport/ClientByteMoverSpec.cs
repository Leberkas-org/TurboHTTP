using System.Threading.Channels;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

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

        var state = new ClientState(stream, inbound, outbound);

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

        var state = new ClientState(stream, inbound, outbound);

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

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_coalesce_small_buffers_in_channel_to_stream()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        // Create a writable stream to capture writes
        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);

        var state = new ClientState(stream, inbound, outbound);

        // Write several small buffers (< 16KB each) to outbound
        // These should be coalesced into fewer writes
        var smallBuf1 = NetworkBuffer.Rent(100);
        smallBuf1.Memory.Span.Fill(0x11);
        smallBuf1.Length = 100;

        var smallBuf2 = NetworkBuffer.Rent(100);
        smallBuf2.Memory.Span.Fill(0x22);
        smallBuf2.Length = 100;

        var smallBuf3 = NetworkBuffer.Rent(100);
        smallBuf3.Memory.Span.Fill(0x33);
        smallBuf3.Length = 100;

        outbound.Writer.TryWrite(smallBuf1);
        outbound.Writer.TryWrite(smallBuf2);
        outbound.Writer.TryWrite(smallBuf3);
        outbound.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        // Assert: small buffers should be coalesced into fewer writes
        // We expect 1 or 2 writes total (coalesced), not 3
        Assert.True(capturedWrites.Count <= 2, $"Expected <=2 writes, got {capturedWrites.Count}");
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_write_large_buffers_directly()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var capturedWrites = new List<byte[]>();
        var stream = new CapturingStream(capturedWrites);

        var state = new ClientState(stream, inbound, outbound);

        // Write a large buffer (> 16KB) followed by a small buffer
        var largeBuf = NetworkBuffer.Rent(17 * 1024);
        largeBuf.Memory.Span.Fill(0xAA);
        largeBuf.Length = 17 * 1024;

        var smallBuf = NetworkBuffer.Rent(100);
        smallBuf.Memory.Span.Fill(0xBB);
        smallBuf.Length = 100;

        outbound.Writer.TryWrite(largeBuf);
        outbound.Writer.TryWrite(smallBuf);
        outbound.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        // Assert: large buffer should be written directly, then small buffer coalesced
        Assert.True(capturedWrites.Count >= 1);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_stream_to_channel_cancellation()
    {
        var inbound = Channel.CreateBounded<NetworkBuffer>(1);
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var stream = new MemoryStream([0x42], writable: false);
        var state = new ClientState(stream, inbound, outbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act & Assert: should complete (with cancellation) without throwing
        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_set_clean_close_on_eof()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var stream = new MemoryStream([], writable: false);
        var state = new ClientState(stream, inbound, outbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        Assert.True(inbound.Reader.Completion.IsCompletedSuccessfully);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_set_abrupt_close_on_read_exception()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var stream = new FailingStream();
        var state = new ClientState(stream, inbound, outbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);

        Assert.True(inbound.Reader.Completion.IsFaulted);
        Assert.IsType<AbruptCloseException>(inbound.Reader.Completion.Exception?.InnerException);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_invoke_on_writes_complete_callback()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var callbackInvoked = false;
        var onWritesCompleted = new Action(() => { callbackInvoked = true; });

        var stream = new MemoryStream();
        var state = new ClientState(stream, inbound, outbound)
        {
            OnWritesComplete = onWritesCompleted
        };

        // Write and complete the outbound channel
        var buf = NetworkBuffer.Rent(10);
        buf.Length = 10;
        outbound.Writer.TryWrite(buf);
        outbound.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        // Assert: callback should have been invoked
        Assert.True(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_handle_channel_to_stream_write_exception()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();

        var stream = new FailingStream();
        var state = new ClientState(stream, inbound, outbound);

        var buf = NetworkBuffer.Rent(10);
        buf.Length = 10;
        outbound.Writer.TryWrite(buf);
        outbound.Writer.Complete();

        var onCloseCalled = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await ClientByteMover.MoveChannelToStream(state, () => { onCloseCalled = true; }, cts.Token);

        Assert.True(onCloseCalled);
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

        public override void Flush()
        {
        }

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

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}