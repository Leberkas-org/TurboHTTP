using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Semantics.Stages;

public sealed class ContentEncodingDoubleDisposeSpec : StreamTestBase
{
    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        ContentEncodingBidiStage stage,
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

    [Fact(Timeout = 5_000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task ContentEncodingBidiStage_should_pass_through_response_when_gzip_body_is_corrupt()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);

        // Invalid gzip data — will cause decompression to throw, exercising the
        // catch+finally double-dispose path
        var corruptGzipBody = new byte[] { 0x1F, 0x8B, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(corruptGzipBody),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/corrupt")
        };
        response.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");

        var results = await RunResponseAsync(stage, response);

        // The stage should pass the raw response through on decompression failure
        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact(Timeout = 5_000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task ContentEncodingBidiStage_should_pass_through_multiple_corrupt_responses()
    {
        var stage = new ContentEncodingBidiStage(automaticDecompression: true);

        var responses = Enumerable.Range(0, 5).Select(i =>
        {
            var corruptBody = new byte[] { 0x1F, 0x8B, 0xDE, 0xAD, (byte)i };
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(corruptBody),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/corrupt/{i}")
            };
            resp.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
            return resp;
        }).ToArray();

        // Multiple corrupt responses exercise the double-dispose path repeatedly.
        // If the double dispose corrupts pool state, later iterations may crash.
        var results = await RunResponseAsync(stage, responses);

        Assert.Equal(5, results.Count);
    }
}
