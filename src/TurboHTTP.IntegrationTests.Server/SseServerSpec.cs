using System.Net;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SseServerSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/echo", () => Results.Ok("ok"));
        routeTable.Add("GET", "/text", () => Results.Ok("hello world"));
        routeTable.Add("GET", "/events", () =>
        {
            var source = Source.From(["event1", "event2"]);
            return TurboStreamResults.EventStream(source);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_basic_request()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/echo"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_text_request()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("hello world", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_correct_content_type()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/text"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/json", response.Content.Headers.ContentType.MediaType ?? "");
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/nonexistent"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_stream_sse_events()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/events"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("data: event1\n\n", body);
        Assert.Contains("data: event2\n\n", body);
    }
}
