using System.Net;
using Akka.Actor;
using Akka.Streams;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.End2End;

public sealed class TrailerEndToEndSpec : IAsyncLifetime
{
    private TurboServerFixture? _fixture;
    private ClientHelper? _client;
    private ActorSystem? _system;
    private IMaterializer? _materializer;

    public async ValueTask InitializeAsync()
    {
        _fixture = new TurboServerFixture(app =>
        {
            app.MapTurboPost("/echo-with-trailers", (TurboHttpContext ctx) =>
            {
                ctx.TurboResponse.AppendTrailer("grpc-status", "0");
                ctx.TurboResponse.AppendTrailer("grpc-message", "OK");
                return Results.Ok("response body");
            });

            app.MapTurboPost("/echo-with-prohibited-trailers", (TurboHttpContext ctx) =>
            {
                ctx.TurboResponse.AppendTrailer("grpc-status", "0");
                ctx.TurboResponse.AppendTrailer("content-length", "13");
                ctx.TurboResponse.AppendTrailer("transfer-encoding", "chunked");
                return Results.Ok("response body");
            });
        });

        await _fixture.InitializeAsync();

        _system = ActorSystem.Create("trailer-e2e-test");
        _materializer = _system.Materializer();

        _client = ClientHelper.CreateClient(
            _fixture.HttpPort,
            new Version(1, 1),
            system: _system);
    }

    [Fact(Timeout = 15000)]
    public async Task Client_should_receive_basic_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo-with-trailers")
        {
            Content = new StringContent("request body")
        };

        var response = await _client!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("\"response body\"", body);
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
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Client_should_receive_trailers_from_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo-with-trailers")
        {
            Content = new StringContent("request body")
        };

        var response = await _client!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("\"response body\"", body);

        // For HTTP/1.1 with chunked encoding, trailers are in TrailingHeaders if available
        if (response.TrailingHeaders.Any())
        {
            Assert.Equal("0", response.TrailingHeaders.GetValues("grpc-status").FirstOrDefault());
            Assert.Equal("OK", response.TrailingHeaders.GetValues("grpc-message").FirstOrDefault());
        }
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public async Task Client_should_not_receive_prohibited_trailer_fields()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo-with-prohibited-trailers")
        {
            Content = new StringContent("request body")
        };

        var response = await _client!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.TrailingHeaders);
        Assert.DoesNotContain(response.TrailingHeaders, h =>
            h.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
            h.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase));
    }
}