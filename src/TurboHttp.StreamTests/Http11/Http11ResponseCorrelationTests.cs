using System.Net;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

public sealed class Http11ResponseCorrelationTests : EngineTestBase
{
    private static readonly Func<byte[]> Ok200 =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "REQ-001: Single request/response pair — response.RequestMessage set to the originating request")]
    public async Task Single_Request_Response_Correlation()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Ok200);

        Assert.NotNull(response.RequestMessage);
        Assert.Same(request, response.RequestMessage);
    }

    [Fact(Timeout = 10_000, DisplayName = "REQ-002: 5 sequential requests — each response.RequestMessage matches correct in-order request")]
    public async Task Five_Sequential_Requests_InOrder_Correlation()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Ok200, 5);

        Assert.Equal(5, responses.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Same(requests[i], responses[i].RequestMessage);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "REQ-003: response.RequestMessage is the exact same object instance (reference equality)")]
    public async Task RequestMessage_Is_Same_Reference()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("body")
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Ok200);

        // ReferenceEquals ensures it is not a copy — same object in memory
        Assert.True(ReferenceEquals(request, response.RequestMessage),
            "response.RequestMessage must be the exact same object reference as the sent request.");
    }

    [Fact(Timeout = 10_000, DisplayName = "REQ-004: Http11Engine flow with fake TCP — correlation preserved end-to-end")]
    public async Task Http11Engine_FakeTcp_CorrelationPreserved()
    {
        var engine = new Http11Engine();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/one");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/two");
        var request3 = new HttpRequestMessage(HttpMethod.Delete, "http://a.test/three");

        var (responses, _) = await SendManyAsync(engine.CreateFlow(),
            [request1, request2, request3], Ok200, 3);

        Assert.Equal(3, responses.Count);
        Assert.Same(request1, responses[0].RequestMessage);
        Assert.Same(request2, responses[1].RequestMessage);
        Assert.Same(request3, responses[2].RequestMessage);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }
}
