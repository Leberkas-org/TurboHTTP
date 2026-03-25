using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

internal static class ClientByteMover
{
    internal static async Task MoveStreamToPipe(ClientState state, Action onClose, CancellationToken ct)
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
                        state.CloseKind = TlsCloseKind.CleanClose;
                        return;
                    }

                    state.Pipe.Writer.Advance(bytesRead);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    pipeError = ex;
                    state.CloseKind = TlsCloseKind.AbruptClose;
                    onClose();
                    return;
                }

                var result = await state.Pipe.Writer.FlushAsync(ct);
                if (result.IsCompleted)
                {
                    return;
                }
            }
        }
        finally
        {
            await state.Pipe.Writer.CompleteAsync(pipeError).ConfigureAwait(false);
        }
    }

    internal static async Task MovePipeToChannel(ClientState state, Action onClose, CancellationToken ct)
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
                        state.Pipe.Reader.AdvanceTo(result.Buffer.Start);
                        onClose();
                        return;
                    }

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

                    state.Pipe.Reader.AdvanceTo(buffer.End);

                    if (!result.IsCompleted) continue;
                    onClose();
                    return;
                }
                catch (OperationCanceledException)
                {
                    onClose();
                    return;
                }
                catch (Exception)
                {
                    state.CloseKind ??= TlsCloseKind.AbruptClose;
                    onClose();
                    return;
                }
            }
        }
        finally
        {
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

    internal static async Task MoveChannelToStream(ClientState state, Action onClose, CancellationToken ct)
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
                            buffer.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                onClose();
                return;
            }
            catch (Exception)
            {
                onClose();
                return;
            }
        }
    }
}