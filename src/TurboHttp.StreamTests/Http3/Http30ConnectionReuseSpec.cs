using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests HTTP/3 version-dispatch in the connection reuse stage per RFC 9114 §3.3.
/// Verifies that <see cref="ConnectionReuseStage"/> routes HTTP/3+ responses through
/// <see cref="TurboHttp.Protocol.Http3.Http3ConnectionReuseEvaluator"/> instead of
/// <see cref="TurboHttp.Protocol.Http11.ConnectionReuseEvaluator"/>.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ConnectionReuseStage"/>.
/// RFC 9114 §3.3: HTTP/3 connections are identified by (scheme, host, port) and can be
/// reused for same-origin requests. Cross-origin reuse requires certificate coverage.
/// </remarks>
public sealed class Http30ConnectionReuseSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Responses, IReadOnlyList<ConnectionReuseItem> Decisions)>
        RunAsync(params HttpResponseMessage[] responses)
    {
        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage());
            var src = b.Add(Source.From(responses));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        var resultResponses = new List<HttpResponseMessage>();
        var resultDecisions = new List<ConnectionReuseItem>();

        for (var i = 0; i < responses.Length; i++)
        {
            sub0.Request(1);
            sub1.Request(1);
            resultResponses.Add(await probe0.ExpectNextAsync(CancellationToken.None));
            var signal = await probe1.ExpectNextAsync(CancellationToken.None);
            resultDecisions.Add((ConnectionReuseItem)signal);
        }

        return (resultResponses, resultDecisions);
    }

    private static HttpResponseMessage MakeH3Response(
        string uri = "https://example.com/path",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Version = new Version(3, 0),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(uri))
            {
                Version = new Version(3, 0)
            }
        };
        return response;
    }

    // HTTP/3 Version Dispatch (RFC 9114 §3.3)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_use_http3_evaluator_when_response_version_is_30()
    {
        var response = MakeH3Response();

        var (results, decisions) = await RunAsync(response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        // Same-origin with no GOAWAY → reuse allowed
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_produce_keep_alive_when_http3_same_origin_response()
    {
        var response = MakeH3Response("https://api.example.com:443/resource");

        var (_, decisions) = await RunAsync(response);

        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_pass_response_through_when_http3_response()
    {
        var response = MakeH3Response();

        var (results, _) = await RunAsync(response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_set_correct_endpoint_key_when_http3_response()
    {
        var response = MakeH3Response("https://h3.example.com:8443/stream");

        var (_, decisions) = await RunAsync(response);

        var decision = Assert.Single(decisions);
        Assert.Equal("h3.example.com", decision.Key.Host);
        Assert.Equal(8443, decision.Key.Port);
        Assert.Equal("https", decision.Key.Scheme);
        Assert.Equal(new Version(3, 0), decision.Key.Version);
    }

    // Version Dispatch Boundary

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_use_standard_evaluator_when_response_version_is_20()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
            {
                Version = HttpVersion.Version20
            }
        };

        var (_, decisions) = await RunAsync(response);

        var decision = Assert.Single(decisions);
        // HTTP/2 always allows reuse (multiplexed)
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3.3")]
    public async Task Http30ConnectionReuse_should_dispatch_correctly_when_mixed_version_responses()
    {
        var h2Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/h2")
            {
                Version = HttpVersion.Version20
            }
        };

        var h3Response = MakeH3Response("https://example.com/h3");

        var (results, decisions) = await RunAsync(h2Response, h3Response);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, decisions.Count);
        // Both should allow reuse (same-origin, no issues)
        Assert.True(decisions[0].Decision.CanReuse);
        Assert.True(decisions[1].Decision.CanReuse);
    }
}
