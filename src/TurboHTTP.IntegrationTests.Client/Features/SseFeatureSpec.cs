using System.Net;
using System.Text.Json;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Client;
using Servus.Akka.Sse;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Features;

[Collection("Sse")]
public sealed class SseFeatureSpec : FeatureSpecBase
{
    public SseFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Sse_should_parse_events_from_standard_endpoint(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse?count=3&duration=1s"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Equal(3, events.Count);
        foreach (var evt in events)
        {
            Assert.Equal("ping", evt.EventType);
            var json = JsonDocument.Parse(evt.Data);
            Assert.True(json.RootElement.TryGetProperty("id", out _));
            Assert.True(json.RootElement.TryGetProperty("timestamp", out _));
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Sse_should_receive_correct_event_count(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse?count=5&duration=2s"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        Assert.Equal(5, events.Count);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Sse_should_have_incrementing_ids(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        var materializer = ActorSystem.Materializer();

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/sse?count=3&duration=1s"), CancellationToken);

        var events = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), materializer);

        for (var i = 0; i < events.Count; i++)
        {
            var json = JsonDocument.Parse(events[i].Data);
            Assert.Equal(i, json.RootElement.GetProperty("id").GetInt32());
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(KestrelOnly))]
    public async Task Sse_should_concatenate_multiline_data(ProtocolVariant variant)
    {
        if (!Server.HasCustomEndpoints) Assert.Skip("Custom SSE endpoints not available on this backend.");
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
    [MemberData(nameof(KestrelOnly))]
    public async Task Sse_should_skip_comment_lines(ProtocolVariant variant)
    {
        if (!Server.HasCustomEndpoints) Assert.Skip("Custom SSE endpoints not available on this backend.");
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
    [MemberData(nameof(KestrelOnly))]
    public async Task Sse_should_parse_id_and_retry_fields(ProtocolVariant variant)
    {
        if (!Server.HasCustomEndpoints) Assert.Skip("Custom SSE endpoints not available on this backend.");
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
}
