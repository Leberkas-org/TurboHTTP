using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Client;
using TurboHTTP.Features.Sse;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.End2End;

public sealed class SseEndToEndSpec : IAsyncLifetime
{
    private TurboServerFixture? _fixture;
    private ClientHelper? _client;
    private ActorSystem? _system;
    private IMaterializer? _materializer;

    public async ValueTask InitializeAsync()
    {
        _fixture = new TurboServerFixture(app =>
        {
            app.MapGet("/events", () =>
            {
                var source = Source.From(["hello", "world"])
                    .Select(msg => msg);
                return TurboStreamResults.EventStream(source);
            });

            app.MapGet("/echo", () => Results.Ok("ok"));
        });

        await _fixture.InitializeAsync();

        _system = ActorSystem.Create("e2e-test");
        _materializer = _system.Materializer();

        _client = ClientHelper.CreateClient(
            _fixture.HttpPort,
            new Version(1, 1),
            system: _system);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_system is not null)
        {
            await _system.Terminate();
        }

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task TurboClient_should_receive_response_from_turbo_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/echo");
        var response = await _client!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task TurboClient_should_consume_sse_from_turbo_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/events");
        var response = await _client!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer!);

        Assert.Equal(2, events.Count);
        Assert.Equal("hello", events[0].Data);
        Assert.Equal("world", events[1].Data);
    }
}
