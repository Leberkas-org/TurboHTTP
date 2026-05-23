using System.IO.Pipelines;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.Streams.IO;

namespace Servus.Akka.Tests.Streams.IO;

public sealed class PipeWriterSinkStageSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public PipeWriterSinkStageSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_write_data_to_pipe_reader()
    {
        var pipe = new Pipe();
        var sink = StreamSink.To(pipe.Writer);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        await Source.Single((ReadOnlyMemory<byte>)data.AsMemory())
            .RunWith(sink, _materializer);

        var readResult = await pipe.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal(data, readResult.Buffer.FirstSpan.ToArray());
        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        var finalRead = await pipe.Reader.ReadAsync(CancellationToken.None);
        Assert.True(finalRead.IsCompleted);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_write_multiple_chunks_to_pipe_reader()
    {
        var pipe = new Pipe();
        var sink = StreamSink.To(pipe.Writer);

        var chunks = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        var writeTask = Source.From(chunks.Select(c => (ReadOnlyMemory<byte>)c.AsMemory()))
            .RunWith(sink, _materializer);

        var total = new List<byte>();
        while (true)
        {
            var readResult = await pipe.Reader.ReadAsync(CancellationToken.None);
            foreach (var segment in readResult.Buffer)
            {
                total.AddRange(segment.ToArray());
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync();
        await writeTask;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, total.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_complete_task_when_upstream_finishes()
    {
        var pipe = new Pipe();
        var sink = StreamSink.To(pipe.Writer);

        var task = Source.Empty<ReadOnlyMemory<byte>>()
            .RunWith(sink, _materializer);

        await task;

        var readResult = await pipe.Reader.ReadAsync(CancellationToken.None);
        Assert.True(readResult.IsCompleted);
        Assert.True(readResult.Buffer.IsEmpty);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_fault_task_when_upstream_fails()
    {
        var pipe = new Pipe();
        var sink = StreamSink.To(pipe.Writer);

        var error = new InvalidOperationException("test failure");
        var task = Source.Failed<ReadOnlyMemory<byte>>(error)
            .RunWith(sink, _materializer);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("test failure", ex.Message);
        await pipe.Reader.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_should_skip_empty_chunks()
    {
        var pipe = new Pipe();
        var sink = StreamSink.To(pipe.Writer);

        var chunks = new[]
        {
            ReadOnlyMemory<byte>.Empty,
            (ReadOnlyMemory<byte>)new byte[] { 1, 2, 3 },
            ReadOnlyMemory<byte>.Empty
        };

        var writeTask = Source.From(chunks)
            .RunWith(sink, _materializer);

        var total = new List<byte>();
        while (true)
        {
            var readResult = await pipe.Reader.ReadAsync(CancellationToken.None);
            foreach (var segment in readResult.Buffer)
            {
                total.AddRange(segment.ToArray());
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync();
        await writeTask;
        Assert.Equal(new byte[] { 1, 2, 3 }, total.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task Sink_to_stream_should_write_data()
    {
        var memoryStream = new MemoryStream();
        var sink = StreamSink.To(memoryStream);

        var data = new byte[] { 10, 20, 30 };
        await Source.Single((ReadOnlyMemory<byte>)data.AsMemory())
            .RunWith(sink, _materializer);

        Assert.Equal(data, memoryStream.ToArray());
    }
}
