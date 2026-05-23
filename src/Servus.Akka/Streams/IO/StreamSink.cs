using System.IO.Pipelines;
using Akka.Streams.Dsl;

namespace Servus.Akka.Streams.IO;

public static class StreamSink
{
    public static Sink<ReadOnlyMemory<byte>, Task> To(PipeWriter writer)
    {
        return Sink.FromGraph(new PipeWriterSinkStage(writer));
    }

    public static Sink<ReadOnlyMemory<byte>, Task> To(Stream stream)
    {
        return To(PipeWriter.Create(stream));
    }
}
