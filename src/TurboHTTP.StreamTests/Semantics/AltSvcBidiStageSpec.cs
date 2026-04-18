using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.AltSvc;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Semantics;

public sealed class AltSvcBidiStageSpec : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        AltSvcBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        AltSvcBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_pass_through_request_when_no_http3_cached()
    {
        var cache = new AltSvcCache();
        var stage = new AltSvcBidiStage(cache);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version11, result.Version);
        Assert.Same(request, result);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_upgrade_to_http3_when_cached()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [new AltSvcEntry("h3", "", 443, 86400, false, DateTimeOffset.UtcNow.AddHours(1))]);

        var stage = new AltSvcBidiStage(cache);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version30, result.Version);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_update_port_when_cached_with_different_port()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [new AltSvcEntry("h3", "", 8443, 86400, false, DateTimeOffset.UtcNow.AddHours(1))]);

        var stage = new AltSvcBidiStage(cache);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/")
        {
            Version = HttpVersion.Version11
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version30, result.Version);
        Assert.Equal(8443, result.RequestUri!.Port);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_update_host_when_cached_with_different_host()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com",
            [new AltSvcEntry("h3", "alt.example.com", 443, 86400, false, DateTimeOffset.UtcNow.AddHours(1))]);

        var stage = new AltSvcBidiStage(cache);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version30, result.Version);
        Assert.Equal("alt.example.com", result.RequestUri!.Host);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_parse_and_cache_alt_svc_header()
    {
        var cache = new AltSvcCache();
        var stage = new AltSvcBidiStage(cache);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        response.Headers.TryAddWithoutValidation("Alt-Svc", "h3=\":443\"; ma=3600");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
        Assert.True(cache.TryGetHttp3("example.com", out _));
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_pass_through_response_without_alt_svc_header()
    {
        var cache = new AltSvcCache();
        var stage = new AltSvcBidiStage(cache);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_clear_cache_when_alt_svc_clear()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [new AltSvcEntry("h3", "", 443, 86400, false, DateTimeOffset.UtcNow.AddHours(1))]);

        Assert.True(cache.TryGetHttp3("example.com", out _));

        var stage = new AltSvcBidiStage(cache);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        response.Headers.TryAddWithoutValidation("Alt-Svc", "clear");

        await RunResponseAsync(stage, response);

        Assert.False(cache.TryGetHttp3("example.com", out _));
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_handle_response_without_request_message()
    {
        var cache = new AltSvcCache();
        var stage = new AltSvcBidiStage(cache);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Alt-Svc", "h3=\":443\"");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_not_upgrade_if_already_http3()
    {
        var cache = new AltSvcCache();
        cache.Store("example.com", [new AltSvcEntry("h3", "", 443, 86400, false, DateTimeOffset.UtcNow.AddHours(1))]);

        var stage = new AltSvcBidiStage(cache);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version30
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Equal(HttpVersion.Version30, result.Version);
    }

    [Trait("RFC", "RFC7838")]
    [Fact(Timeout = 5000)]
    public async Task AltSvcBidiStage_should_handle_multiple_alt_svc_values()
    {
        var cache = new AltSvcCache();
        var stage = new AltSvcBidiStage(cache);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        response.Headers.TryAddWithoutValidation("Alt-Svc", "h3=\":443\"; ma=3600");
        response.Headers.TryAddWithoutValidation("Alt-Svc", "h2=\":443\"; ma=3600");

        var results = await RunResponseAsync(stage, response);

        Assert.Single(results);
    }
}