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
    private readonly ResponsePipeWriter _writer;

    public TurboHttpResponseBodyFeature()
    {
        _writer = new ResponsePipeWriter(_pipe.Writer);
    }

    internal void SetOnStarting(Func<Task> onStarting) => _writer.SetOnStarting(onStarting);

    internal bool HasStarted => _writer.HasStarted;

    internal Task WhenHeadersReady => _writer.WhenHeadersReady;

    public Stream Stream => field ??= _writer.AsStream();

    public PipeWriter Writer => _writer;

    public Task WhenSinkCompleted => Task.CompletedTask;

    public Sink<ReadOnlyMemory<byte>, Task> BodySink
    {
        get
        {
            if (field == null)
            {
                var pipeSink = PipeSink.To(_pipe.Writer);
                field = Flow.Create<ReadOnlyMemory<byte>>()
                    .SelectAsync(1, chunk =>
                    {
                        _writer.CommitHeaders();
                        return Task.FromResult(chunk);
                    })
                    .ToMaterialized(pipeSink, Keep.Right);
            }

            return field;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _writer.CommitHeaders();
        return Task.CompletedTask;
    }

    public async Task SendFileAsync(string path, long offset, long? count,
        CancellationToken cancellationToken = default)
    {
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

                var dest = _writer.GetMemory(read);
                buffer.AsSpan(0, read).CopyTo(dest.Span);
                _writer.Advance(read);
                await _writer.FlushAsync(cancellationToken);
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
        _writer.Complete();
    }

    public Task CompleteAsync()
    {
        return _writer.CompleteAsync().AsTask();
    }

    public void DisableBuffering()
    {
    }

    internal Source<ReadOnlyMemory<byte>, NotUsed> GetResponseSource()
    {
        return PipeSource.From(_pipe.Reader);
    }

    internal Stream GetResponseStream() => _pipe.Reader.AsStream();

    internal sealed class ResponsePipeWriter : PipeWriter
    {
        private readonly PipeWriter _inner;
        private readonly TaskCompletionSource _headerCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<Task>? _onStarting;
        private bool _started;
        private bool _completed;
        private long _bytesWritten;

        public ResponsePipeWriter(PipeWriter inner)
        {
            _inner = inner;
        }

        public Task WhenHeadersReady => _headerCommit.Task;
        public bool HasStarted => _started;
        public long BytesWritten => _bytesWritten;

        public void SetOnStarting(Func<Task> onStarting) => _onStarting = onStarting;

        public void CommitHeaders()
        {
            if (!_started)
            {
                _started = true;
                _headerCommit.TrySetResult();
            }
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);

        public override void Advance(int bytes)
        {
            _inner.Advance(bytes);
            _bytesWritten += bytes;
        }

        public override void CancelPendingFlush() => _inner.CancelPendingFlush();

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            if (_started)
            {
                return _inner.FlushAsync(cancellationToken);
            }

            return CommitAndFlushAsync(cancellationToken);
        }

        private async ValueTask<FlushResult> CommitAndFlushAsync(CancellationToken cancellationToken)
        {
            _started = true;
            try
            {
                if (_onStarting is not null)
                {
                    await _onStarting();
                }
            }
            finally
            {
                _headerCommit.TrySetResult();
            }

            return await _inner.FlushAsync(cancellationToken);
        }

        public override void Complete(Exception? exception = null)
        {
            if (!_completed)
            {
                _completed = true;
                _inner.Complete(exception);
            }
        }

        public override ValueTask CompleteAsync(Exception? exception = null)
        {
            if (!_completed)
            {
                _completed = true;
                return _inner.CompleteAsync(exception);
            }

            return default;
        }
    }
}
