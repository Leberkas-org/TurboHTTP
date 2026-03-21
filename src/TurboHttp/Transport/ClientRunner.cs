using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace TurboHttp.Transport;

internal sealed class ClientRunner : ReceiveActor
{
    private sealed record StreamReady(Stream Stream);
    private sealed record StreamFailed(Exception Exception);

    private readonly IClientProvider _clientProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly IActorRef _selfClosure;
    private readonly IActorRef _handler;
    private readonly int _maxFrameSize;
    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? _inboundChannel;
    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? _outboundChannel;
    private ClientState? _state;

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
        _maxFrameSize = maxFrameSize;
        _inboundChannel = inboundChannel;
        _outboundChannel = outboundChannel;

        Receive<StreamReady>(OnStreamReady);
        Receive<StreamFailed>(OnStreamFailed);
        Receive<DoClose>(_ =>
        {
            _cts.Cancel();
            _handler.Tell(new ClientDisconnected(_clientProvider.RemoteEndPoint ?? new IPEndPoint(IPAddress.None, 0)));
            Context.Self.Tell(PoisonPill.Instance);
        });
    }

    protected override void PreStart()
    {
        _clientProvider.GetStreamAsync(_cts.Token)
            .PipeTo(Self,
                success: stream => new StreamReady(stream),
                failure: ex => new StreamFailed(ex));
    }

    private void OnStreamReady(StreamReady msg)
    {
        _state = new ClientState(_maxFrameSize, msg.Stream, _inboundChannel, _outboundChannel);
        _handler.Tell(new ClientConnected(_clientProvider.RemoteEndPoint!, _state.InboundReader, _state.OutboundWriter));

        var log = Context.GetLogger();
        var self = _selfClosure;

        var t1 = ClientByteMover.MoveStreamToPipe(_state, _selfClosure, log, _cts.Token);
        _ = t1.ContinueWith(
            t => { log.Error(t.Exception, "ClientRunner: MoveStreamToPipe faulted unexpectedly"); self.Tell(DoClose.Instance); },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        var t2 = ClientByteMover.MovePipeToChannel(_state, _selfClosure, log, _cts.Token);
        _ = t2.ContinueWith(
            t => { log.Error(t.Exception, "ClientRunner: MovePipeToChannel faulted unexpectedly"); self.Tell(DoClose.Instance); },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        var t3 = ClientByteMover.MoveChannelToStream(_state, _selfClosure, log, _cts.Token);
        _ = t3.ContinueWith(
            t => { log.Error(t.Exception, "ClientRunner: MoveChannelToStream faulted unexpectedly"); self.Tell(DoClose.Instance); },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private void OnStreamFailed(StreamFailed msg)
    {
        Context.GetLogger().Error(msg.Exception, "ClientRunner: Failed to establish connection");
        _handler.Tell(new ClientDisconnected(_clientProvider.RemoteEndPoint ?? new IPEndPoint(IPAddress.None, 0)));
        Context.Self.Tell(PoisonPill.Instance);
    }

    protected override void PostStop()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (_state is null)
        {
            _cts.Dispose();
            return;
        }

        try
        {
            _state.Dispose();
            _clientProvider.Close();
            _cts.Dispose();
        }
        catch (Exception ex)
        {
            Context.GetLogger().Warning(ex, "Failed to cleanly dispose of TCP client and stream.");
        }
    }
}
