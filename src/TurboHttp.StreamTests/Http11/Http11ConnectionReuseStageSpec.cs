using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// Tests the connection reuse stage per RFC 9112.
/// Verifies that keep-alive and close decisions are correctly derived from response headers and HTTP version.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ConnectionReuseStage"/>.
/// RFC 9112 §9: HTTP/1.1 persistent connection management and connection reuse eligibility.
/// </remarks>
public sealed class Http11ConnectionReuseStageSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> responses, IReadOnlyList<ConnectionReuseItem> decisions)>
        RunAsync(Version httpVersion, bool bodyFullyConsumed = true, params HttpResponseMessage[] responses)
    {
        // Set version on each response so the stage can read it
        foreach (var r in responses)
        {
            r.Version = httpVersion;
        }

        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage(bodyFullyConsumed));
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

        // Request from both outlets for each response.
        // Both must have demand before the stage pulls upstream.
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

    /// <summary>
    /// Runs the stage with HTTP/1.0 responses where the signal outlet is bypassed.
    /// Returns responses only — the signal outlet should complete with zero elements.
    /// </summary>
    private async Task<IReadOnlyList<HttpResponseMessage>> RunH10BypassAsync(
        bool bodyFullyConsumed = true, params HttpResponseMessage[] responses)
    {
        foreach (var r in responses)
        {
            r.Version = HttpVersion.Version10;
        }

        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage(bodyFullyConsumed));
            var src = b.Add(Source.From(responses));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        // Signal outlet needs initial demand so TryPullIfReady can proceed
        sub1.Request(responses.Length + 1);

        var resultResponses = new List<HttpResponseMessage>();

        for (var i = 0; i < responses.Length; i++)
        {
            sub0.Request(1);
            resultResponses.Add(await probe0.ExpectNextAsync(CancellationToken.None));
        }

        // Signal outlet should complete with zero elements (H10 bypass)
        await probe1.ExpectCompleteAsync(CancellationToken.None);

        return resultResponses;
    }

    private static HttpResponseMessage MakeResponse(
        HttpStatusCode status = HttpStatusCode.OK,
        string? connectionHeader = null,
        string? keepAliveHeader = null)
    {
        var response = new HttpResponseMessage(status);
        if (connectionHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Connection", connectionHeader);
        }
        if (keepAliveHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Keep-Alive", keepAliveHeader);
        }
        return response;
    }

    private static HttpResponseMessage MakeResponseWithRequest(
        Version requestVersion,
        string uri = "https://example.com:443/path",
        HttpStatusCode status = HttpStatusCode.OK,
        string? connectionHeader = null)
    {
        var response = MakeResponse(status, connectionHeader);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(uri))
        {
            Version = requestVersion
        };
        return response;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_always_allow_reuse_when_http2()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version20, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_allow_reuse_when_http11_with_no_connection_header()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_disallow_reuse_when_http11_with_connection_close()
    {
        var response = MakeResponse(connectionHeader: "close");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_bypass_signal_when_http10_with_keep_alive()
    {
        var response = MakeResponse(connectionHeader: "Keep-Alive");

        var results = await RunH10BypassAsync(true, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_bypass_signal_when_http10_with_no_keep_alive()
    {
        var response = MakeResponse();

        var results = await RunH10BypassAsync(true, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_disallow_reuse_when_body_not_fully_consumed()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, bodyFullyConsumed: false, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_disallow_reuse_when_switching_protocols()
    {
        var response = MakeResponse(HttpStatusCode.SwitchingProtocols);

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_pass_response_through_when_processing_reuse()
    {
        var response = MakeResponse(HttpStatusCode.Created);

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_produce_one_decision_per_response_when_multiple_responses()
    {
        var resp1 = MakeResponse(); // 200 → reuse
        var resp2 = MakeResponse(connectionHeader: "close"); // close → no reuse
        var resp3 = MakeResponse(); // 200 → reuse

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Equal(3, decisions.Count);
        Assert.True(decisions[0].Decision.CanReuse);
        Assert.False(decisions[1].Decision.CanReuse);
        Assert.True(decisions[2].Decision.CanReuse);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_parse_keep_alive_parameters_when_http11_keep_alive_header()
    {
        var response = MakeResponse(keepAliveHeader: "timeout=30, max=100");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.Decision.KeepAliveTimeout);
        Assert.Equal(100, decision.Decision.MaxRequests);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_set_correct_endpoint_key_when_http11_response_with_request_message()
    {
        var response = MakeResponseWithRequest(HttpVersion.Version11, "https://api.example.com:8443/resource");

        var (_, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        var decision = Assert.Single(decisions);
        Assert.Equal("api.example.com", decision.Key.Host);
        Assert.Equal(8443, decision.Key.Port);
        Assert.Equal("https", decision.Key.Scheme);
        Assert.Equal(HttpVersion.Version11, decision.Key.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_bypass_signal_and_pass_response_when_http10_response_with_request_message()
    {
        var response = MakeResponseWithRequest(HttpVersion.Version10, "http://legacy.example.com:80/old",
            connectionHeader: "Keep-Alive");

        var results = await RunH10BypassAsync(true, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_set_correct_endpoint_key_when_http20_response_with_request_message()
    {
        var response = MakeResponseWithRequest(HttpVersion.Version20, "https://h2.example.com:443/stream");

        var (_, decisions) = await RunAsync(HttpVersion.Version20, true, response);

        var decision = Assert.Single(decisions);
        Assert.Equal("h2.example.com", decision.Key.Host);
        Assert.Equal(443, decision.Key.Port);
        Assert.Equal("https", decision.Key.Scheme);
        Assert.Equal(HttpVersion.Version20, decision.Key.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_fall_back_to_default_endpoint_key_when_request_message_is_null()
    {
        var response = MakeResponse(); // no RequestMessage set

        var (_, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        var decision = Assert.Single(decisions);
        Assert.Equal(RequestEndpoint.Default, decision.Key);
        Assert.Equal(string.Empty, decision.Key.Host);
        Assert.Equal(HttpVersion.Unknown, decision.Key.Version);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Http11ConnectionReuseStage_should_absorb_upstream_failure_when_upstream_fails()
    {
        var publisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage());
            var src = b.Add(Source.FromPublisher(publisher));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var pubSub = publisher.ExpectSubscription(TestContext.Current.CancellationToken);
        probe0.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);
        probe1.ExpectSubscription(TestContext.Current.CancellationToken).Request(10);

        // Fail upstream — stage absorbs error (no OnError) but completes gracefully
        pubSub.SendError(new Exception("upstream boom"));

        probe0.ExpectComplete(TestContext.Current.CancellationToken);
        probe1.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_bypass_all_signals_when_http10_multiple_responses()
    {
        var resp1 = MakeResponse();
        var resp2 = MakeResponse(connectionHeader: "Keep-Alive");
        var resp3 = MakeResponse(connectionHeader: "close");

        var results = await RunH10BypassAsync(true, resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Same(resp1, results[0]);
        Assert.Same(resp2, results[1]);
        Assert.Same(resp3, results[2]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionReuseStage_should_emit_signal_when_http11_not_bypassed()
    {
        var resp1 = MakeResponse();
        var resp2 = MakeResponse(connectionHeader: "close");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, resp1, resp2);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, decisions.Count);
        Assert.True(decisions[0].Decision.CanReuse);
        Assert.False(decisions[1].Decision.CanReuse);
    }
}
