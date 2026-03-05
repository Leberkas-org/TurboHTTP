using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;

namespace TurboHttp.IO;

public class ClientRunner : ReceiveActor
{
    private readonly IClientProvider _clientProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly IActorRef _selfClosure;
    private readonly IActorRef _handler;
    private readonly ClientState _state;

    public record ClientConnected(
        EndPoint RemoteEndPoint,
        ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> InboundReader,
        ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundWriter) : IDeadLetterSuppression;

    public record ClientDisconnected(EndPoint RemoteEndPoint) : IDeadLetterSuppression;

    public ClientRunner(IClientProvider clientProvider, IActorRef handler, int maxFrameSize,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? inboundChannel = null,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? outboundChannel = null)
    {
        _clientProvider = clientProvider;
        _handler = handler;
        _selfClosure = Context.Self;
        var stream = _clientProvider.GetStream();
        _state = new ClientState(maxFrameSize, stream, inboundChannel, outboundChannel);

        Receive<DoClose>(_ =>
        {
            _cts.Cancel();
            _handler.Tell(new ClientDisconnected(_clientProvider.RemoteEndPoint!));
            Context.Self.Tell(PoisonPill.Instance);
        });
    }

    protected override void PreStart()
    {
        _handler.Tell(new ClientConnected(_clientProvider.RemoteEndPoint!, _state.InboundReader,
            _state.OutboundWriter));

        _ = ClientByteMover.MoveStreamToPipe(_state, _selfClosure, _cts.Token);
        _ = ClientByteMover.MovePipeToChannel(_state, _selfClosure, _cts.Token);
        _ = ClientByteMover.MoveChannelToStream(_state, _selfClosure, _cts.Token);
    }

    protected override void PostStop()
    {
        _state.InboundWriter.TryComplete();
        _state.OutboundWriter.TryComplete();

        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        try
        {
            _state.Pipe.Reader.Complete();
            _state.Pipe.Writer.Complete();
            _state.Stream.Close();
            _state.Stream.Dispose();
        }
        catch (Exception ex)
        {
            Context.GetLogger().Warning(ex, "Failed to cleanly dispose of TCP client and stream.");
        }
    }
}