using System.Net;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Round-trip pipeline tests for HTTP/1.1 request encoding and response decoding per RFC 9112.
/// Verifies that multiple pipelined requests are matched to their responses in FIFO order.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="Http11EncoderStage"/> and <see cref="Http11DecoderStage"/>.
/// RFC 9112 §9.3: HTTP/1.1 pipeline request ordering and response correlation.
/// </remarks>
public sealed class Http11RoundTripPipeliningTests : EngineTestBase
{
    private static readonly Func<byte[]> Ok200 =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11PL-001: 3 sequential GET requests → 3 responses in FIFO order")]
    public async Task Should_ReturnResponsesInFifoOrder_WhenThreeSequentialGetRequests()
    {
        var requests = Enumerable.Range(1, 3)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/resource/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 3);

        Assert.Equal(3, responses.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, responses[i].StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11PL-002: Each response has correct RequestMessage reference")]
    public async Task Should_SetCorrectRequestMessageReference_WhenPipelined()
    {
        var requests = Enumerable.Range(1, 3)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/item/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 3);

        Assert.Equal(3, responses.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(responses[i].RequestMessage);
            Assert.Same(requests[i], responses[i].RequestMessage);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11PL-003: Mixed methods (GET, POST, DELETE) → correct assignment")]
    public async Task Should_AssignCorrectRequestMessage_WhenMixedMethods()
    {
        var getReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/items");
        var postReq = new HttpRequestMessage(HttpMethod.Post, "http://example.com/items")
        {
            Content = new StringContent("{\"name\":\"test\"}", System.Text.Encoding.UTF8, "application/json")
        };
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/items/42");

        var requests = new[] { getReq, postReq, deleteReq };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 3);

        Assert.Equal(3, responses.Count);
        Assert.Same(getReq, responses[0].RequestMessage);
        Assert.Same(postReq, responses[1].RequestMessage);
        Assert.Same(deleteReq, responses[2].RequestMessage);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11PL-004: 10 requests → all 10 responses received")]
    public async Task Should_ReceiveAllResponses_WhenTenRequestsSent()
    {
        var requests = Enumerable.Range(1, 10)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/page/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 10);

        Assert.Equal(10, responses.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.NotNull(responses[i]);
            Assert.Equal(HttpStatusCode.OK, responses[i].StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11PL-005: Response order matches request order (FIFO guarantee)")]
    public async Task Should_MatchResponseOrderToRequestOrder_WhenFifoGuaranteed()
    {
        var requests = Enumerable.Range(1, 10)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/seq/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 10);

        Assert.Equal(10, responses.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Same(requests[i], responses[i].RequestMessage);
        }
    }
}
