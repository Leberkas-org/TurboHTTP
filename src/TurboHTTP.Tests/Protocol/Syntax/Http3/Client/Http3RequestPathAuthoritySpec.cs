using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3RequestPathAuthoritySpec
{
    private static Http3ClientEncoder CreateEncoder()
    {
        return new Http3ClientEncoder(new QpackTableSync());
    }

    private static IReadOnlyList<(string Name, string Value)> DecodeHeaders(Http3ClientEncoder encoder, HttpRequestMessage request)
    {
        var frames = encoder.Encode(request);
        var headersFrame = (HeadersFrame)frames[0];
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        return decoder.Decode(headersFrame.HeaderBlock.Span);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_include_path_with_slash_when_uri_has_no_path()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_preserve_query_string_in_path()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=test&page=1");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/search?q=test&page=1" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_include_authority_from_uri()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value.Contains("example.com"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_include_non_default_port_in_authority()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value.Contains("8443"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_include_scheme_from_uri()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_should_reject_duplicate_method()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"), (":method", "POST"),
            (":path", "/"), (":scheme", "https"), (":authority", "example.com"),
        };
        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_should_reject_duplicate_path()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"), (":path", "/a"), (":path", "/b"),
            (":scheme", "https"), (":authority", "example.com"),
        };
        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void Encode_should_place_pseudo_headers_before_regular_headers()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "text/html");
        var headers = DecodeHeaders(encoder, request);

        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name.StartsWith(':')) lastPseudoIndex = i;
            else if (firstRegularIndex == int.MaxValue) firstRegularIndex = i;
        }
        Assert.True(lastPseudoIndex < firstRegularIndex);
    }
}
