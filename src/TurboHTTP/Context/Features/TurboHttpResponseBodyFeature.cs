using System.Buffers;
using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Streams.IO;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseBodyFeature : IHttpResponseBodyFeature
{
    private readonly Pipe _pipe = new();
    private readonly TaskCompletionSource _headerCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<Task>? _onStarting;
    private bool _completed;

    internal void SetOnStarting(Func<Task> onStarting) => _onStarting = onStarting;

    internal bool HasStarted { get; private set; }

    internal Task WhenHeadersReady => _headerCommit.Task;

    public Stream Stream => field ??= _pipe.Writer.AsStream();

    public PipeWriter Writer => _pipe.Writer;

    public Task WhenSinkCompleted => Task.CompletedTask;

    public Sink<ReadOnlyMemory<byte>, Task> BodySink
    {
        get
        {
            if (field == null)
            {
                var pipeSink = PipeSink.To(_pipe.Writer);
                field = Flow.Create<ReadOnlyMemory<byte>>()
                    .SelectAsync(1, async chunk =>
                    {
                        await EnsureStartedAsync();
                        return chunk;
                    })
                    .ToMaterialized(pipeSink, Keep.Right);
            }

            return field;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync();
    }

    public async Task SendFileAsync(string path, long offset, long? count,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024,
            useAsync: true);
        if (offset > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
        }

        var remaining = count ?? long.MaxValue;
        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024);
        try
        {
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var dest = _pipe.Writer.GetMemory(read);
                buffer.AsSpan(0, read).CopyTo(dest.Span);
                _pipe.Writer.Advance(read);
                await _pipe.Writer.FlushAsync(cancellationToken);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal void Complete()
    {
        if (!_completed)
        {
            _completed = true;
            _pipe.Writer.Complete();
        }
    }

    public async Task CompleteAsync()
    {
        if (!_completed)
        {
            _completed = true;
            await _pipe.Writer.CompleteAsync();
        }
    }

    public void DisableBuffering()
    {
    }

    internal Source<ReadOnlyMemory<byte>, NotUsed> GetResponseSource()
    {
        return PipeSource.From(_pipe.Reader);
    }

    internal Stream GetResponseStream() => _pipe.Reader.AsStream();

    private async Task EnsureStartedAsync()
    {
        if (!HasStarted)
        {
            HasStarted = true;
            if (_onStarting is not null)
            {
                await _onStarting();
            }

            _headerCommit.TrySetResult();
        }
    }
}