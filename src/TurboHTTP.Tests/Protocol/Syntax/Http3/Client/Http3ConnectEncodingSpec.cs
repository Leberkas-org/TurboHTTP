using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3ConnectEncodingSpec
{
    private static IReadOnlyList<(string Name, string Value)> DecodeHeaders(Http3ClientEncoder encoder, HttpRequestMessage request)
    {
        var frames = encoder.Encode(request);
        var headersFrame = (HeadersFrame)frames[0];
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        return decoder.Decode(headersFrame.HeaderBlock.Span);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_should_omit_scheme_when_connect()
    {
        var encoder = new Http3ClientEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:443/");
        var headers = DecodeHeaders(encoder, request);
        Assert.DoesNotContain(headers, h => h.Name == ":scheme");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_should_omit_path_when_connect()
    {
        var encoder = new Http3ClientEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:443/");
        var headers = DecodeHeaders(encoder, request);
        Assert.DoesNotContain(headers, h => h.Name == ":path");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_should_include_authority_with_port_when_connect()
    {
        var encoder = new Http3ClientEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value.Contains("8443"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_should_include_method_connect_when_connect()
    {
        var encoder = new Http3ClientEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:443/");
        var headers = DecodeHeaders(encoder, request);
        Assert.Contains(headers, h => h is { Name: ":method", Value: "CONNECT" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public void ErrorCode_should_define_connect_error()
    {
        Assert.Equal(0x10f, (int)ErrorCode.ConnectError);
    }
}
