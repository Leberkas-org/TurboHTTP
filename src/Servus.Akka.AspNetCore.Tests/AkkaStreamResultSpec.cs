using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore.Tests;

public sealed class AkkaStreamResultSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public AkkaStreamResultSpec() : base(ActorSystem.Create("test"))
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
    public async Task Stream_should_write_all_chunks_to_response_body()
    {
        var chunks = new[]
        {
            (ReadOnlyMemory<byte>)"hello "u8.ToArray(),
            (ReadOnlyMemory<byte>)"world"u8.ToArray()
        };
        var source = Source.From(chunks);
        var result = AkkaResults.Stream(source, _materializer, "text/plain");

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        body.Position = 0;
        var content = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal("hello world", content);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_should_set_content_type()
    {
        var source = Source.From([(ReadOnlyMemory<byte>)new byte[] { 1 }]);
        var result = AkkaResults.Stream(source, _materializer, "application/pdf");

        var (ctx, _) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal("application/pdf", ctx.Response.ContentType);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_should_set_status_200()
    {
        var source = Source.From([(ReadOnlyMemory<byte>)new byte[] { 1 }]);
        var result = AkkaResults.Stream(source, _materializer);

        var (ctx, _) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_should_default_to_octet_stream_content_type()
    {
        var source = Source.From([(ReadOnlyMemory<byte>)new byte[] { 1 }]);
        var result = AkkaResults.Stream(source, _materializer);

        var (ctx, _) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal("application/octet-stream", ctx.Response.ContentType);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_should_handle_empty_source()
    {
        var source = Source.Empty<ReadOnlyMemory<byte>>();
        var result = AkkaResults.Stream(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal(0, body.Length);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_should_write_binary_data_correctly()
    {
        var data = new byte[] { 0x00, 0xFF, 0xAB, 0xCD };
        var source = Source.Single((ReadOnlyMemory<byte>)data);
        var result = AkkaResults.Stream(source, _materializer);

        var (ctx, body) = CreateTestContext();
        await result.ExecuteAsync(ctx);

        Assert.Equal(data, body.ToArray());
    }
}