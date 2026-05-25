using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2ServerTrailerEncodingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void TrailerFeature_should_store_and_retrieve_trailer_headers()
    {
        var feature = new TurboHttpResponseTrailersFeature();

        feature.Trailers["grpc-status"] = "0";
        feature.Trailers["grpc-message"] = "OK";

        Assert.Equal("0", feature.Trailers["grpc-status"]);
        Assert.Equal("OK", feature.Trailers["grpc-message"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerFeature_should_reject_prohibited_trailer_fields()
    {
        var feature = new TurboHttpResponseTrailersFeature();

        feature.Trailers["transfer-encoding"] = "chunked";

        Assert.DoesNotContain(feature.GetAllowedTrailers(),
            h => h.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void TurboHttpResponse_should_expose_DeclareTrailer_and_AppendTrailer()
    {
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseTrailersFeature>(new TurboHttpResponseTrailersFeature());

        var response = new TurboHttpResponse(features);
        var httpContext = new DefaultHttpContext(features);
        response.SetHttpContext(httpContext);

        response.DeclareTrailer("grpc-status");
        response.AppendTrailer("grpc-status", "0");
        response.AppendTrailer("grpc-message", "OK");

        var trailers = response.GetTrailers();

        Assert.Equal("0", trailers["grpc-status"]);
        Assert.Equal("OK", trailers["grpc-message"]);
        Assert.Contains("grpc-status", response.Headers["Trailer"].ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Encoder_should_produce_trailing_HEADERS_frame_with_END_STREAM()
    {
        var encoder = new Http2ServerEncoder();
        var trailers = new HeaderDictionary
        {
            { "grpc-status", "0" },
            { "grpc-message", "OK" }
        };

        var frames = encoder.EncodeTrailers(streamId: 1, trailers);

        Assert.NotEmpty(frames);
        var lastFrame = frames[^1];
        Assert.IsType<HeadersFrame>(lastFrame);
        var headersFrame = (HeadersFrame)lastFrame;
        Assert.True(headersFrame.EndStream, "Trailer HEADERS frame should have EndStream=true");
        Assert.True(headersFrame.EndHeaders, "Trailer HEADERS frame should have EndHeaders=true");
        Assert.Equal(1, headersFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void Encoder_should_filter_prohibited_trailer_fields()
    {
        var encoder = new Http2ServerEncoder();
        var decoder = new HpackDecoder();

        var trailers = new HeaderDictionary
        {
            { "grpc-status", "0" },
            { "transfer-encoding", "chunked" },
            { "content-length", "42" }
        };

        var frames = encoder.EncodeTrailers(streamId: 1, trailers);

        Assert.NotEmpty(frames);

        var headerBlockBytes = new List<byte>();
        foreach (var frame in frames)
        {
            if (frame is HeadersFrame hf)
            {
                headerBlockBytes.AddRange(hf.HeaderBlockFragment.Span.ToArray());
            }
            else if (frame is ContinuationFrame cf)
            {
                headerBlockBytes.AddRange(cf.HeaderBlockFragment.Span.ToArray());
            }
        }

        var decodedHeaders = decoder.Decode(headerBlockBytes.ToArray());

        var grpcStatusHeader = decodedHeaders.FirstOrDefault(h => h.Name == "grpc-status");
        Assert.NotNull(grpcStatusHeader.Name);
        Assert.Equal("0", grpcStatusHeader.Value);

        Assert.DoesNotContain(decodedHeaders, h => h.Name == "transfer-encoding");
        Assert.DoesNotContain(decodedHeaders, h => h.Name == "content-length");
    }
}
