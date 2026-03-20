using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace TurboHttp.IO;

internal sealed class ClientState : IDisposable
{
    public int MaxFrameSize { get; }
    public Stream Stream { get; }

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _inboundChannel;
    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _outboundChannel;

    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundReader => _outboundChannel.Reader;
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundWriter => _outboundChannel.Writer;

    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> InboundReader => _inboundChannel.Reader;
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> InboundWriter => _inboundChannel.Writer;
    public Pipe Pipe { get; }

    public ClientState(int maxFrameSize, Stream stream,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? inboundChannel,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? outboundChannel)
    {
        _inboundChannel = inboundChannel ?? Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();
        _outboundChannel = outboundChannel ?? Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

        MaxFrameSize = maxFrameSize;
        Stream = stream;
        Pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: GetBufferSize(),
            resumeWriterThreshold: GetBufferSize() / 2,
            useSynchronizationContext: false));
    }


    public Memory<byte> GetWriteMemory() => Pipe.Writer.GetMemory(MaxFrameSize / 4);

    private int GetBufferSize()
    {
        return MaxFrameSize switch
        {
            // if the max frame size is under 128kb, scale it up to 512kb
            <= 128 * 1024 => 512 * 1024,
            // between 128kb and 1mb, scale it up to 2mb
            <= 1024 * 1024 => 2 * 1024 * 1024,
            // if the max frame size is above 1mb, 2x it
            _ => MaxFrameSize * 2
        };
    }

    public void Dispose()
    {
        // Complete both writers so no new items can be enqueued
        _inboundChannel.Writer.TryComplete();
        _outboundChannel.Writer.TryComplete();

        // Drain inbound channel and dispose all pending IMemoryOwner<byte> items
        while (_inboundChannel.Reader.TryRead(out var inboundItem))
        {
            inboundItem.buffer.Dispose();
        }

        // Drain outbound channel and dispose all pending IMemoryOwner<byte> items
        while (_outboundChannel.Reader.TryRead(out var outboundItem))
        {
            outboundItem.buffer.Dispose();
        }

        Pipe.Reader.Complete();
        Pipe.Writer.Complete();
        Stream.Dispose();
    }
}