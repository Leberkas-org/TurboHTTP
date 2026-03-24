using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

public sealed record DoClose
{
    public static readonly DoClose Instance = new();
}

internal static class ClientByteMover
{
    internal static async Task MoveStreamToPipe(ClientState state, IActorRef runner, ILoggingAdapter log, CancellationToken ct)
    {
        Exception? pipeError = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var bytesRead = await state.Stream.ReadAsync(state.GetWriteMemory(), ct).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        // ReadAsync returning 0 means:
                        // - SslStream: close_notify was received (clean TLS closure)
                        // - NetworkStream: TCP FIN was received (clean TCP close)
                        //
                        // Do NOT send DoClose here. The pipe writer is completed in
                        // the finally block, which signals MovePipeToChannel to drain
                        // remaining data and then send DoClose itself. Sending DoClose
                        // here cancels the CTS before MovePipeToChannel finishes reading,
                        // causing response data still in the pipe to be lost.
                        state.CloseKind = TlsCloseKind.CleanClose;
                        return;
                    }

                    state.Pipe.Writer.Advance(bytesRead);
                }
                catch (OperationCanceledException)
                {
                    // no need to log here
                    return;
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "ClientByteMover.MoveStreamToPipe: stream read faulted");
                    pipeError = ex;
                    // IOException from SslStream without close_notify, or socket error:
                    // treat as abrupt close — partially received data is unreliable.
                    state.CloseKind = TlsCloseKind.AbruptClose;
                    runner.Tell(DoClose.Instance);
                    return;
                }

                // make data available to PipeReader
                var result = await state.Pipe.Writer.FlushAsync(ct);
                if (result.IsCompleted)
                {
                    return;
                }
            }
        }
        finally
        {
            // Always complete the pipe writer on any exit path so that ReadFromPipeAsync
            // can detect writer completion via result.IsCompleted rather than depending
            // solely on CancellationToken callback timing. Without this, ReadFromPipeAsync
            // can stall indefinitely on a loaded CI system if the cancellation callback
            // dispatch is delayed by thread pool pressure.
            await state.Pipe.Writer.CompleteAsync(pipeError).ConfigureAwait(false);
        }
    }

    internal static async Task MovePipeToChannel(ClientState state, IActorRef runner, ILoggingAdapter log, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await state.Pipe.Reader.ReadAsync(ct);
                    if (result.IsCanceled)
                    {
                        // PipeReader.ReadAsync can return with IsCanceled=true when the token is
                        // cancelled rather than throwing OperationCanceledException. In that case
                        // the buffer is empty and we must not write a zero-length entry into
                        // _readsFromTransport. Advance past the empty buffer and exit cleanly.
                        state.Pipe.Reader.AdvanceTo(result.Buffer.Start);
                        runner.Tell(DoClose.Instance);
                        return;
                    }

                    // consume this entire sequence by copying it into a pooled buffer
                    var buffer = result.Buffer;
                    var length = (int)buffer.Length;
                    if (length > 0)
                    {
                        var pooled = MemoryPool<byte>.Shared.Rent(length);
                        buffer.CopyTo(pooled.Memory.Span);
                        if (!state.InboundWriter.TryWrite((pooled, length)))
                        {
                            pooled.Dispose();
                        }
                    }

                    // tell the pipe we're done with this data
                    state.Pipe.Reader.AdvanceTo(buffer.End);

                    if (!result.IsCompleted) continue;
                    runner.Tell(DoClose.Instance);
                    return;
                }
                catch (OperationCanceledException)
                {
                    runner.Tell(DoClose.Instance);
                    return;
                }
                catch (Exception ex)
                {
                    // PipeWriter was completed with an exception (e.g. socket IOException propagated
                    // through DoWriteToPipeAsync). The faulted pipe surfaces as an exception here
                    // rather than as result.IsCompleted, so we must handle it explicitly to ensure
                    // ReadFinished is always self-told and BackgroundTasksCompleted can fire.
                    log.Warning(ex, "ClientByteMover.MovePipeToChannel: pipe read faulted");
                    state.CloseKind ??= TlsCloseKind.AbruptClose;
                    runner.Tell(DoClose.Instance);
                    return;
                }
            }
        }
        finally
        {
            // Complete the inbound channel so ConnectionStage's pump loop exits.
            // For abrupt close, complete with an exception so the pump can distinguish it.
            if (state.CloseKind == TlsCloseKind.AbruptClose)
            {
                state.InboundWriter.TryComplete(new AbruptCloseException());
            }
            else
            {
                state.InboundWriter.TryComplete();
            }
        }
    }

    internal static async Task MoveChannelToStream(ClientState state, IActorRef runner, ILoggingAdapter log, CancellationToken ct)
    {
        while (!state.OutboundReader.Completion.IsCompleted)
        {
            try
            {
                while (await state.OutboundReader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (state.OutboundReader.TryRead(out var item))
                    {
                        var (buffer, readableBytes) = item;
                        try
                        {
                            var workingBuffer = buffer.Memory;
                            while (readableBytes > 0 && state.Stream is not null)
                            {
                                var slice = workingBuffer[..readableBytes];
                                await state.Stream.WriteAsync(slice, ct).ConfigureAwait(false);
                                readableBytes = 0;
                            }
                        }
                        finally
                        {
                            // free the pooled buffer
                            buffer.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                runner.Tell(DoClose.Instance);
                return;
            }
            catch (Exception ex)
            {
                log.Warning(ex, "ClientByteMover.MoveChannelToStream: stream write faulted");
                runner.Tell(DoClose.Instance);
                return;
            }
        }
    }
}