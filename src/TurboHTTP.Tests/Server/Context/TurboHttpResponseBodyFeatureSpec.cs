using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboHttpResponseBodyFeatureSpec : IDisposable
{
    private readonly ActorSystem _system;
    private readonly IMaterializer _materializer;

    public TurboHttpResponseBodyFeatureSpec()
    {
        _system = ActorSystem.Create("test");
        _materializer = _system.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_write_should_be_readable_from_GetResponseSource()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = Encoding.UTF8.GetBytes("hello");

        await feature.Stream.WriteAsync(bodyBytes);
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("hello", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task PipeWriter_write_should_be_readable_from_GetResponseSource()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = Encoding.UTF8.GetBytes("pipe-data");

        var memory = feature.Writer.GetMemory(bodyBytes.Length);
        bodyBytes.CopyTo(memory);
        feature.Writer.Advance(bodyBytes.Length);
        await feature.Writer.FlushAsync();
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("pipe-data", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task BodySink_should_receive_data_from_akka_source()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var chunk = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("akka-data"));

        await Source.Single(chunk).RunWith(feature.BodySink, _materializer);
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("akka-data", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseSource_should_return_empty_when_nothing_written()
    {
        var feature = new TurboHttpResponseBodyFeature();
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        Assert.Empty(result);
    }

    [Fact(Timeout = 5000)]
    public void Stream_should_return_same_instance()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var s1 = feature.Stream;
        var s2 = feature.Stream;
        Assert.Same(s1, s2);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseStream_should_return_readable_stream()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = Encoding.UTF8.GetBytes("stream-data");

        await feature.Stream.WriteAsync(bodyBytes);
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer);

        Assert.Equal("stream-data", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseStream_should_return_empty_stream_when_nothing_written()
    {
        var feature = new TurboHttpResponseBodyFeature();
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer);

        Assert.Equal(0, bytesRead);
    }

    [Fact(Timeout = 5000)]
    public async Task CompleteAsync_should_be_idempotent()
    {
        var feature = new TurboHttpResponseBodyFeature();
        await feature.Stream.WriteAsync(Encoding.UTF8.GetBytes("data"));
        await feature.CompleteAsync();
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer);

        Assert.Equal("data", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    public void Dispose()
    {
        _system.Dispose();
    }
}
