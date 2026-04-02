using System.Threading.Channels;
using TurboHttp.Internal;
using TurboHttp.Transport.Quic;

namespace TurboHttp.Transport.Connection;

internal sealed class ClientState : IDisposable
{
    public int MaxFrameSize { get; }
    public Stream Stream { get; }
    public StreamDirection Direction { get; }

    /// <summary>
    /// Indicates how the transport connection was closed.
    /// Set by <see cref="ClientByteMover"/> when the read loop exits.
    /// </summary>
    public TlsCloseKind? CloseKind { get; set; }

    private readonly Channel<NetworkBuffer> _inboundChannel;
    private readonly Channel<NetworkBuffer> _outboundChannel;

    public ChannelReader<NetworkBuffer> OutboundReader => _outboundChannel.Reader;
    public ChannelWriter<NetworkBuffer> OutboundWriter => _outboundChannel.Writer;

    public ChannelReader<NetworkBuffer> InboundReader => _inboundChannel.Reader;
    public ChannelWriter<NetworkBuffer> InboundWriter => _inboundChannel.Writer;

    public ClientState(int maxFrameSize, Stream stream,
        Channel<NetworkBuffer>? inboundChannel,
        Channel<NetworkBuffer>? outboundChannel,
        StreamDirection direction = StreamDirection.Bidirectional)
    {
        MaxFrameSize = maxFrameSize;
        Stream = stream;
        Direction = direction;

        switch (direction)
        {
            case StreamDirection.WriteOnly:
                // Write-only: outbound channel needed; inbound channel is pre-completed
                // so read pumps exit immediately without deadlocking.
                _outboundChannel = outboundChannel ?? Channel.CreateUnbounded<NetworkBuffer>();
                _inboundChannel = CreateCompletedChannel();
                break;

            case StreamDirection.ReadOnly:
                // Read-only: inbound channel needed; outbound channel is pre-completed
                // so write pump exits immediately without deadlocking.
                _inboundChannel = inboundChannel ?? Channel.CreateUnbounded<NetworkBuffer>();
                _outboundChannel = CreateCompletedChannel();
                break;

            default: // Bidirectional
                _inboundChannel = inboundChannel ?? Channel.CreateUnbounded<NetworkBuffer>();
                _outboundChannel = outboundChannel ?? Channel.CreateUnbounded<NetworkBuffer>();
                break;
        }
    }

    private static Channel<NetworkBuffer> CreateCompletedChannel()
    {
        var channel = Channel.CreateUnbounded<NetworkBuffer>();
        channel.Writer.TryComplete();
        return channel;
    }

    public void Dispose()
    {
        // Complete both writers so no new items can be enqueued
        _inboundChannel.Writer.TryComplete();
        _outboundChannel.Writer.TryComplete();

        // Drain inbound channel and dispose all pending NetworkBuffer items
        while (_inboundChannel.Reader.TryRead(out var buf))
        {
            buf.Dispose();
        }

        // Drain outbound channel and dispose all pending NetworkBuffer items
        while (_outboundChannel.Reader.TryRead(out var buf))
        {
            buf.Dispose();
        }

        Stream.Dispose();
    }
}