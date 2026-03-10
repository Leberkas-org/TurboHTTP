using System.Net;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9112 §9.3 — HTTP/1.1 Pipelining round-trip tests through Akka.Streams stages.
/// Validates FIFO request-response ordering, correct RequestMessage correlation,
/// and mixed-method pipelining through the Http11Engine (Encoder → FakeTCP → Decoder → Correlation).
/// </summary>
public sealed class Http11StageRoundTripPipelineTests : EngineTestBase
{
    private static readonly Func<byte[]> Ok200 =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http11Engine Engine => new();

    // ── 11RT-P-001: 3 sequential GET requests → 3 responses in FIFO order ───────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3-RT-P-001: 3 sequential GET requests → 3 responses in FIFO order")]
    public async Task _11RT_P_001_Three_Gets_Fifo_Order()
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

    // ── 11RT-P-002: Each response has correct RequestMessage reference ───────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3-RT-P-002: Each response has correct RequestMessage reference")]
    public async Task _11RT_P_002_Each_Response_Has_Correct_RequestMessage()
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

    // ── 11RT-P-003: Mixed methods (GET, POST, DELETE) → correct assignment ───────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3-RT-P-003: Mixed methods (GET, POST, DELETE) → correct assignment")]
    public async Task _11RT_P_003_Mixed_Methods_Correct_Assignment()
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

    // ── 11RT-P-004: 10 requests → all 10 responses received ─────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3-RT-P-004: 10 requests → all 10 responses received")]
    public async Task _11RT_P_004_Ten_Requests_All_Responses_Received()
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

    // ── 11RT-P-005: Response order matches request order (FIFO guarantee) ────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§9.3-RT-P-005: Response order matches request order (FIFO guarantee)")]
    public async Task _11RT_P_005_Response_Order_Matches_Request_Order()
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
