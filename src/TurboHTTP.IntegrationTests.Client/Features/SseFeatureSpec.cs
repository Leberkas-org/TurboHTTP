using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Client;
using TurboHTTP.Features.Sse;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Features;

public sealed class SseFeatureSpec : FeatureSpecBase
{
    public SseFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Simple_should_parse_two_data_events(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/simple"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Equal(2, events.Count);
        Assert.Equal("hello", events[0].Data);
        Assert.Equal("world", events[1].Data);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Typed_should_preserve_event_type(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/typed"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Equal(2, events.Count);
        Assert.Equal("greeting", events[0].EventType);
        Assert.Equal("hello", events[0].Data);
        Assert.Equal("farewell", events[1].EventType);
        Assert.Equal("goodbye", events[1].Data);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Multi_should_receive_all_events(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/multi?n=10"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Equal(10, events.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(string.Concat("event-", i), events[i].Data);
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Multiline_should_concatenate_data_lines(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/multiline"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Single(events);
        Assert.Equal("line1\nline2\nline3", events[0].Data);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task WithComments_should_skip_comment_lines(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/with-comments"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Single(events);
        Assert.Equal("visible", events[0].Data);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task WithIdRetry_should_parse_all_fields(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/with-id-retry"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Single(events);
        Assert.Equal("payload", events[0].Data);
        Assert.Equal("update", events[0].EventType);
        Assert.Equal("42", events[0].Id);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), events[0].Retry);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Empty_should_produce_no_events(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse/empty"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Empty(events);
    }
}
