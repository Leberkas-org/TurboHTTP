using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Akka.Util;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseBodyFeature : IHttpResponseBodyFeature, ITurboResponseBodyFeature
{
    private readonly Pipe _pipe = new();
    private bool _completed;

    public Stream Stream => field ??= _pipe.Writer.AsStream();

    public PipeWriter Writer => _pipe.Writer;

    public Sink<ReadOnlyMemory<byte>, Task> BodySink
    {
        get
        {
            if (field == null)
            {
                var sink = Sink.ForEachAsync<ReadOnlyMemory<byte>>(1, async chunk =>
                {
                    var memory = _pipe.Writer.GetMemory(chunk.Length);
                    chunk.CopyTo(memory);
                    _pipe.Writer.Advance(chunk.Length);
                    await _pipe.Writer.FlushAsync();
                });
                field = sink.MapMaterializedValue(task =>
                    task.ContinueWith(_ => Task.CompletedTask, TaskScheduler.Default).Unwrap());
            }

            return field;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
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
        var writerStream = _pipe.Writer.AsStream();
        var buffer = new byte[4 * 1024];
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await writerStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
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
        return Source.UnfoldAsync(_pipe.Reader, async reader =>
        {
            var readResult = await reader.ReadAsync();
            var buffer = readResult.Buffer;

            if (buffer.IsEmpty && readResult.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                return Option<(PipeReader, ReadOnlyMemory<byte>)>.None;
            }

            if (buffer.IsEmpty)
            {
                reader.AdvanceTo(buffer.Start);
                return Option<(PipeReader, ReadOnlyMemory<byte>)>.None;
            }

            byte[] bytes;
            if (buffer.IsSingleSegment)
            {
                bytes = buffer.FirstSpan.ToArray();
            }
            else
            {
                bytes = new byte[buffer.Length];
                var offset = 0;
                foreach (var segment in buffer)
                {
                    segment.Span.CopyTo(bytes.AsSpan(offset));
                    offset += segment.Length;
                }
            }

            reader.AdvanceTo(buffer.End);

            return (reader, new ReadOnlyMemory<byte>(bytes));
        });
    }

    internal Stream GetResponseStream() => _pipe.Reader.AsStream();
}