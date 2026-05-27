using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class TurboStreamResultsSpec : IDisposable
{
    private readonly ActorSystem _system;
    private readonly IMaterializer _materializer;

    public TurboStreamResultsSpec()
    {
        _system = ActorSystem.Create("test");
        _materializer = _system.Materializer();
    }

    [Fact(Timeout = 5000)]
    public void EventStream_should_return_IResult()
    {
        var source = Source.Single("hello");
        var result = TurboStreamResults.EventStream(source);
        Assert.IsAssignableFrom<IResult>(result);
    }

    [Fact(Timeout = 5000)]
    public void Stream_should_return_IResult()
    {
        var source = Source.Single(new ReadOnlyMemory<byte>("Hello"u8.ToArray()));
        var result = TurboStreamResults.Stream(source);
        Assert.IsAssignableFrom<IResult>(result);
    }

    [Fact(Timeout = 5000)]
    public void Stream_with_content_type_should_return_IResult()
    {
        var source = Source.Single(new ReadOnlyMemory<byte>("Hello"u8.ToArray()));
        var result = TurboStreamResults.Stream(source, "application/json");
        Assert.IsAssignableFrom<IResult>(result);
    }

    [Fact(Timeout = 5000)]
    public async Task AkkaStreamResult_should_materialize_source_into_pipe_writer()
    {
        var ctx = CreateTestContext();
        var source = Source.From([
            new ReadOnlyMemory<byte>("chunk1"u8.ToArray()),
            new ReadOnlyMemory<byte>("chunk2"u8.ToArray())
        ]);

        var result = TurboStreamResults.Stream(source, "application/octet-stream");
        await result.ExecuteAsync(ctx);

        var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
        var chunks = await bodyFeature!.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var body = string.Concat(chunks.Select(c => Encoding.UTF8.GetString(c.Span)));
        Assert.Equal("chunk1chunk2", body);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("application/octet-stream", ctx.Response.ContentType);
    }

    [Fact(Timeout = 5000)]
    public async Task EventStreamResult_should_format_as_sse_and_materialize()
    {
        var ctx = CreateTestContext();
        var source = Source.From(["event1", "event2"]);

        var result = TurboStreamResults.EventStream(source);
        await result.ExecuteAsync(ctx);

        var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
        var chunks = await bodyFeature!.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var body = string.Concat(chunks.Select(c => Encoding.UTF8.GetString(c.Span)));
        Assert.Contains("data: event1\n\n", body);
        Assert.Contains("data: event2\n\n", body);
        Assert.Equal("text/event-stream", ctx.Response.ContentType);
    }

    private TurboHttpContext CreateTestContext()
    {
        var features = new TurboFeatureCollection();
        var responseFeature = new TurboHttpResponseFeature();
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new FakeServiceProvider(),
            CancellationToken.None, null!)
        {
            Materializer = _materializer
        };
    }

    public void Dispose()
    {
        _system.Dispose();
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
