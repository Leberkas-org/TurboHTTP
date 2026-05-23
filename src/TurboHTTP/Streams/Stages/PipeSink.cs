using System.IO.Pipelines;
using Akka.Streams.Dsl;
using Servus.Akka.Streams.IO;

namespace TurboHTTP.Streams.Stages;

internal sealed class PipeSink : IAsyncDisposable
{
    private readonly Pipe _pipe;
    private bool _readerCompleted;

    public PipeSink() : this(PipeOptions.Default)
    {
    }

    internal PipeSink(PipeOptions options)
    {
        _pipe = new Pipe(options);
    }

    public Sink<ReadOnlyMemory<byte>, Task> Sink
    {
        get
        {
            field ??= StreamSink.To(_pipe.Writer);
            return field;
        }
    }

    public PipeReader Reader => _pipe.Reader;

    public Stream AsStream() => _pipe.Reader.AsStream(leaveOpen: true);

    public async ValueTask CompleteAsync(Exception? exception = null)
    {
        if (!_readerCompleted)
        {
            _readerCompleted = true;
            await _pipe.Reader.CompleteAsync(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_readerCompleted)
        {
            _readerCompleted = true;
            await _pipe.Reader.CompleteAsync();
        }

        await _pipe.Writer.CompleteAsync();
    }
}