using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHTTP.Client;
using Servus.Akka.Sse;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Client;

public sealed class ExtensionsSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public ExtensionsSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public void GetResponseAsync_should_attach_pending_request_to_options()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var task = request.GetResponseAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(request.Options.TryGetValue(OptionsKey.Key, out var pending));
        Assert.NotNull(pending);
        Assert.True(request.Options.TryGetValue(OptionsKey.VersionKey, out _));
        Assert.False(task.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseAsync_should_cancel_on_cancellation_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var cts = new CancellationTokenSource();

        var task = request.GetResponseAsync(cts.Token);
        Assert.False(task.IsCompleted);

        await cts.CancelAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(task.IsCanceled);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseAsync_should_complete_when_result_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var task = request.GetResponseAsync(ct: TestContext.Current.CancellationToken);

        request.Options.TryGetValue(OptionsKey.Key, out var pending);
        request.Options.TryGetValue(OptionsKey.VersionKey, out var version);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        pending!.TrySetResult(response, version);

        var result = await task;
        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsEventStream_should_parse_sse_from_response_content()
    {
        // Create a response with SSE content
        var content = "data: hello\n\ndata: world\n\n";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
        };

        // Parse the SSE stream
        var result = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Equal(2, result.Count);
        Assert.Equal("hello", result[0].Data);
        Assert.Equal("world", result[1].Data);
    }

    [Fact(Timeout = 5000)]
    public async Task AsEventStream_should_parse_events_with_all_fields()
    {
        var content = "event: update\ndata: payload\nid: 42\nretry: 3000\n\n";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
        };

        var result = await response.AsEventStream()
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(result);
        Assert.Equal("payload", result[0].Data);
        Assert.Equal("update", result[0].EventType);
        Assert.Equal("42", result[0].Id);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), result[0].Retry);
    }
}