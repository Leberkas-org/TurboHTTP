using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

public sealed class EngineVersionRoutingTests : EngineTestBase
{
    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpTransportFlow()
    {
        return Flow.FromGraph(new H2EngineFakeConnectionStage());
    }

    [Fact(Timeout = 10_000, DisplayName = "EROUTE-001: HTTP/1.0 request routed through Http10Engine")]
    public async Task Http10RequestRoutedThroughHttp10Engine()
    {
        var http10Response = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var http11Response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var h2Frames = new[] { new SettingsFrame([]).Serialize() };

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http10Response)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http11Response)),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(h2Frames)),
            NoOpTransportFlow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var response = await RunEngineAsync(flow, request);

        Assert.Equal(HttpVersion.Version10, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "EROUTE-002: HTTP/1.1 request routed through Http11Engine")]
    public async Task Http11RequestRoutedThroughHttp11Engine()
    {
        var http10Response = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var http11Response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var h2Frames = new[] { new SettingsFrame([]).Serialize() };

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http10Response)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http11Response)),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(h2Frames)),
            NoOpTransportFlow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunEngineAsync(flow, request);

        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "EROUTE-003: HTTP/2.0 request routed through Http20Engine")]
    public async Task Http20RequestRoutedThroughHttp20Engine()
    {
        var http10Response = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var http11Response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var hpack = new HpackEncoder(useHuffman: false);
        var settingsFrame = new SettingsFrame([]).Serialize();
        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: hpack.Encode([(":status", "200")]),
            endStream: true,
            endHeaders: true).Serialize();
        var h2Frames = new[] { settingsFrame, headersFrame };

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http10Response)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http11Response)),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(h2Frames)),
            NoOpTransportFlow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var response = await RunEngineAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "EROUTE-004: Mixed HTTP versions each routed to correct engine")]
    public async Task MixedVersionsEachRoutedToCorrectEngine()
    {
        var http10Response = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var http11Response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var h2Frames = new[] { new SettingsFrame([]).Serialize() };

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http10Response)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(() => http11Response)),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(h2Frames)),
            NoOpTransportFlow);

        var request10 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1")
        {
            Version = HttpVersion.Version10
        };

        var request11 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2")
        {
            Version = HttpVersion.Version11
        };

        var requests = new[] { request10, request11 };
        var responses = await RunEngineManyAsync(flow, requests);

        Assert.Equal(2, responses.Count);

        var response10 = responses.FirstOrDefault(r => r.Version == HttpVersion.Version10);
        var response11 = responses.FirstOrDefault(r => r.Version == HttpVersion.Version11);

        Assert.NotNull(response10);
        Assert.NotNull(response11);
    }

    private async Task<HttpResponseMessage> RunEngineAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var task = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        return await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private async Task<List<HttpResponseMessage>> RunEngineManyAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        IEnumerable<HttpRequestMessage> requests)
    {
        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count >= 2)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        // Wait for at least 2 responses or timeout
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // If we timed out but got some responses, return what we have
            if (results.Count == 0)
            {
                throw;
            }
        }

        return results;
    }
}