using System.Net;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http11;

public sealed class Http11ResponseCorrelationSpec : EngineTestBase
{
    private static readonly Func<byte[]> Ok200 = () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http11Engine Engine =>
        new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ResponseCorrelation_should_set_request_message_on_response_when_single_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Ok200);

        Assert.NotNull(response.RequestMessage);
        Assert.Same(request, response.RequestMessage);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ResponseCorrelation_should_correlate_in_order_when_five_sequential_requests()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ResponseCorrelation_should_use_exact_same_reference_when_correlating_request_message()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ResponseCorrelation_should_preserve_correlation_when_fake_tcp_used()
    {
        var engine =
            new Http11Engine(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

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