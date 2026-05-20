using System.Net;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class HttpContextBidiStageSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Stage_should_create_context_from_request_and_extract_response()
    {
        var connectionInfo = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = new ServiceCollection().BuildServiceProvider();

        var bidi = BidiFlow.FromGraph(
            new HttpContextBidiStage(connectionInfo, services, CancellationToken.None));

        var innerFlow = Flow.Create<TurboHttpContext>()
            .Select(ctx =>
            {
                ctx.Response.StatusCode = 200;
                return ctx;
            });

        var pipeline = bidi.Join(innerFlow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var result = await Source.Single(request)
            .Via(pipeline)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_populate_request_features()
    {
        var connectionInfo = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = new ServiceCollection().BuildServiceProvider();

        var bidi = BidiFlow.FromGraph(
            new HttpContextBidiStage(connectionInfo, services, CancellationToken.None));

        string? capturedMethod = null;
        string? capturedPath = null;

        var innerFlow = Flow.Create<TurboHttpContext>()
            .Select(ctx =>
            {
                capturedMethod = ctx.Request.Method;
                capturedPath = ctx.Request.Path.Value;
                return ctx;
            });

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/orders");
        await Source.Single(request)
            .Via(bidi.Join(innerFlow))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal("POST", capturedMethod);
        Assert.Equal("/api/orders", capturedPath);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_500_on_downstream_stream_failure()
    {
        var connectionInfo = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = new ServiceCollection().BuildServiceProvider();

        var bidi = BidiFlow.FromGraph(
            new HttpContextBidiStage(connectionInfo, services, CancellationToken.None));

        var failingFlow = Flow.Create<TurboHttpContext>()
            .Select(ctx =>
            {
                throw new InvalidOperationException("boom");
#pragma warning disable CS0162 // Unreachable code
                return ctx;
#pragma warning restore CS0162 // Unreachable code
            });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var result = await Source.Single(request)
            .Via(bidi.Join(failingFlow))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_wire_body_source_for_request_with_content()
    {
        var connectionInfo = new TurboConnectionInfo("test", null, 0, null, 0);
        var services = new ServiceCollection().BuildServiceProvider();

        var bidi = BidiFlow.FromGraph(
            new HttpContextBidiStage(connectionInfo, services, CancellationToken.None));

        ITurboRequestBodyFeature? capturedFeature = null;
        var innerFlow = Flow.Create<TurboHttpContext>()
            .Select(ctx =>
            {
                capturedFeature = ctx.Features.Get<ITurboRequestBodyFeature>();
                return ctx;
            });

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };

        await Source.Single(request)
            .Via(bidi.Join(innerFlow))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.NotNull(capturedFeature);
        Assert.NotNull(capturedFeature!.BodySource);
    }
}
