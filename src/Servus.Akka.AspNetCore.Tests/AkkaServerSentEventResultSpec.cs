using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Sse;

namespace Servus.Akka.AspNetCore.Tests;

public sealed class AkkaServerSentEventResultSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public AkkaServerSentEventResultSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    private static (DefaultHttpContext Context, MemoryStream Body) CreateTestContext()
    {
        var body = new MemoryStream();
        var ctx = new DefaultHttpContext
        {
            Response =
            {
                Body = body
            }
        };
        return (ctx, body);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_set_content_type_to_event_stream()
    {
        var source = Source.From([new ServerSentEvent("hello")]);
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, _) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal("text/event-stream", ctx.Response.ContentType);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_set_status_200()
    {
        var source = Source.From([new ServerSentEvent("hello")]);
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, _) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_format_single_event()
    {
        var source = Source.From([new ServerSentEvent("hello")]);
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        body.Position = 0;
        var content = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal("data: hello\n\n", content);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_format_multiple_events()
    {
        var events = new[]
        {
            new ServerSentEvent("first"),
            new ServerSentEvent("second")
        };
        var source = Source.From(events);
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        body.Position = 0;
        var content = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal("data: first\n\ndata: second\n\n", content);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_format_event_with_type_and_id()
    {
        var source = Source.From([
            new ServerSentEvent("payload", EventType: "update", Id: "42")
        ]);
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        body.Position = 0;
        var content = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("event: update\n", content);
        Assert.Contains("data: payload\n", content);
        Assert.Contains("id: 42\n", content);
    }

    [Fact(Timeout = 5000)]
    public async Task Sse_should_handle_empty_source()
    {
        var source = Source.Empty<ServerSentEvent>();
        var result = AkkaResults.ServerSentEvent(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal(0, body.Length);
    }
}